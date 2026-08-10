using MMW.Application.Trading.Scoring;
using MMW.Application.Trading.Sizing;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.Scoring;

/// <summary>
/// T083 / FR-034 — bảng ngưỡng kích thước và bất biến <c>finalSizeR ≤ baseSizeR</c>.
/// </summary>
public class PositionSizerTests
{
    private static readonly IPositionSizer Sizer = new ScoreBasedPositionSizer();

    private static ScoringOutcome Score(int total, bool vetoed = false) => new(
        total, total, 0, 0, 0, vetoed,
        vetoed ? VetoReason.DirectionNotAllowed : null,
        vetoed ? "veto" : null,
        Array.Empty<ScoredLine>());

    private static SizingResult Calculate(
        int score, decimal dayMultiplier = 1.0m, decimal gateMultiplier = 1.0m, decimal ai = 1.0m,
        bool vetoed = false, bool blocked = false)
    {
        var gates = blocked
            ? new GateAggregate(gateMultiplier, 0, true, VetoReason.MaxTradesReached, "chặn")
            : new GateAggregate(gateMultiplier, 0, false, null, null);

        return Sizer.Calculate(
            Score(score, vetoed),
            ScoringFixtures.Plan(riskMultiplier: dayMultiplier),
            gates, ai, ScoringFixtures.Settings());
    }

    // ── Bảng ngưỡng 55 / 70 / 85 ────────────────────────────────────────

    [Theory]
    [InlineData(0, 0)]
    [InlineData(54, 0)]        // dưới ngưỡng vào lệnh
    [InlineData(55, 0.5)]      // đúng ngưỡng tối thiểu
    [InlineData(69, 0.5)]
    [InlineData(70, 1.0)]      // đúng ngưỡng đầy đủ
    [InlineData(84, 1.0)]
    [InlineData(85, 1.5)]      // đúng ngưỡng tối đa
    [InlineData(100, 1.5)]
    public void Bang_nguong_cho_dung_kich_thuoc_goc(int score, double expected)
    {
        Assert.Equal((decimal)expected, Calculate(score).BaseSizeR);
    }

    [Fact]
    public void Diem_duoi_nguong_cho_kich_thuoc_cuoi_bang_0()
    {
        var result = Calculate(54);

        Assert.Equal(0m, result.FinalSizeR);
        Assert.Contains("Zero lệnh là kết quả đúng", result.ReasonVi);
    }

    [Fact]
    public void Bi_veto_thi_kich_thuoc_cuoi_bang_0_bat_ke_diem_cao()
    {
        Assert.Equal(0m, Calculate(100, vetoed: true).FinalSizeR);
    }

    [Fact]
    public void Bi_gate_ky_luat_chan_thi_kich_thuoc_cuoi_bang_0()
    {
        Assert.Equal(0m, Calculate(100, blocked: true).FinalSizeR);
    }

    // ── Ca kiểm chứng của đặc tả ────────────────────────────────────────

    [Fact]
    public void Tam_muoi_tam_diem_nhan_he_so_ngay_0_3_cho_0_45R()
    {
        var result = Calculate(88, dayMultiplier: 0.3m);

        Assert.Equal(1.5m, result.BaseSizeR);
        Assert.Equal(0.45m, result.FinalSizeR);
    }

    // ── Bất biến số học ─────────────────────────────────────────────────

    [Theory]
    [InlineData(55)]
    [InlineData(70)]
    [InlineData(85)]
    [InlineData(100)]
    public void Kich_thuoc_cuoi_khong_bao_gio_vuot_kich_thuoc_goc(int score)
    {
        foreach (var day in new[] { 0m, 0.3m, 0.5m, 1.0m })
        foreach (var gate in new[] { 0m, 0.5m, 1.0m })
        foreach (var ai in new[] { 0m, 0.5m, 1.0m })
        {
            var r = Calculate(score, day, gate, ai);

            Assert.True(r.FinalSizeR <= r.BaseSizeR,
                $"điểm {score}, ngày {day}, kỷ luật {gate}, AI {ai}: {r.FinalSizeR} > {r.BaseSizeR}");
        }
    }

    [Fact]
    public void He_so_AI_vuot_1_bi_kep_ve_1_chu_khong_phong_to_lenh()
    {
        // FR-042 cưỡng chế ở phía NHẬN. Nếu lớp AI trả 1.5 vì lỗi bóc tách hay vì mô hình
        // "tự tin", phép kẹp biến nó thành 1.0 và không có gì xảy ra.
        var result = Calculate(70, ai: 1.5m);

        Assert.Equal(1.0m, result.AiMultiplier);
        Assert.Equal(result.BaseSizeR, result.FinalSizeR);
    }

    [Fact]
    public void He_so_am_bi_kep_ve_0_chu_khong_dao_dau_lenh()
    {
        var result = Calculate(70, gateMultiplier: -2m);

        Assert.Equal(0m, result.DisciplineMultiplier);
        Assert.Equal(0m, result.FinalSizeR);
    }

    [Fact]
    public void He_so_ngay_vuot_1_cung_bi_kep()
    {
        var result = Calculate(70, dayMultiplier: 3m);

        Assert.Equal(1.0m, result.DayRiskMultiplier);
        Assert.Equal(result.BaseSizeR, result.FinalSizeR);
    }

    [Fact]
    public void Nguong_diem_doc_tu_cau_hinh_chu_khong_viet_cung()
    {
        var settings = ScoringFixtures.Settings(s =>
        {
            s.MinScoreToEnter = 20;
            s.ScoreThresholdFull = 30;
            s.ScoreThresholdMax = 40;
        });

        var result = Sizer.Calculate(
            Score(45), ScoringFixtures.Plan(), GateAggregate.Neutral, 1.0m, settings);

        Assert.Equal(settings.SizeMultiplierMax, result.BaseSizeR);
    }

    [Fact]
    public void Ly_do_neu_du_ca_bon_thanh_phan_de_doi_chieu_duoc()
    {
        var result = Calculate(88, dayMultiplier: 0.3m, gateMultiplier: 0.5m, ai: 0.8m);

        Assert.Contains("88", result.ReasonVi);
        Assert.Contains("0.30", result.ReasonVi);
        Assert.Contains("0.50", result.ReasonVi);
        Assert.Contains("0.80", result.ReasonVi);
    }
}
