using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MMW.Application.MarketData.Models;
using MMW.Application.Services;
using MMW.Application.Trading.Scoring;
using MMW.Domain.DbContext;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.RuleEngine.Tests.TimeGuard;
using Xunit;

namespace MMW.RuleEngine.Tests.Scoring;

/// <summary>
/// V2 §4 chạy xuyên suốt <see cref="SignalEvalService"/>, không chỉ trong lớp thuần.
/// </summary>
/// <remarks>
/// Test đơn vị của <c>DirectionPolicy</c> và <c>DirectionSelector</c> chứng minh hai lớp đó đúng.
/// Chúng KHÔNG chứng minh rằng chu kỳ đánh giá thật sự gọi tới chúng — và một quy tắc đúng nhưng
/// chưa được nối dây trông y hệt một quy tắc đang chạy, cho tới lần backtest đầu tiên.
/// </remarks>
public class DirectionSelectionFlowTests
{
    private static readonly DateTime RunAt = new(2026, 8, 5, 14, 1, 0, DateTimeKind.Utc);
    private const string Symbol = "BTCUSDT";

    [Fact]
    public async Task Ngay_range_giua_bien_do_bi_chan_truoc_khi_cham_diem()
    {
        var (harness, bounds) = await SetupAsync(DayRegime.Range);
        harness.MarketData.Prices[Symbol] = (bounds.Low + bounds.High) / 2m;

        var card = await EvaluateAsync(harness);

        Assert.Equal(ScorecardOutcome.Vetoed, card.Outcome);
        Assert.Equal(VetoReason.NotAtRangeEdge, card.VetoReason);
        Assert.Equal(0m, card.FinalSizeR);
        Assert.NotNull(card.RangePositionPercent);
        Assert.InRange(card.RangePositionPercent!.Value, 40m, 60m);

        // Chặn TRƯỚC khi chấm: không có dòng tiêu chí nào được ghi vì không tiêu chí nào chạy.
        Assert.Empty(card.Lines);

        harness.Dispose();
    }

    [Fact]
    public async Task Ngay_range_sat_bien_duoi_thi_chi_con_chieu_mua()
    {
        var (harness, bounds) = await SetupAsync(DayRegime.Range);
        harness.MarketData.Prices[Symbol] = bounds.Low + (bounds.High - bounds.Low) * 0.1m;

        var card = await EvaluateAsync(harness);

        Assert.Equal(TradeDirection.Long, card.Direction);
        Assert.NotEqual(VetoReason.NotAtRangeEdge, card.VetoReason);
        Assert.NotEmpty(card.Lines);

        // Vị trí trong biên độ được ghi lại kể cả khi nó KHÔNG chặn gì — đó là thứ trả lời câu
        // "hôm đó giá đứng ở đâu" mà không phải dựng lại chuỗi nến.
        Assert.NotNull(card.RangePositionPercent);
        Assert.InRange(card.RangePositionPercent!.Value, 5m, 15m);

        harness.Dispose();
    }

    /// <summary>
    /// Kế hoạch cho cả hai chiều ⟹ CẢ HAI được chấm, và cả hai điểm được ghi vào phiếu.
    /// </summary>
    [Fact]
    public async Task Ngay_cho_ca_hai_chieu_thi_cham_ca_hai_va_ghi_ca_hai_diem()
    {
        var (harness, _) = await SetupAsync(
            DayRegime.HighVolatility, NoPivots());

        var card = await EvaluateAsync(harness);

        Assert.NotNull(card.OppositeScore);
        Assert.NotNull(card.OppositeDirectionalScore);

        // Chiều được chọn phải là chiều có điểm đổi-theo-chiều CAO HƠN.
        Assert.True(card.DirectionalScore >= card.OppositeDirectionalScore,
            $"Chọn chiều {card.Direction} với {card.DirectionalScore} điểm nhưng chiều kia được " +
            $"{card.OppositeDirectionalScore}.");

        harness.Dispose();
    }

    /// <summary>
    /// A/B #23/#24 bác bỏ biên veto; hai chiều gần nhau không còn sinh DirectionUnclear.
    /// </summary>
    /// <remarks>
    /// Chọn chiều vẫn tất định và chỉ threshold/gate thật mới được quyền chặn.
    /// </remarks>
    [Fact]
    public async Task Hai_chieu_gan_diem_khong_con_bi_gan_nhan_DirectionUnclear()
    {
        var (harness, _) = await SetupAsync(
            DayRegime.HighVolatility, NoPivots());

        var card = await EvaluateAsync(harness);

        Assert.NotEqual(VetoReason.DirectionUnclear, card.VetoReason);
        Assert.NotNull(card.OppositeDirectionalScore);

        harness.Dispose();
    }

