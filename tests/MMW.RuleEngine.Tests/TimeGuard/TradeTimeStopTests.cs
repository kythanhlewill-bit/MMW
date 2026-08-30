using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MMW.Application.Interfaces;
using MMW.Application.MarketData;
using MMW.Application.Services;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Shared.Interfaces;
using Xunit;

namespace MMW.RuleEngine.Tests.TimeGuard;

/// <summary>
/// Dừng theo THỜI GIAN: một vị thế không được sống lâu hơn lập luận đã mở nó.
/// </summary>
/// <remarks>
/// Engine vốn có ba đường thoát lệnh — chạm dừng lỗ, chạm chốt lời, và hết hạn lệnh CHỜ. Không
/// đường nào chạm tới một vị thế ĐÃ KHỚP mà giá cứ đi ngang, nên trước 2026-08-30 một vị thế
/// như thế nằm lại vô hạn.
///
/// Cái giá không nằm trong P&amp;L của chính nó mà nằm ở chỗ nó CHIẾM CHỖ. Với
/// <c>MaxConcurrentPositions = 2</c> và cổng chống trùng chặn cả các mã cùng tài sản gốc, hai vị
/// thế đứng im ngày 29–30/08 (#57 mở 4 ngày, #72 mở 2 ngày) đã veto 323/476 phiếu bằng lý do
/// <c>PositionAlreadyOpen</c>: engine chấm 476 phiếu và vào 0 lệnh.
/// </remarks>
public class TradeTimeStopTests
{
    private static readonly DateTime OpenedAt = new(2026, 8, 26, 14, 0, 0, DateTimeKind.Utc);

    private static Trade LiveTrade(
        long accountId,
        TradeStyle style = TradeStyle.Intraday,
        LiveOrderStatus liveStatus = LiveOrderStatus.Submitted,
        TradeStatus status = TradeStatus.Open,
        DateTime? openedAt = null) => new()
    {
        TradingAccountId = accountId,
        Symbol = "ETHUSDT",
        Direction = TradeDirection.Long,
        Status = status,
        LiveStatus = liveStatus,
        Style = style,
        IsLive = true,
        EntryPrice = 2500m,
        StopLoss = 2480m,
        Quantity = 1m,
        OpenedAt = openedAt ?? OpenedAt,
    };

    /// <remarks>
    /// Dựng dịch vụ bằng tay thay vì lấy từ DI vì bộ khung không cấu hình
    /// <c>LiveTradingOptions</c>, mà cờ <c>Enabled</c> mặc định là TẮT — lấy từ DI thì mọi test
    /// đều "đúng" bằng cách không làm gì cả. Đây cũng chính là bẫy cấu hình đã từng làm cả một
    /// ngày chạy thử không sinh lệnh nào.
    /// </remarks>
    private static async Task<int> RunAsync(TimeGuardHarness harness, bool liveEnabled = true)
    {
        using var scope = harness.NewScope();
        var service = new TradeTimeStopService(
            scope.ServiceProvider.GetRequiredService<IBaseRepository<Trade>>(),
            scope.ServiceProvider.GetRequiredService<IBaseRepository<EngineSetting>>(),
            harness.LiveOrders,
            scope.ServiceProvider.GetRequiredService<INotificationService>(),
            Options.Create(new LiveTradingOptions { Enabled = liveEnabled }),
            NullLogger<TradeTimeStopService>.Instance);

        return await service.RunAsync();
    }

    /// <summary>Cờ chạy thật TẮT thì không đóng gì — mọi lớp gửi lệnh đều phải sau cổng đó.</summary>
    [Fact]
    public async Task Co_chay_that_tat_thi_khong_dong_gi()
    {
        using var harness = await HarnessAsync(ageHours: 500);

        Assert.Equal(0, await RunAsync(harness, liveEnabled: false));
    }

    /// <summary>
    /// Sàn không với tới được thì KHÔNG được đếm là đã đóng.
    /// </summary>
    /// <remarks>
    /// <c>CloseOnExchangeAsync</c> nuốt lỗi sàn để một vị thế hỏng không giết cả vòng job, nên
    /// nó trả về kết quả và người gọi phải đọc. Không đọc thì ta ghi nhật ký và bắn thông báo
    /// nói vị thế đã đóng trong khi nó vẫn đang mở — đúng chuyện đã xảy ra ngày 30/08/2026 lúc
    /// 18:30, khi Binance đang cấm IP và lệnh đóng lệnh #72 chưa bao giờ rời máy.
    ///
    /// Không cần trạng thái riêng để nhớ việc dở dang: điều kiện quá hạn vẫn đúng ở vòng sau.
    /// </remarks>
    [Fact]
    public async Task San_khong_voi_toi_duoc_thi_khong_dem_la_da_dong()
    {
        using var harness = await HarnessAsync(ageHours: 25);
        harness.LiveOrders.FailToClose = true;

        Assert.Equal(0, await RunAsync(harness));
        Assert.Empty(harness.LiveOrders.ClosedTradeIds);
    }

