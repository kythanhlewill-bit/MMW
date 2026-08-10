using MMW.Application.Trading.Scoring.Criteria;
using MMW.Application.Trading.TimeGuard;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.Scoring;

/// <summary>T078 — năm tiêu chí nhóm bối cảnh thị trường.</summary>
public class MarketCriteriaTests
{
    // ── market.day_regime_match ─────────────────────────────────────────

    [Fact]
    public void Khop_trang_thai_ngay_thuan_xu_huong_duoc_diem_toi_da()
    {
        var context = ScoringFixtures.Context(
            plan: ScoringFixtures.Plan(AllowedDirections.LongOnly, DayRegime.TrendUp));

        Assert.Equal(10, new DayRegimeMatchCriterion().Evaluate(context).AwardedPoints);
    }

    [Fact]
    public void Chieu_khong_duoc_ke_hoach_cho_phep_la_VETO_CUNG()
    {
        var context = ScoringFixtures.Context(
            direction: TradeDirection.Short,
            plan: ScoringFixtures.Plan(AllowedDirections.LongOnly, DayRegime.TrendUp));

        var result = new DayRegimeMatchCriterion().Evaluate(context);

        Assert.True(result.IsHardVeto);
        Assert.Equal(VetoReason.DirectionNotAllowed, result.VetoReason);
    }

    [Fact]
    public void Het_han_muc_lenh_trong_ngay_cung_la_VETO_CUNG()
    {
        var context = ScoringFixtures.Context(plan: ScoringFixtures.Plan(maxTrades: 0));

        var result = new DayRegimeMatchCriterion().Evaluate(context);

        Assert.True(result.IsHardVeto);
        Assert.Equal(VetoReason.MaxTradesReached, result.VetoReason);
    }

    [Theory]
    [InlineData(DayRegime.Range, 6)]
    [InlineData(DayRegime.HighVolatility, 4)]
    [InlineData(DayRegime.EventDay, 4)]
    public void Ngay_khong_thuan_xu_huong_duoc_diem_thap_hon(DayRegime regime, int expected)
    {
        var context = ScoringFixtures.Context(plan: ScoringFixtures.Plan(AllowedDirections.Both, regime));

        Assert.Equal(expected, new DayRegimeMatchCriterion().Evaluate(context).AwardedPoints);
    }

    // ── market.volatility_regime ────────────────────────────────────────

    [Fact]
    public void Phan_vi_trong_dai_ly_tuong_duoc_diem_toi_da()
    {
        var context = ScoringFixtures.Context(plan: ScoringFixtures.Plan(atrPercentile: 50m));

        Assert.Equal(6, new VolatilityRegimeCriterion().Evaluate(context).AwardedPoints);
    }

    [Fact]
    public void Phan_vi_cuc_doan_duoc_0_diem()
    {
        var context = ScoringFixtures.Context(
            plan: ScoringFixtures.Plan(volatility: VolatilityRegime.Extreme, atrPercentile: 96m));

        Assert.Equal(0, new VolatilityRegimeCriterion().Evaluate(context).AwardedPoints);
    }

    [Fact]
    public void Dai_ly_tuong_doc_tu_cau_hinh()
    {
        var context = ScoringFixtures.Context(
            plan: ScoringFixtures.Plan(atrPercentile: 20m),
            settings: ScoringFixtures.Settings(s => { s.VolatilitySweetSpotLow = 10m; s.VolatilitySweetSpotHigh = 25m; }));

        Assert.Equal(6, new VolatilityRegimeCriterion().Evaluate(context).AwardedPoints);
    }

    [Fact]
    public void Khong_co_phan_vi_thi_bao_thieu_du_lieu()
    {
        var context = ScoringFixtures.Context(plan: ScoringFixtures.Plan(atrPercentile: null));

        var result = new VolatilityRegimeCriterion().Evaluate(context);

        Assert.False(result.DataAvailable);
        Assert.Equal(0, result.AwardedPoints);
    }

    // ── market.session_quality ──────────────────────────────────────────

    [Fact]
    public void Chat_luong_phien_lay_nguyen_diem_cua_tang_khung_gio()
    {
        var context = ScoringFixtures.Context() with { SessionQuality = new SessionQuality(6, "Chồng lấn New York", true, 40) };

        var result = new SessionQualityCriterion().Evaluate(context);

        Assert.Equal(6, result.AwardedPoints);
        Assert.Contains("thống kê của bạn", result.Reason);
    }

