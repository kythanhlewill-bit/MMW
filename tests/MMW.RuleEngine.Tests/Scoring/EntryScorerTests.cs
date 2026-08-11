using MMW.Application.Trading.Scoring;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.Scoring;

/// <summary>Tiêu chí giả cho các test về vòng tổng hợp.</summary>
internal sealed class StubCriterion : IScoreCriterion
{
    private readonly CriterionResult _result;

    public StubCriterion(
        string key, ScoreGroup group, int maxPoints, CriterionResult result, bool isDirectional = false)
    {
        Key = key;
        Group = group;
        MaxPoints = maxPoints;
        IsDirectional = isDirectional;
        _result = result;
    }

    public string Key { get; }
    public ScoreGroup Group { get; }
    public int MaxPoints { get; }
    public bool IsDirectional { get; }

    /// <summary>Số lần bị gọi — dùng để chứng minh vòng tổng hợp hỏi HẾT tiêu chí kể cả sau veto.</summary>
    public int CallCount { get; private set; }

    public CriterionResult Evaluate(ScoringContext context)
    {
        CallCount++;
        return _result;
    }
}

/// <summary>
/// T081 / T080 — vòng tổng hợp: chạy hết tiêu chí rồi mới áp veto đầu tiên, thứ tự duyệt tất
/// định, và thiếu dữ liệu ⟹ 0 điểm.
/// </summary>
public class EntryScorerTests
{
    private static StubCriterion Points(string key, ScoreGroup group, int max, int awarded) =>
        new(key, group, max, new CriterionResult(awarded, $"{key} = {awarded}"));

    private static StubCriterion Veto(string key, ScoreGroup group, VetoReason reason) =>
        new(key, group, 10, CriterionResult.Veto(reason, $"{key} veto"));

    // ── Veto áp sau khi đã hỏi hết ──────────────────────────────────────

    [Fact]
    public void Gap_veto_cung_thi_cac_tieu_chi_sau_VAN_chay()
    {
        var first = Veto("market.aaa", ScoreGroup.Market, VetoReason.DirectionNotAllowed);
        var later = Points("market.zzz", ScoreGroup.Market, 10, 10);

        var outcome = new EntryScorer(new IScoreCriterion[] { first, later }).Score(ScoringFixtures.Context());

        Assert.True(outcome.IsVetoed);
        Assert.Equal(1, first.CallCount);
        Assert.Equal(1, later.CallCount);
    }

    [Fact]
    public void Veto_KHONG_lam_cut_do_phu_du_lieu()
    {
        // Đây là lý do bỏ dừng sớm. Trước đây `availableMax` cụt tại chỗ veto, nên phiếu ghi
        // độ phủ 10/20 và DataCoverage đọc ra như mất nguồn dữ liệu — trong khi cả hai tiêu chí
        // đều có dữ liệu. Con số đó chảy thẳng vào hệ số kích thước.
        var criteria = new IScoreCriterion[]
        {
            Veto("market.aaa", ScoreGroup.Market, VetoReason.DirectionNotAllowed),
            Points("market.zzz", ScoreGroup.Market, 10, 10),
        };

        var outcome = new EntryScorer(criteria).Score(ScoringFixtures.Context());

        Assert.Equal(20, outcome.AvailableMaxPoints);
        Assert.Equal(20, outcome.TotalMaxPoints);
        Assert.Equal(1m, outcome.DataCoverage);
    }

    [Fact]
    public void Phieu_ghi_dung_MOT_ly_do_tu_choi()
    {
        // Nhiều veto cùng lúc vẫn chỉ chốt MỘT lý do — veto đầu tiên theo thứ tự (Group, Key).
        // Gom cả bốn lý do sẽ khiến câu hỏi "vì sao lệnh này bị loại" không còn câu quyết định.
        var criteria = new IScoreCriterion[]
        {
            Veto("market.aaa", ScoreGroup.Market, VetoReason.DirectionNotAllowed),
            Veto("market.bbb", ScoreGroup.Market, VetoReason.HtfMisaligned),
        };

        var outcome = new EntryScorer(criteria).Score(ScoringFixtures.Context());

        Assert.Equal(VetoReason.DirectionNotAllowed, outcome.VetoReason);

        // Các veto sau vẫn nằm lại trong Lines để truy vết — chỉ lý do CHỐT là duy nhất.
        Assert.Equal(2, outcome.Lines.Count(l => l.Result.IsHardVeto));
    }

