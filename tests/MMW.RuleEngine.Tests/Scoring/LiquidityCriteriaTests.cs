using MMW.Application.Trading.Scoring.Criteria;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.Scoring;

/// <summary>T079 — ba tiêu chí nhóm thanh khoản.</summary>
public class LiquidityCriteriaTests
{
    // ── liquidity.open_interest ─────────────────────────────────────────

    [Fact]
    public void OI_tang_manh_duoc_diem_toi_da()
    {
        var context = ScoringFixtures.Context() with { OpenInterest = ScoringFixtures.OpenInterest(6m) };

        Assert.Equal(5, new OpenInterestCriterion().Evaluate(context).AwardedPoints);
    }

    [Fact]
    public void OI_giam_manh_duoc_0_diem()
    {
        var context = ScoringFixtures.Context() with { OpenInterest = ScoringFixtures.OpenInterest(-10m) };

        var result = new OpenInterestCriterion().Evaluate(context);

        Assert.Equal(0, result.AwardedPoints);
        Assert.Contains("đóng vị thế cũ", result.Reason);
    }

    [Fact]
    public void OI_nguong_manh_doc_tu_cau_hinh()
    {
        var context = ScoringFixtures.Context(
            settings: ScoringFixtures.Settings(s => s.OpenInterestStrongChangePercent = 10m))
            with { OpenInterest = ScoringFixtures.OpenInterest(6m) };

        Assert.Equal(3, new OpenInterestCriterion().Evaluate(context).AwardedPoints);
    }

    [Fact]
    public void OI_khong_co_du_lieu_thi_bao_thieu()
    {
        var context = ScoringFixtures.Context() with { OpenInterest = null };

        var result = new OpenInterestCriterion().Evaluate(context);

        Assert.False(result.DataAvailable);
        Assert.Equal(0, result.AwardedPoints);
    }

    // liquidity.zone_position đã được gỡ khỏi thang điểm ngày 2026-08-12 cùng bộ kiểm thử của nó.
    // Lý do đo đạc nằm ở đầu tệp Criteria/LiquidityCriteria.cs. Bất biến thay thế nằm ở
    // EngineSettingTests.Thang_diem_khong_con_tieu_chi_zone_position.

    // ── liquidity.spread_depth ──────────────────────────────────────────

    [Fact]
    public void Chenh_lech_hep_duoc_diem_toi_da()
    {
        var context = ScoringFixtures.Context() with { Depth = ScoringFixtures.Depth(1m) };

        Assert.Equal(5, new SpreadDepthCriterion().Evaluate(context).AwardedPoints);
    }

    [Fact]
    public void Chenh_lech_qua_rong_duoc_0_diem()
    {
        var context = ScoringFixtures.Context() with { Depth = ScoringFixtures.Depth(20m) };

        Assert.Equal(0, new SpreadDepthCriterion().Evaluate(context).AwardedPoints);
    }

    [Fact]
    public void Tran_chenh_lech_doc_tu_cau_hinh()
    {
        var context = ScoringFixtures.Context(
            settings: ScoringFixtures.Settings(s => s.MaxSpreadBps = 50m))
            with { Depth = ScoringFixtures.Depth(20m) };

        Assert.Equal(5, new SpreadDepthCriterion().Evaluate(context).AwardedPoints);
    }

    [Fact]
    public void So_lenh_rong_mot_ben_thi_bao_thieu_du_lieu_chu_khong_cho_diem_tuyet_doi()
    {
        // Sổ lệnh rỗng một bên nghĩa là thanh khoản đã cạn. Coi chênh lệch bằng 0 ở đó sẽ
        // chấm điểm CAO NHẤT đúng vào lúc thị trường tệ nhất.
        var context = ScoringFixtures.Context() with
        {
            Depth = new Application.MarketData.Models.DepthSnapshot(
                Array.Empty<Application.MarketData.Models.DepthLevel>(),
                new[] { new Application.MarketData.Models.DepthLevel(1000m, 1m) },
                ScoringFixtures.Now),
        };

        var result = new SpreadDepthCriterion().Evaluate(context);

        Assert.False(result.DataAvailable);
        Assert.Equal(0, result.AwardedPoints);
    }

    [Fact]
    public void Khong_lay_duoc_so_lenh_thi_bao_thieu_du_lieu()
    {
        var context = ScoringFixtures.Context() with { Depth = null };

        Assert.False(new SpreadDepthCriterion().Evaluate(context).DataAvailable);
    }
}
