using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MMW.Application.Services;
using MMW.Domain.DbContext;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.TimeGuard;

/// <summary>
/// T061 / FR-013 — vị thế đang mở khi sắp vào cửa sổ chặn KHÔNG được để trần.
/// </summary>
/// <remarks>
/// Khẳng định quan trọng nhất của tệp này là khẳng định phủ định: KHÔNG nhánh nào để nguyên
/// trạng. Đứng ngoài không vào lệnh mới lúc CPI ra mà vẫn ôm nguyên vị thế cũ thì cả tầng chặn
/// theo khung giờ chẳng tránh được gì — nó chỉ tạo cảm giác đã phòng thủ.
///
/// Mốc thử: CPI 2026-08-05 13:30 UTC, cửa sổ [12:30, 14:00). Chạy lúc 12:20, tức còn 10 phút
/// nữa vào cửa sổ, nằm trong tầm nhìn trước 15 phút mặc định.
/// </remarks>
public class PositionManageTests
{
    private static readonly DateTime EventAt = new(2026, 8, 5, 13, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime WindowFrom = new(2026, 8, 5, 12, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime RunAt = new(2026, 8, 5, 12, 20, 0, DateTimeKind.Utc);

    private const decimal Entry = 100m;
    private const decimal Stop = 90m;     // rủi ro 10 giá = 1R

    private static async Task<TimeGuardHarness> HarnessAsync(
        decimal price, Action<EngineSetting>? configure = null, ScheduledEventKind kind = ScheduledEventKind.Cpi)
    {
        var harness = await TimeGuardHarness.CreateAsync(configure);
        harness.MarketData.Prices["BTCUSDT"] = price;

        await harness.AddEventsAsync(new ScheduledEvent
        {
            Kind = kind,
            Title = kind == ScheduledEventKind.Cpi ? "CPI tháng 8" : kind.ToString(),
            OccursAtUtc = EventAt,
            Impact = MacroEventImpact.Critical,
            Origin = ScheduledEventOrigin.Seeded,
            SourceKey = $"test:{kind}",
        });

        return harness;
    }

    private static Trade OpenTrade(long accountId, decimal? stop = Stop, string symbol = "BTCUSDT") => new()
    {
        TradingAccountId = accountId,
        Symbol = symbol,
        Direction = TradeDirection.Long,
        Status = TradeStatus.Open,
        EntryPrice = Entry,
        StopLoss = stop,
        Quantity = 1m,
        OpenedAt = new DateTime(2026, 8, 5, 10, 0, 0, DateTimeKind.Utc),
    };

    private static async Task<IReadOnlyList<PositionAction>> RunAsync(TimeGuardHarness harness, DateTime? at = null)
    {
        using var scope = harness.NewScope();
        var service = scope.ServiceProvider.GetRequiredService<IPositionManageService>();
        return await service.RunAsync(harness.AccountId, at ?? RunAt);
    }

    // ── Quyết định theo mức lãi ─────────────────────────────────────────

    [Fact]
    public async Task Lai_tu_nua_R_tro_len_thi_keo_dung_lo_ve_hoa_von()
    {
        using var harness = await HarnessAsync(price: 108m);          // +0,8R
        await harness.AddClosedTradesAsync(new[] { OpenTrade(harness.AccountId) });

        var action = Assert.Single(await RunAsync(harness));

        Assert.Equal(PositionActionKind.MoveStopToBreakeven, action.Kind);
        Assert.Equal(0.8m, action.RMultiple);
    }

    [Fact]
    public async Task Keo_ve_hoa_von_ghi_that_vao_lenh()
    {
        // Quyết định mà không ghi lại thì lần chạy sau lại quyết định y hệt, và dừng lỗ thật
        // vẫn nằm nguyên chỗ cũ.
        using var harness = await HarnessAsync(price: 108m);
        await harness.AddClosedTradesAsync(new[] { OpenTrade(harness.AccountId) });

        await RunAsync(harness);

        using var scope = harness.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();
        var trade = await db.Trades.SingleAsync();

        Assert.Equal(Entry, trade.StopLoss);
    }

    [Fact]
    public async Task Dung_nguong_nua_R_van_tinh_la_du_lai()
    {
        using var harness = await HarnessAsync(price: 105m);          // đúng +0,5R
        await harness.AddClosedTradesAsync(new[] { OpenTrade(harness.AccountId) });

        var action = Assert.Single(await RunAsync(harness));

        Assert.Equal(PositionActionKind.MoveStopToBreakeven, action.Kind);
    }

    [Fact]
    public async Task Lai_duoi_nguong_thi_dong_bot()
    {
        using var harness = await HarnessAsync(price: 102m);          // +0,2R
        await harness.AddClosedTradesAsync(new[] { OpenTrade(harness.AccountId) });

        var action = Assert.Single(await RunAsync(harness));

        Assert.Equal(PositionActionKind.ClosePartial, action.Kind);
        Assert.Equal(0.2m, action.RMultiple);
    }

    [Fact]
    public async Task Dang_lo_thi_dong_bot()
    {
        using var harness = await HarnessAsync(price: 95m);           // −0,5R
        await harness.AddClosedTradesAsync(new[] { OpenTrade(harness.AccountId) });

        var action = Assert.Single(await RunAsync(harness));

        Assert.Equal(PositionActionKind.ClosePartial, action.Kind);
        Assert.Equal(-0.5m, action.RMultiple);
    }

    [Fact]
    public async Task Nguong_hoa_von_lay_tu_cau_hinh_chu_khong_viet_cung()
    {
        using var harness = await HarnessAsync(price: 108m, configure: s => s.BlackoutBreakevenAtR = 1.0m);
        await harness.AddClosedTradesAsync(new[] { OpenTrade(harness.AccountId) });

        var action = Assert.Single(await RunAsync(harness));

        Assert.Equal(PositionActionKind.ClosePartial, action.Kind);   // 0,8R < 1,0R
    }

    // ── Không nhánh nào để nguyên trạng ─────────────────────────────────

    [Fact]
    public async Task Thieu_dung_lo_van_phai_hanh_dong_va_hanh_dong_an_toan_hon()
    {
        // Không tính được R không phải lý do để bỏ mặc vị thế trần khi tin sắp ra.
        using var harness = await HarnessAsync(price: 108m);
        await harness.AddClosedTradesAsync(new[] { OpenTrade(harness.AccountId, stop: null) });

        var action = Assert.Single(await RunAsync(harness));

        Assert.Equal(PositionActionKind.ClosePartial, action.Kind);
        Assert.Null(action.RMultiple);
    }

    [Fact]
    public async Task Khong_lay_duoc_gia_van_phai_hanh_dong()
    {
        using var harness = await HarnessAsync(price: 108m);
        harness.MarketData.Prices.Clear();                            // sàn không trả giá
        await harness.AddClosedTradesAsync(new[] { OpenTrade(harness.AccountId) });

        var action = Assert.Single(await RunAsync(harness));

        Assert.Equal(PositionActionKind.ClosePartial, action.Kind);
    }

    [Fact]
    public async Task Moi_vi_the_dang_mo_deu_nhan_dung_mot_hanh_dong()
    {
        // Đây là khẳng định trung tâm của FR-013: đếm hành động phải bằng đếm vị thế mở.
        using var harness = await HarnessAsync(price: 108m);
        harness.MarketData.Prices["ETHUSDT"] = 95m;

        await harness.AddClosedTradesAsync(new[]
        {
            OpenTrade(harness.AccountId),                                        // +0,8R
            OpenTrade(harness.AccountId, symbol: "ETHUSDT"),                     // −0,5R
            OpenTrade(harness.AccountId, stop: null),                            // không tính được R
        });

        var actions = await RunAsync(harness);

        Assert.Equal(3, actions.Count);
        Assert.Equal(3, actions.Select(a => a.TradeId).Distinct().Count());
        Assert.All(actions, a => Assert.False(string.IsNullOrWhiteSpace(a.ReasonVi)));
    }

    // ── Khi nào KHÔNG chạy ──────────────────────────────────────────────

    [Fact]
    public async Task Chua_toi_tam_nhin_truoc_thi_chua_dong_gi()
    {
        // 11:00 còn cách cửa sổ 12:30 tận 90 phút, ngoài tầm nhìn 15 phút.
        using var harness = await HarnessAsync(price: 108m);
        await harness.AddClosedTradesAsync(new[] { OpenTrade(harness.AccountId) });

        Assert.Empty(await RunAsync(harness, new DateTime(2026, 8, 5, 11, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task Tam_nhin_truoc_lay_tu_cau_hinh()
    {
        using var harness = await HarnessAsync(price: 108m, configure: s => s.BlackoutLeadMinutes = 120);
        await harness.AddClosedTradesAsync(new[] { OpenTrade(harness.AccountId) });

        Assert.Single(await RunAsync(harness, new DateTime(2026, 8, 5, 11, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task Cua_so_khong_buoc_xu_ly_vi_the_thi_de_yen()
    {
        // Thanh toán phí vốn không buộc làm phẳng vị thế — làm phẳng ba lần mỗi ngày thì
        // không còn giữ được lệnh nào qua 1–4 tiếng như thiết kế.
        using var harness = await TimeGuardHarness.CreateAsync();
        harness.MarketData.Prices["BTCUSDT"] = 108m;
        await harness.AddClosedTradesAsync(new[] { OpenTrade(harness.AccountId) });

        // 07:50 — mốc phí vốn 08:00 còn 10 phút, cửa sổ [07:55, 08:05).
        Assert.Empty(await RunAsync(harness, new DateTime(2026, 8, 5, 7, 50, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task Lenh_da_dong_va_lenh_moi_len_ke_hoach_khong_bi_dung_toi()
    {
        using var harness = await HarnessAsync(price: 108m);
        await harness.AddClosedTradesAsync(new[]
        {
            new Trade
            {
                TradingAccountId = harness.AccountId, Symbol = "BTCUSDT",
                Direction = TradeDirection.Long, Status = TradeStatus.Closed,
                EntryPrice = Entry, StopLoss = Stop, Quantity = 1m,
                OpenedAt = new DateTime(2026, 8, 4, 10, 0, 0, DateTimeKind.Utc),
                ClosedAt = new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc),
                Outcome = TradeOutcome.Win,
            },
            new Trade
            {
                TradingAccountId = harness.AccountId, Symbol = "BTCUSDT",
                Direction = TradeDirection.Long, Status = TradeStatus.Planned,
                EntryPrice = Entry, StopLoss = Stop, Quantity = 1m,
            },
        });

        Assert.Empty(await RunAsync(harness));
    }

    // ── Thông báo và lệch đồng hồ ───────────────────────────────────────

    [Fact]
    public async Task Co_hanh_dong_thi_phat_thong_bao_cho_trader()
    {
        using var harness = await HarnessAsync(price: 108m);
        await harness.AddClosedTradesAsync(new[] { OpenTrade(harness.AccountId) });

        await RunAsync(harness);

        using var scope = harness.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();

        Assert.True(await db.Notifications.AnyAsync(n => n.Type == NotificationType.TradeRiskWarning));
    }

    [Fact]
    public async Task Dong_ho_lech_san_qua_nguong_thi_canh_bao()
    {
        using var harness = await HarnessAsync(price: 108m);
        harness.MarketData.ExchangeTimeUtc = RunAt.AddSeconds(-45);   // lệch 45 giây, ngưỡng 30
        await harness.AddClosedTradesAsync(new[] { OpenTrade(harness.AccountId) });

        await RunAsync(harness);

        using var scope = harness.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();

        Assert.True(await db.Notifications.AnyAsync(n => n.Type == NotificationType.SystemHealth));
    }

    [Fact]
    public async Task Dong_ho_lech_trong_nguong_thi_khong_canh_bao()
    {
        using var harness = await HarnessAsync(price: 108m);
        harness.MarketData.ExchangeTimeUtc = RunAt.AddSeconds(-5);
        await harness.AddClosedTradesAsync(new[] { OpenTrade(harness.AccountId) });

        await RunAsync(harness);

        using var scope = harness.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();

        Assert.False(await db.Notifications.AnyAsync(n => n.Type == NotificationType.SystemHealth));
    }
}