    [Fact]
    public void Chat_luong_phien_dem_mong_duoc_diem_thap()
    {
        var context = ScoringFixtures.Context() with { SessionQuality = new SessionQuality(1, "Đêm mỏng", false, 0) };

        Assert.Equal(1, new SessionQualityCriterion().Evaluate(context).AwardedPoints);
    }

    [Fact]
    public void Khong_tinh_duoc_chat_luong_phien_thi_bao_thieu_du_lieu()
    {
        var context = ScoringFixtures.Context() with { SessionQuality = null };

        var result = new SessionQualityCriterion().Evaluate(context);

        Assert.False(result.DataAvailable);
        Assert.Equal(0, result.AwardedPoints);
    }

    // ── market.leader_correlation ───────────────────────────────────────

    [Fact]
    public void Giao_dich_chinh_ma_dan_dat_khong_bi_phat()
    {
        // BTC không mang rủi ro lệch pha với chính nó — trả "thiếu dữ liệu" ở đây sẽ phạt
        // BTC 4 điểm mỗi lần chấm vì một rủi ro nó không có.
        var context = ScoringFixtures.Context(symbol: ScoringFixtures.Leader) with { LeaderCorrelation = null };

        var result = new LeaderCorrelationCriterion().Evaluate(context);

        Assert.Equal(4, result.AwardedPoints);
        Assert.True(result.DataAvailable);
    }

    [Fact]
    public void Tuong_quan_manh_duoc_diem_toi_da()
    {
        var context = ScoringFixtures.Context() with { LeaderCorrelation = 0.9m };

        Assert.Equal(4, new LeaderCorrelationCriterion().Evaluate(context).AwardedPoints);
    }

    [Fact]
    public void Tuong_quan_qua_yeu_duoc_0_diem()
    {
        var context = ScoringFixtures.Context() with { LeaderCorrelation = 0.1m };

        Assert.Equal(0, new LeaderCorrelationCriterion().Evaluate(context).AwardedPoints);
    }

    [Fact]
    public void Khong_tinh_duoc_tuong_quan_thi_bao_thieu_du_lieu()
    {
        var context = ScoringFixtures.Context() with { LeaderCorrelation = null };

        var result = new LeaderCorrelationCriterion().Evaluate(context);

        Assert.False(result.DataAvailable);
        Assert.Equal(0, result.AwardedPoints);
    }

    // ── market.funding_crowding ─────────────────────────────────────────

    [Fact]
    public void Phi_von_cuc_doan_CUNG_chieu_lenh_bi_tru_het_diem()
    {
        var context = ScoringFixtures.Context(direction: TradeDirection.Long)
            with { Funding = ScoringFixtures.Funding(0.001m) };

        var result = new FundingCrowdingCriterion().Evaluate(context);

        Assert.Equal(0, result.AwardedPoints);
        Assert.Contains("CÙNG chiều", result.Reason);
    }

    [Fact]
    public void Phi_von_cuc_doan_NGUOC_chieu_lenh_khong_bi_tru()
    {
        var context = ScoringFixtures.Context(direction: TradeDirection.Short)
            with { Funding = ScoringFixtures.Funding(0.001m) };

        Assert.Equal(4, new FundingCrowdingCriterion().Evaluate(context).AwardedPoints);
    }

    [Fact]
    public void Phi_von_binh_thuong_duoc_diem_toi_da()
    {
        var context = ScoringFixtures.Context() with { Funding = ScoringFixtures.Funding(0.00001m) };

        Assert.Equal(4, new FundingCrowdingCriterion().Evaluate(context).AwardedPoints);
    }

    [Fact]
    public void Nguong_phi_von_cuc_doan_doc_tu_cau_hinh()
    {
        var context = ScoringFixtures.Context(
            direction: TradeDirection.Long,
            settings: ScoringFixtures.Settings(s => s.ExtremeFundingRate = 0.01m))
            with { Funding = ScoringFixtures.Funding(0.001m) };

        // Cùng con số phí vốn, chỉ khác ngưỡng ⟹ không còn bị coi là cực đoan.
        Assert.Equal(4, new FundingCrowdingCriterion().Evaluate(context).AwardedPoints);
    }

    [Fact]
    public void Khong_lay_duoc_phi_von_thi_bao_thieu_du_lieu()
    {
        var context = ScoringFixtures.Context() with { Funding = null };

        var result = new FundingCrowdingCriterion().Evaluate(context);

        Assert.False(result.DataAvailable);
        Assert.Equal(0, result.AwardedPoints);
    }
}
