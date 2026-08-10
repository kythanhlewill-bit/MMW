using MMW.Application.Backtest;
using MMW.Application.MarketData.Models;
using MMW.Application.Trading.Execution;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.Backtest;

/// <summary>
/// Bước 2 — mô hình khớp lệnh limit và hết hạn chờ.
/// </summary>
/// <remarks>
/// Nến không có sổ lệnh, nên vị trí hàng đợi phải là một giả định tường minh chứ không phải một
/// mặc định lặng lẽ. Hai biên được cài đặt ở đây:
///
/// • LẠC QUAN (<c>BacktestLimitFillRequiresThrough = false</c>) — chạm là khớp, coi như lệnh
///   luôn đứng đầu hàng đợi. Đây là biên TRÊN.
/// • THẬN TRỌNG (<c>true</c>) — phải xuyên qua, coi như luôn phải đợi hết phần xếp trước.
///
/// Điều kiện chấp nhận của V2 đòi kết quả đứng vững ở CẢ HAI. Một cải tiến chỉ tồn tại ở mô hình
/// lạc quan là cải tiến của giả định, không phải của chiến lược.
/// </remarks>
public class LimitFillModelTests
{
    private static readonly DateTime Start = new(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc);

    private static EngineSetting Setting(bool requiresThrough, int expiryBars = 6) => new()
    {
        BacktestTakerFeePercent = 0m,
        BacktestMakerFeePercent = 0m,
        BacktestEntrySlippageBps = 0m,
        BacktestStopSlippageBps = 0m,
        BacktestLimitFillRequiresThrough = requiresThrough,
        LimitEntryExpiryBars = expiryBars,
    };

    private static Candle Candle(decimal low, decimal high, int index = 1)
    {
        var open = Start.AddMinutes(index * 15);
        return new Candle(open, high, high, low, high, 100m, open.AddMinutes(15).AddTicks(-1));
    }

    /// <summary>Vào 100, limit chờ 95, dừng lỗ 90, chốt lời 130.</summary>
    private static TradeExecutionPlan Plan() => new(
        [
            new PlannedEntryTranche(100m, 0.5m),
            new PlannedEntryTranche(95m, 0.5m, IsLimit: true),
        ],
        StopLoss: 90m,
        FirstTakeProfit: 130m,
        RunnerTakeProfit: null,
        FirstTakeProfitFraction: 1m,
        MoveRunnerStopToBreakeven: false,
        Mode: "StrongTrendRunner");

    private static SimulatedTradePosition Open(EngineSetting setting) =>
        SimulatedTradePosition.Open(
            "BTCUSDT", TradeDirection.Long, Start, 1m, DayRegime.TrendUp, Plan(), setting);

    // ── Hai mô hình khớp ────────────────────────────────────────────────

    /// <summary>Nến chạm ĐÚNG mức: lạc quan khớp, thận trọng không.</summary>
    /// <remarks>
    /// Đây là toàn bộ sự khác nhau giữa hai mô hình, gói trong một cây nến. Đáy nến bằng đúng
    /// mức limit nghĩa là giá đã tới đó nhưng không đi tiếp — không có gì trong dữ liệu nến nói
    /// được rằng phần khối lượng xếp trước ta đã được khớp hết hay chưa.
    /// </remarks>
    [Fact]
    public void Nen_cham_dung_muc_thi_lac_quan_khop_con_than_trong_khong()
    {
        var optimistic = Open(Setting(requiresThrough: false));
        optimistic.Advance(Candle(low: 95m, high: 101m), Setting(requiresThrough: false));
        Assert.Equal(2, optimistic.Entries.Count(e => e.IsFilled));

        var conservative = Open(Setting(requiresThrough: true));
        conservative.Advance(Candle(low: 95m, high: 101m), Setting(requiresThrough: true));
        Assert.Single(conservative.Entries, e => e.IsFilled);
    }

    /// <summary>Nến đi xuyên qua mức: cả hai mô hình đều khớp.</summary>
    [Fact]
    public void Nen_xuyen_qua_muc_thi_ca_hai_mo_hinh_deu_khop()
    {
        foreach (var requiresThrough in new[] { true, false })
        {
            var setting = Setting(requiresThrough);
            var position = Open(setting);

            position.Advance(Candle(low: 94.99m, high: 101m), setting);

            Assert.Equal(2, position.Entries.Count(e => e.IsFilled));
        }
    }

    /// <summary>Quy tắc "phải xuyên qua" áp cho cả chân CHỐT LỜI, vì nó cũng là lệnh limit.</summary>
    /// <remarks>
    /// Thận trọng ở chân vào mà lạc quan ở chân ra là tự nghiêng kết quả về phía có lợi: lệnh khó
    /// vào hơn nhưng thoát dễ hơn. Cùng một loại lệnh phải chịu cùng một giả định hàng đợi.
    /// </remarks>
    [Fact]
    public void Chan_chot_loi_cung_chiu_quy_tac_hang_doi_nhu_chan_vao()
    {
        var optimistic = Setting(requiresThrough: false);
        var a = Open(optimistic);
        Assert.True(a.Advance(Candle(low: 99m, high: 130m), optimistic));
        Assert.Equal(TradeOutcome.Win, a.Outcome);

        var conservative = Setting(requiresThrough: true);
        var b = Open(conservative);
        Assert.False(b.Advance(Candle(low: 99m, high: 130m), conservative));
        Assert.Null(b.Outcome);
    }