    [Fact]
    public void Veto_cung_van_thang_thieu_du_lieu()
    {
        // Thứ tự ưu tiên phải giữ nguyên như hồi còn thoát sớm: gặp cả hai thì ghi veto cứng,
        // không ghi InsufficientData.
        var criteria = new IScoreCriterion[]
        {
            Veto("market.aaa", ScoreGroup.Market, VetoReason.DirectionNotAllowed),
            new StubCriterion("market.zzz", ScoreGroup.Market, 90, CriterionResult.Missing("chết")),
        };

        var outcome = new EntryScorer(criteria).Score(ScoringFixtures.Context());

        Assert.Equal(VetoReason.DirectionNotAllowed, outcome.VetoReason);
    }

    [Fact]
    public void Veto_lam_tong_diem_ve_0_du_da_cong_duoc_diem_truoc_do()
    {
        var criteria = new IScoreCriterion[]
        {
            Points("technical.aaa", ScoreGroup.Technical, 10, 10),
            Veto("market.aaa", ScoreGroup.Market, VetoReason.InBlackoutWindow),
        };

        var outcome = new EntryScorer(criteria).Score(ScoringFixtures.Context());

        Assert.Equal(0, outcome.TotalScore);
        Assert.Equal(10, outcome.TechnicalScore);   // vẫn ghi lại để truy vết
    }

    // ── Thứ tự duyệt ────────────────────────────────────────────────────

    [Fact]
    public void Thu_tu_duyet_theo_nhom_roi_theo_khoa_bat_ke_thu_tu_DI()
    {
        var criteria = new IScoreCriterion[]
        {
            Points("liquidity.b", ScoreGroup.Liquidity, 5, 1),
            Points("technical.b", ScoreGroup.Technical, 5, 1),
            Points("market.a", ScoreGroup.Market, 5, 1),
            Points("technical.a", ScoreGroup.Technical, 5, 1),
        };

        var outcome = new EntryScorer(criteria).Score(ScoringFixtures.Context());

        Assert.Equal(
            new[] { "technical.a", "technical.b", "market.a", "liquidity.b" },
            outcome.Lines.Select(l => l.Key));
    }

    [Fact]
    public void Dao_thu_tu_dang_ky_khong_doi_ket_qua()
    {
        var forward = new IScoreCriterion[]
        {
            Points("technical.a", ScoreGroup.Technical, 5, 3),
            Points("market.a", ScoreGroup.Market, 5, 4),
        };
        var reversed = forward.Reverse().ToArray();

        var a = new EntryScorer(forward).Score(ScoringFixtures.Context());
        var b = new EntryScorer(reversed).Score(ScoringFixtures.Context());

        Assert.Equal(a.TotalScore, b.TotalScore);
        Assert.Equal(a.Lines.Select(l => l.Key), b.Lines.Select(l => l.Key));
    }

    // ── Cộng điểm theo nhóm ─────────────────────────────────────────────

    [Fact]
    public void Cong_diem_dung_nhom_va_kep_tong_ve_0_100()
    {
        var criteria = new IScoreCriterion[]
        {
            Points("technical.a", ScoreGroup.Technical, 40, 40),
            Points("market.a", ScoreGroup.Market, 30, 30),
            Points("liquidity.a", ScoreGroup.Liquidity, 15, 15),
        };

        var outcome = new EntryScorer(criteria).Score(ScoringFixtures.Context());

        Assert.Equal(40, outcome.TechnicalScore);
        Assert.Equal(30, outcome.MarketScore);
        Assert.Equal(15, outcome.LiquidityScore);
        Assert.Equal(85, outcome.TotalScore);
    }

    [Fact]
    public void Nhom_ky_luat_chi_TRU_diem_khong_bao_gio_cong()
    {
        var criteria = new IScoreCriterion[]
        {
            Points("technical.a", ScoreGroup.Technical, 40, 40),
            Points("discipline.bonus", ScoreGroup.Discipline, 0, +20),   // cố tình trả điểm dương
        };

        var outcome = new EntryScorer(criteria).Score(ScoringFixtures.Context());

        Assert.Equal(0, outcome.DisciplinePenalty);
        Assert.Equal(40, outcome.TotalScore);
    }