    // ── Bộ dựng ─────────────────────────────────────────────────────────

    /// <summary>
    /// Chuỗi tăng đơn điệu — KHÔNG sinh điểm xoay nào, nên cả hai chiều cùng rơi vào mức dự phòng.
    /// </summary>
    /// <remarks>
    /// Cần thiết cho các test về CHỌN CHIỀU: trên chuỗi răng cưa, mục tiêu cấu trúc của một chiều
    /// luôn là đỉnh/đáy kế tiếp cách chỉ nửa biên độ trong khi dừng lỗ cách trọn một biên độ, nên
    /// rào 1,6R của <c>technical.structural_room</c> loại sạch một chiều và phép so không còn gì
    /// để so. Ở đây cả hai chiều nhận cùng một mức dự phòng đối xứng, nên khác biệt duy nhất giữa
    /// chúng đúng là thứ đang được đo: điểm đổi theo chiều.
    ///
    /// Đường lui này KHÔNG nới rào nào cả — <c>MinStructuralRr</c> giữ nguyên mặc định 1,6 và
    /// mức dự phòng đạt 2,0R nhờ <c>RiskSetting.MinRiskRewardRatio</c>.
    /// </remarks>
    private static List<Candle> NoPivots() => ScoringFixtures.Ramp(300);

    private static List<Candle> WithPivots() =>
        ScoringFixtures.ZigZag(300, interval: TimeSpan.FromMinutes(15));

    private static async Task<(TimeGuardHarness Harness, RangeLocation Bounds)> SetupAsync(
        DayRegime regime, List<Candle>? candles = null, Action<EngineSetting>? configure = null)
    {
        var harness = await TimeGuardHarness.CreateAsync(s =>
        {
            s.Symbols = Symbol;
            configure?.Invoke(s);
        });

        harness.Clock.UtcNow = RunAt;

        var series = candles ?? WithPivots();
        harness.MarketData.Candles[Symbol] = series;
        harness.MarketData.FearGreed = 55;

        // Phí vốn có mặt để tỉ lệ phủ dữ liệu vượt ngưỡng 75% — nếu không, cả lượt chấm bị veto
        // vì "quá mù" và test đo nhầm rào khác.
        harness.MarketData.FundingRate = 0.0001m;

        // Nguồn nến giả trả cùng một chuỗi cho mọi khung, nên biên độ khung thiên hướng đọc từ
        // đúng chuỗi này — sau khi cắt đuôi nến chưa đóng, y như chu kỳ đánh giá thật làm.
        var closed = series.Where(c => c.CloseTime <= RunAt).ToList();
        var bounds = new DirectionPolicy(ScoringFixtures.Swings).Locate(closed, 2, price: 1m)
                     ?? new RangeLocation(closed[^1].Close, closed[^1].Close, 0m, 0);

        harness.MarketData.Prices[Symbol] = bounds.High > bounds.Low
            ? (bounds.Low + bounds.High) / 2m
            : closed[^1].Close;

        // Kế hoạch ngày ghi thẳng vào cơ sở dữ liệu: bộ phân loại đọc cấu trúc BTC nhiều ngày,
        // và dựng một chuỗi nến ra đúng regime cần thử là ghim gián tiếp qua ba tầng.
        using (var scope = harness.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();

            var risk = await db.RiskSettings.FirstAsync(r => r.TradingAccountId == harness.AccountId);
            risk.MinRiskRewardRatio = 2.0m;

            db.DailyPlans.Add(new DailyPlan
            {
                TradingAccountId = harness.AccountId,
                PlanDateUtc = DateOnly.FromDateTime(RunAt),
                GeneratedAtUtc = RunAt.AddHours(-14),
                DayRegime = regime,
                VolatilityRegime = VolatilityRegime.Normal,
                AllowedDirections = AllowedDirections.Both,
                RiskMultiplier = 1.0m,
                MaxTradesToday = 5,
                AtrPercentile = 50m,
                IsComplete = true,
            });
            await db.SaveChangesAsync();
        }

        return (harness, bounds);
    }

    private static async Task<EntryScorecard> EvaluateAsync(TimeGuardHarness harness)
    {
        using var scope = harness.NewScope();
        var service = scope.ServiceProvider.GetRequiredService<ISignalEvalService>();
        return await service.EvaluateAsync(harness.AccountId, Symbol, RunAt);
    }
}
