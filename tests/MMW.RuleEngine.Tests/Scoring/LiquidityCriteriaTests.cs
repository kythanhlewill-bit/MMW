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

    // ── liquidity.zone_position ─────────────────────────────────────────

    private static LiquidityZoneCriterion Zone() => new(ScoringFixtures.Swings);

    [Fact]
    public void Vung_thanh_khoan_LUON_danh_dau_la_xap_xi()
    {
        // R-010: cụm thanh khoản thật nằm trong sổ lệnh của sàn, thứ không có API công khai
        // nào cho xem đầy đủ. Con số ở đây suy ra từ điểm xoay, và phải nói rõ điều đó.
        var context = ScoringFixtures.Context(entry: ScoringFixtures.ZigZag(120));

        Assert.True(Zone().Evaluate(context).IsApproximation);
    }

    [Fact]
    public void Cum_nam_ngay_ngoai_dung_lo_bi_tru_ve_0()
    {
        // Đặt dừng lỗ ngay trên một đáy xoay: giá chỉ cần chạm tới đó là quét sạch lệnh
        // rồi quay đầu, và setup đúng vẫn thua.
        var candles = ScoringFixtures.ZigZag(120);
        var pivots = ScoringFixtures.Swings.Detect(candles, 2);
        var lowPivot = pivots.Where(p => !p.IsHigh).Select(p => p.Price).DefaultIfEmpty(0m).Max();

        var context = ScoringFixtures.Context(entry: candles) with
        {
            CurrentPrice = candles[^1].Close,
            PlannedStopLoss = lowPivot + (candles[^1].Close - lowPivot) * 0.1m,
            PlannedTakeProfit = candles[^1].Close * 1.05m,
        };

        var result = Zone().Evaluate(context);

        Assert.Equal(0, result.AwardedPoints);
        Assert.True(result.IsApproximation);
        Assert.Contains("quét", result.Reason);
    }

    [Fact]
    public void Duong_toi_muc_tieu_khong_vuong_cum_nao_duoc_diem_toi_da()
    {
        // Mục tiêu đặt ngay sát giá hiện tại nên không cụm nào chắn giữa, và dừng lỗ đặt xa
        // dưới mọi điểm xoay nên không có vùng bị quét.
        var candles = ScoringFixtures.ZigZag(120);
        var price = candles[^1].Close;

        var context = ScoringFixtures.Context(entry: candles) with
        {
            CurrentPrice = price,
            PlannedStopLoss = price * 0.5m,
            PlannedTakeProfit = price * 1.0001m,
        };

        Assert.Equal(5, Zone().Evaluate(context).AwardedPoints);
    }

    [Fact]
    public void Chua_co_muc_dung_lo_thi_bao_thieu_du_lieu()
    {
        var context = ScoringFixtures.Context() with { PlannedStopLoss = null };

        var result = Zone().Evaluate(context);

        Assert.False(result.DataAvailable);
        Assert.Equal(0, result.AwardedPoints);
    }

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
