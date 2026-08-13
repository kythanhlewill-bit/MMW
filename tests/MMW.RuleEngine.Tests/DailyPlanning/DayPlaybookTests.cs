using MMW.Application.MarketData.Models;
using MMW.Application.Trading.DailyPlanning;
using MMW.Application.Trading.Execution;
using MMW.Application.Trading.Scoring;
using MMW.Domain.Enums;
using MMW.RuleEngine.Tests.Scoring;
using Xunit;

namespace MMW.RuleEngine.Tests.DailyPlanning;

/// <summary>
/// Nhãn ngày và cấu trúc ngày là hai thứ khác nhau, và bộ kích hoạt phải rẽ theo cấu trúc.
/// </summary>
/// <remarks>
/// Bộ kiểm thử này khoá lại một lỗ hổng đã trả giá bằng dữ liệu thật: ngày 12 và 13/08/2026 có tin
/// CPI/PPI nên bị dán nhãn <c>EventDay</c>, và vì <see cref="SetupTriggerPolicy"/> so thẳng nhãn
/// với <c>DayRegime.Range</c>, cả hai ngày rơi vào nhánh xu hướng rồi bị bác ngay dòng đầu.
/// 302 phiếu không phiếu nào đi quá bậc 1 của phễu — máy chưa từng đi tìm setup.
/// </remarks>
public class DayPlaybookTests
{
    private readonly SetupTriggerPolicy _triggers = new(
        ScoringFixtures.Structure,
        new MMW.Application.Trading.Structure.SidewaysPatternAnalyzer(ScoringFixtures.Swings));

    // ── Lấy lại cấu trúc từ dưới lớp nhãn ───────────────────────────────

    [Theory]
    [InlineData(DayRegime.TrendUp, DayStructure.TrendUp)]
    [InlineData(DayRegime.TrendDown, DayStructure.TrendDown)]
    [InlineData(DayRegime.Range, DayStructure.Range)]
    public void Ba_nhan_cau_truc_thi_chinh_nhan_la_cau_truc(DayRegime regime, DayStructure expected)
    {
        // Quan trọng: đọc nhãn TRƯỚC BtcStructure, vì override trong phiên lật Range→Trend bằng
        // cách đổi nhãn trên bản sao trong khi BtcStructure vẫn giữ chữ cũ của kế hoạch gốc.
        var plan = ScoringFixtures.Plan(regime: regime, btcStructure: "Range");

        Assert.Equal(expected, DayPlaybook.StructureOf(plan));
    }

    [Theory]
    [InlineData(DayRegime.EventDay, "TrendUp", DayStructure.TrendUp)]
    [InlineData(DayRegime.EventDay, "TrendDown", DayStructure.TrendDown)]
    [InlineData(DayRegime.EventDay, "Range", DayStructure.Range)]
    [InlineData(DayRegime.HighVolatility, "TrendUp", DayStructure.TrendUp)]
    [InlineData(DayRegime.HighVolatility, "TrendDown", DayStructure.TrendDown)]
    public void Nhan_nguy_hiem_thi_lui_ve_BtcStructure(
        DayRegime regime, string structure, DayStructure expected)
    {
        var plan = ScoringFixtures.Plan(regime: regime, btcStructure: structure);

        Assert.Equal(expected, DayPlaybook.StructureOf(plan));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("KhongPhaiCauTruc")]
    [InlineData("99")]
    public void Khong_doc_duoc_cau_truc_thi_lui_ve_Range(string? garbage)
    {
        // Trùng với cách DayRegimeClassifier.ReadStructure tự xử lý khi thiếu nến. An toàn vì
        // playbook đi ngang không tự mở lệnh — bộ dò phải tìm thấy hình học thật mới cho qua.
        var plan = ScoringFixtures.Plan(regime: DayRegime.EventDay, btcStructure: garbage);

        Assert.Equal(DayStructure.Range, DayPlaybook.StructureOf(plan));
    }

    // ── Lỗ hổng cũ: ngày có tin không có playbook nào ────────────────────