    [Fact]
    public void Tieu_chi_tra_vuot_tran_bi_kep_ve_tran()
    {
        var criteria = new IScoreCriterion[] { Points("technical.a", ScoreGroup.Technical, 10, 999) };

        Assert.Equal(10, new EntryScorer(criteria).Score(ScoringFixtures.Context()).TotalScore);
    }

    [Fact]
    public void Tieu_chi_tra_am_o_nhom_cong_diem_bi_kep_ve_0()
    {
        var criteria = new IScoreCriterion[] { Points("technical.a", ScoreGroup.Technical, 10, -50) };

        Assert.Equal(0, new EntryScorer(criteria).Score(ScoringFixtures.Context()).TotalScore);
    }

    // ── FR-006 ở tầng tổng hợp ──────────────────────────────────────────

    [Fact]
    public void Thieu_du_lieu_khong_duoc_cong_diem_du_tieu_chi_tra_nham()
    {
        // Chốt chặn thứ hai cho FR-006: kể cả khi một tiêu chí trả DataAvailable = false
        // kèm điểm dương, vòng tổng hợp vẫn tính 0.
        var criteria = new IScoreCriterion[]
        {
            new StubCriterion("technical.a", ScoreGroup.Technical, 10,
                new CriterionResult(10, "lỗi lập trình", DataAvailable: false)),
        };

        Assert.Equal(0, new EntryScorer(criteria).Score(ScoringFixtures.Context()).TotalScore);
    }

    [Fact]
    public void Khong_co_tieu_chi_nao_thi_diem_bang_0_chu_khong_no()
    {
        var outcome = new EntryScorer(Array.Empty<IScoreCriterion>()).Score(ScoringFixtures.Context());

        Assert.Equal(0, outcome.TotalScore);
        Assert.False(outcome.IsVetoed);
        Assert.Empty(outcome.Lines);
    }

    // ── Bộ 13 tiêu chí thật ─────────────────────────────────────────────

    [Fact]
    public void Bo_13_tieu_chi_that_cong_dung_85_diem_toi_da()
    {
        var criteria = ScoringFixtures.AllCriteria();

        // 14 tiêu chí, nhưng vẫn đúng 85 điểm: `technical.structural_room` là một CÁNH CỔNG
        // 0 điểm, không phải một thang đo. Nhờ vậy ba ngưỡng 55/70/85 không phải tính lại.
        Assert.Equal(14, criteria.Count);
        Assert.Equal(0, criteria.Single(c => c.Key == "technical.structural_room").MaxPoints);
        Assert.Equal(40, criteria.Where(c => c.Group == ScoreGroup.Technical).Sum(c => c.MaxPoints));
        Assert.Equal(30, criteria.Where(c => c.Group == ScoreGroup.Market).Sum(c => c.MaxPoints));
        Assert.Equal(15, criteria.Where(c => c.Group == ScoreGroup.Liquidity).Sum(c => c.MaxPoints));
    }

    [Fact]
    public void Khoa_cua_13_tieu_chi_deu_duy_nhat()
    {
        // Khoá trùng nghĩa là hai tiêu chí ghi đè nhau trong bảng dòng phiếu, và mọi thống kê
        // "tiêu chí nào hay về 0 điểm nhất" đều sai từ đó trở đi.
        var keys = ScoringFixtures.AllCriteria().Select(c => c.Key).ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Bo_14_tieu_chi_that_chay_tron_ven_tren_boi_canh_day_du()
    {
        var outcome = new EntryScorer(ScoringFixtures.AllCriteria()).Score(ScoringFixtures.Context());

        Assert.Equal(14, outcome.Lines.Count);
        Assert.InRange(outcome.TotalScore, 0, 85);
        Assert.All(outcome.Lines, l => Assert.False(string.IsNullOrWhiteSpace(l.Result.Reason)));
    }

    [Fact]
    public void Tong_diem_khong_bao_gio_dat_100_vi_nhom_ky_luat_chi_tru()
    {
        // Thiết kế có chủ ý: không có setup nào hoàn hảo, và thang điểm không nên gợi ý
        // điều ngược lại.
        Assert.Equal(85, ScoringFixtures.AllCriteria().Sum(c => c.MaxPoints));
    }
}
