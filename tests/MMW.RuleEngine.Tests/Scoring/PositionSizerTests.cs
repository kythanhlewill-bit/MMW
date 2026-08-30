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

    /// <summary>
    /// Bốn hệ số vẫn nhân bình thường khi tích của chúng còn trên sàn.
    /// </summary>
    /// <remarks>
    /// Ca kiểm chứng gốc của đặc tả là 88 điểm × hệ số ngày 0,3 = 0,45R. Con số ấy không còn
    /// đúng vì phép kẹp <c>MinSizeMultiplierProduct</c> nâng tích 0,30 lên 0,50 — xem
    /// <c>EngineSetting.MinSizeMultiplierProduct</c>. Nên ca kiểm chứng được dựng lại ở một hệ
    /// số nằm TRÊN sàn, để nó vẫn khoá đúng thứ nó sinh ra để khoá: phép nhân, chứ không phải
    /// phép kẹp.
    /// </remarks>
    [Fact]
    public void Tam_muoi_tam_diem_nhan_he_so_ngay_0_8_cho_1_2R()
    {
        var result = Calculate(88, dayMultiplier: 0.8m);

        Assert.Equal(1.5m, result.BaseSizeR);
        Assert.Equal(1.2m, result.FinalSizeR);
    }

    /// <summary>
    /// Tích bốn hệ số rơi dưới sàn thì bị kẹp lên, không được teo tự do.
    /// </summary>
    /// <remarks>
    /// Bốn hệ số đều trong [0, 1] và NHÂN nhau nên chúng giảm theo cấp số nhân. Đợt chạy thử
    /// 18–28/08 cho ra <c>RiskAmount</c> trải 2,38 → 24,99 USDT (gấp 10,5 lần) vì đúng cơ chế
    /// này, và hệ quả là tổng +2,59R nhưng −51,05 USDT: cược to vào lệnh thua, cược bé vào lệnh
    /// thắng. Kẹp tích lại thì thắng-thua quy ra tiền mới so sánh được với nhau.
    /// </remarks>
    [Fact]
    public void Tich_he_so_duoi_san_thi_bi_kep_len()
    {
        var result = Calculate(88, dayMultiplier: 0.3m);

        // 0,30 < sàn 0,50 ⟹ dùng 0,50 ⟹ 1,5 × 0,50 = 0,75R (không phải 0,45R).
        Assert.Equal(0.75m, result.FinalSizeR);
        Assert.Contains("kẹp", result.ReasonVi);
    }

    /// <summary>
    /// Hệ số bằng 0 là một câu trả lời "không" — phép kẹp không được biến nó thành nửa cỡ lệnh.
    /// </summary>
    /// <remarks>
    /// Đây là ranh giới quan trọng nhất của phép kẹp. Kế hoạch ngày cấm rủi ro, gate kỷ luật
    /// chặn, AI veto, hay không đo được dữ liệu nào — cả bốn đều cho hệ số 0, và cả bốn đều phải
    /// ra 0 lệnh. Kẹp lên sàn ở đây sẽ biến bốn cái veto thành bốn lệnh.
    /// </remarks>
    [Fact]
    public void He_so_bang_khong_van_ra_khong_chu_khong_bi_kep_len_san()
    {
        Assert.Equal(0m, Calculate(88, dayMultiplier: 0m).FinalSizeR);
        Assert.Equal(0m, Calculate(88, gateMultiplier: 0m).FinalSizeR);
        Assert.Equal(0m, Calculate(88, ai: 0m).FinalSizeR);
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