    /// <summary>Dừng lỗ KHÔNG chịu quy tắc đó — nó là stop-market, chạm mức là kích hoạt.</summary>
    /// <remarks>
    /// Áp "phải xuyên qua" cho dừng lỗ sẽ cho lệnh sống sót những cây nến mà ngoài đời nó đã bị
    /// quét. Đó là kiểu lạc quan nguy hiểm nhất, vì nó làm đẹp đúng phần rủi ro.
    /// </remarks>
    [Fact]
    public void Dung_lo_van_kich_hoat_khi_chi_cham_muc_du_o_mo_hinh_than_trong()
    {
        var setting = Setting(requiresThrough: true);
        var position = Open(setting);

        Assert.True(position.Advance(Candle(low: 90m, high: 101m), setting));

        Assert.Equal(TradeOutcome.Loss, position.Outcome);
    }

    // ── Phí và trượt giá theo loại lệnh ─────────────────────────────────

    /// <summary>Lệnh limit khớp ĐÚNG mức đã đặt — không trượt giá, kể cả khi cấu hình có trượt.</summary>
    /// <remarks>
    /// Lệnh chờ sẵn trong sổ khớp tại mức của nó hoặc tốt hơn, không bao giờ tệ hơn. V1 áp trượt
    /// giá bất lợi cho mọi chân, tức là phạt một chuyện về nguyên tắc không xảy ra — và phạt đúng
    /// vào cái cải tiến mà V2 cần đo.
    /// </remarks>
    [Fact]
    public void Chan_limit_khop_dung_muc_khong_truot_gia()
    {
        var setting = Setting(requiresThrough: true);
        setting.BacktestEntrySlippageBps = 50m;   // rất lớn, để nếu bị áp thì lộ ra ngay

        var position = Open(setting);
        position.Advance(Candle(low: 94m, high: 101m), setting);

        var market = position.Entries[0];
        var limit = position.Entries[1];

        Assert.Equal(100m * 1.005m, market.EntryPrice);   // thị trường: có trượt giá
        Assert.Equal(95m, limit.EntryPrice);              // limit: đúng mức
    }

    // ── Hết hạn ─────────────────────────────────────────────────────────

    /// <summary>Lệnh limit chờ quá hạn thì bị huỷ, và được ghi nhận là HẾT HẠN.</summary>
    /// <remarks>
    /// Một nhịp hồi mất hơn ngần ấy nến để chạm mức thì không còn là nhịp hồi, nó là một cú
    /// khựng — khớp lúc đó là gia tăng vị thế đúng lúc động lượng đã tắt.
    /// </remarks>
    [Fact]
    public void Chan_limit_cho_qua_han_thi_bi_huy()
    {
        var setting = Setting(requiresThrough: true, expiryBars: 3);
        var position = Open(setting);

        // Ba nến trong hạn, giá không về tới 95.
        for (var i = 1; i <= 3; i++)
            Assert.False(position.Advance(Candle(low: 98m, high: 101m, index: i), setting));

        Assert.False(position.Entries[1].IsCancelled);

        // Nến thứ tư: hết hạn TRƯỚC khi xét khớp, nên dù giá xuyên qua 95 cũng không vào nữa.
        Assert.False(position.Advance(Candle(low: 94m, high: 101m, index: 4), setting));

        Assert.True(position.Entries[1].IsExpired);
        Assert.False(position.Entries[1].IsFilled);
        Assert.Equal(1, position.LimitTranchesExpired);
        Assert.Equal(0, position.LimitTranchesFilled);
    }

    /// <summary>Huỷ vì hết hạn khác huỷ vì lệnh đã đóng — hai chuyện, hai con số.</summary>
    /// <remarks>
    /// Gộp chung sẽ che mất trường hợp "mức đặt sai chỗ", trường hợp duy nhất cần sửa.
    /// </remarks>
    [Fact]
    public void Huy_vi_lenh_da_dong_khong_bi_dem_la_het_han()
    {
        var setting = Setting(requiresThrough: true, expiryBars: 6);
        var position = Open(setting);

        Assert.True(position.Advance(Candle(low: 89m, high: 101m), setting));

        Assert.True(position.Entries[1].IsCancelled || position.Entries[1].IsFilled);
        Assert.Equal(0, position.LimitTranchesExpired);
    }

    /// <summary>Hết hạn chỉ áp cho chân limit; chân thị trường đã khớp lúc mở vị thế.</summary>
    [Fact]
    public void Het_han_khong_dung_toi_chan_thi_truong()
    {
        var setting = Setting(requiresThrough: true, expiryBars: 1);
        var position = Open(setting);

        for (var i = 1; i <= 5; i++)
            position.Advance(Candle(low: 98m, high: 101m, index: i), setting);

        Assert.True(position.Entries[0].IsFilled);
        Assert.False(position.Entries[0].IsCancelled);
        Assert.Equal(1, position.LimitTranchesOffered);
    }

    // ── Bất biến ────────────────────────────────────────────────────────

    [Fact]
    public void Chan_vao_lenh_dau_limit_o_trang_thai_cho_den_khi_gia_xuyen_qua()
    {
        var plan = new TradeExecutionPlan(
            [new PlannedEntryTranche(100m, 1m, IsLimit: true)],
            90m, 130m, null, 1m, false, "RangeQuick");

        var position = SimulatedTradePosition.Open(
            "BTCUSDT", TradeDirection.Long, Start, 1m, DayRegime.Range, plan,
            Setting(requiresThrough: true));

        Assert.False(position.HasAnyFill);
        Assert.False(position.Advance(Candle(low: 100m, high: 105m), Setting(requiresThrough: true)));
        Assert.False(position.HasAnyFill);

        Assert.False(position.Advance(Candle(low: 99m, high: 105m, index: 2), Setting(requiresThrough: true)));
        Assert.True(position.HasAnyFill);
        Assert.Equal(1, position.MakerFills);
    }
}