    [Theory]
    [InlineData(DayRegime.EventDay)]
    [InlineData(DayRegime.HighVolatility)]
    public void Ngay_nhan_nguy_hiem_nhung_cau_truc_Range_van_di_vao_playbook_di_ngang(DayRegime regime)
    {
        var context = ScoringFixtures.Context(
            entry: ScoringFixtures.Flat(40, price: 100m, range: 2m, volume: 100m),
            direction: TradeDirection.Long,
            plan: ScoringFixtures.Plan(regime: regime, btcStructure: "Range"),
            settings: ScoringFixtures.Settings(s => s.StrategyVersion = TradingStrategyVersion.AdaptiveSidewaysV6));

        var result = _triggers.Evaluate(context, range: null);

        // Không đòi phải vào lệnh — nến phẳng thì đúng là không có setup. Đòi rằng lý do từ chối
        // phải đến TỪ BỘ DÒ đi ngang, chứ không phải "regime này không có playbook".
        Assert.NotEqual(SetupTriggerState.NoBreakOfStructure, result.State);
        Assert.Contains(result.State, new[]
        {
            SetupTriggerState.RangeGeometryWeak,
            SetupTriggerState.CompressionMissing,
            SetupTriggerState.RangeNotSwept,
            SetupTriggerState.BreakoutMissing,
        });
    }

    [Theory]
    [InlineData(DayRegime.EventDay, "TrendDown", TradeDirection.Short)]
    [InlineData(DayRegime.HighVolatility, "TrendUp", TradeDirection.Long)]
    public void Ngay_nhan_nguy_hiem_nhung_cau_truc_trend_van_duoc_di_tim_BOS(
        DayRegime regime, string structure, TradeDirection direction)
    {
        var context = ScoringFixtures.Context(
            entry: ScoringFixtures.Flat(40, price: 100m, range: 2m, volume: 100m),
            direction: direction,
            plan: ScoringFixtures.Plan(regime: regime, btcStructure: structure));

        var result = _triggers.Evaluate(context, range: null);

        // Nến phẳng nên vẫn không có BOS — nhưng lý do phải là "chưa có BOS thuận chiều", tức là
        // máy ĐÃ đi tìm. Trước bản vá nó thậm chí không chạy tới bước phân tích cấu trúc.
        Assert.False(result.Passed);
        Assert.Equal(SetupTriggerState.NoBreakOfStructure, result.State);
        Assert.DoesNotContain("không có playbook", result.DetailVi);
    }

    [Fact]
    public void Chieu_nguoc_cau_truc_ngay_van_bi_tu_choi()
    {
        // Bản vá mở lại đường đi, không nới ràng buộc chiều.
        var context = ScoringFixtures.Context(
            entry: ScoringFixtures.Flat(40, price: 100m, range: 2m, volume: 100m),
            direction: TradeDirection.Long,
            plan: ScoringFixtures.Plan(regime: DayRegime.EventDay, btcStructure: "TrendDown"));

        var result = _triggers.Evaluate(context, range: null);

        Assert.False(result.Passed);
        Assert.Equal(SetupTriggerState.NoBreakOfStructure, result.State);
        Assert.Contains("TrendDown", result.DetailVi);
    }

    [Theory]
    [InlineData(DayStructure.TrendUp, TradeDirection.Long, true)]
    [InlineData(DayStructure.TrendUp, TradeDirection.Short, false)]
    [InlineData(DayStructure.TrendDown, TradeDirection.Short, true)]
    [InlineData(DayStructure.TrendDown, TradeDirection.Long, false)]
    [InlineData(DayStructure.Range, TradeDirection.Long, false)]
    [InlineData(DayStructure.Range, TradeDirection.Short, false)]
    public void Thuan_chieu_chi_dung_khi_cau_truc_va_chieu_khop(
        DayStructure structure, TradeDirection direction, bool expected)
    {
        Assert.Equal(expected, DayPlaybook.IsTrendAligned(structure, direction));
    }

    // ── Bảng rủi ro vẫn giữ nguyên mọi tầng bảo vệ ──────────────────────

    [Fact]
    public void Ngay_co_tin_van_bi_bang_rui_ro_siet_lai()
    {
        // Đây là bằng chứng bản vá đúng ý định thiết kế chứ không phải nới lỏng: bảng FR-019 vốn
        // đã cấp cho ngày có tin 2 lệnh ở hệ số 0,4 — tức là "vào nhỏ và ít", không phải "cấm".
        var withEvent = RegimeTable.Resolve(DayStructure.TrendDown, VolatilityRegime.Normal, hasHighImpactEvent: true);
        var without = RegimeTable.Resolve(DayStructure.TrendDown, VolatilityRegime.Normal, hasHighImpactEvent: false);

        Assert.Equal(0.4m, withEvent.RiskMultiplier);
        Assert.Equal(2, withEvent.MaxTradesToday);
        Assert.True(withEvent.RiskMultiplier < without.RiskMultiplier);
        Assert.True(withEvent.MaxTradesToday < without.MaxTradesToday);
        Assert.NotEqual(AllowedDirections.None, withEvent.AllowedDirections);
    }
}