    /// <summary>
    /// Bộ khung dựng vị thế với TUỔI cho trước, tính ngược từ hiện tại.
    /// </summary>
    /// <remarks>
    /// Dịch vụ đọc <c>DateTime.UtcNow</c> chứ không nhận thời điểm qua tham số, nên test phải
    /// dựng tuổi thay vì dựng mốc. Đổi lại, nó đo đúng thứ chạy thật.
    /// </remarks>
    private static async Task<TimeGuardHarness> HarnessAsync(
        double ageHours, TradeStyle style = TradeStyle.Intraday,
        LiveOrderStatus liveStatus = LiveOrderStatus.Submitted,
        bool isLive = true,
        TradeStatus status = TradeStatus.Open,
        Action<EngineSetting>? configure = null)
    {
        var harness = await TimeGuardHarness.CreateAsync(configure);
        var trade = LiveTrade(harness.AccountId, style, liveStatus, status,
            openedAt: DateTime.UtcNow.AddHours(-ageHours));
        trade.IsLive = isLive;
        await harness.AddClosedTradesAsync(new[] { trade });
        return harness;
    }

    // ── Quá hạn thì đóng ────────────────────────────────────────────────

    [Fact]
    public async Task Lenh_trong_phien_qua_24_gio_thi_bi_dong()
    {
        using var harness = await HarnessAsync(ageHours: 25);

        Assert.Equal(1, await RunAsync(harness));
        Assert.Single(harness.LiveOrders.ClosedTradeIds);
    }

    [Fact]
    public async Task Lenh_trong_phien_chua_toi_han_thi_de_yen()
    {
        using var harness = await HarnessAsync(ageHours: 23);

        Assert.Equal(0, await RunAsync(harness));
        Assert.Empty(harness.LiveOrders.ClosedTradeIds);
    }

    /// <summary>
    /// Lệnh swing có hạn rộng hơn hẳn — cùng một tuổi mà lệnh trong phiên đã quá hạn.
    /// </summary>
    /// <remarks>
    /// Đây là lý do hạn phải tính theo <see cref="TradeStyle"/> chứ không phải một con số chung:
    /// lệnh trong phiên chấm trên nến 15 phút, lệnh swing chấm trên cấu trúc 4 giờ và cần nhiều
    /// ngày để đi hết mục tiêu. Một con số chung sẽ hoặc cắt cụt lệnh swing, hoặc thả lỏng lệnh
    /// trong phiên — và lệnh #57 (swing, mở 4 ngày) đúng là lệnh KHÔNG nên bị cắt vì tuổi.
    /// </remarks>
    [Fact]
    public async Task Lenh_swing_cung_tuoi_do_thi_van_con_han()
    {
        using var harness = await HarnessAsync(ageHours: 25, style: TradeStyle.HtfSwing);

        Assert.Equal(0, await RunAsync(harness));
    }

    [Fact]
    public async Task Lenh_swing_qua_120_gio_thi_cung_bi_dong()
    {
        using var harness = await HarnessAsync(ageHours: 121, style: TradeStyle.HtfSwing);

        Assert.Equal(1, await RunAsync(harness));
    }

    // ── Cái không được đụng tới ─────────────────────────────────────────

    /// <summary>
    /// Lệnh CHỜ chưa khớp có đường xử lý riêng — không được đụng vào.
    /// </summary>
    /// <remarks>
    /// <c>LiveOrderService.ReconcilePendingEntriesAsync</c> đã quản hạn của lệnh chờ. Hai nơi
    /// cùng huỷ một lệnh thì nơi thua cuộc sẽ huỷ nhầm một vị thế vừa khớp — và một vị thế bị
    /// đóng bằng lệnh thị trường ngay sau khi vào là mất trọn phí hai chiều mà không có gì bù.
    /// </remarks>
    [Fact]
    public async Task Lenh_cho_chua_khop_khong_bi_dung_toi()
    {
        using var harness = await HarnessAsync(ageHours: 200, liveStatus: LiveOrderStatus.EntryPending);

        Assert.Equal(0, await RunAsync(harness));
        Assert.Empty(harness.LiveOrders.ClosedTradeIds);
    }

    [Fact]
    public async Task Lenh_da_dong_khong_bi_dung_toi()
    {
        using var harness = await HarnessAsync(ageHours: 200, status: TradeStatus.Closed);

        Assert.Equal(0, await RunAsync(harness));
    }

    [Fact]
    public async Task Lenh_chi_ghi_nhat_ky_khong_bi_dung_toi()
    {
        using var harness = await HarnessAsync(ageHours: 200, isLive: false);

        Assert.Equal(0, await RunAsync(harness));
    }

    // ── Tắt được ────────────────────────────────────────────────────────

    /// <remarks>
    /// Số 0 nghĩa là không giới hạn. Đây là đường lui khi một bộ luật mới cần vị thế sống lâu
    /// hơn, và nó phải là sửa cấu hình chứ không phải sửa mã.
    /// </remarks>
    [Fact]
    public async Task Han_bang_khong_thi_khong_gioi_han()
    {
        using var harness = await HarnessAsync(
            ageHours: 500, configure: s => s.MaxHoldingHoursIntraday = 0);

        Assert.Equal(0, await RunAsync(harness));
    }
}
