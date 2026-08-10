using MMW.Application.MarketData.Models;
using MMW.Domain.Entities;
using MMW.Domain.Enums;

namespace MMW.Application.Trading.DailyPlanning;

public sealed record IntradayRegimeDecision(
    DayRegime Regime,
    AllowedDirections AllowedDirections,
    bool IsOverride,
    string ReasonVi);

/// <summary>
/// Nhận diện một ngày Range đã chuyển thành trend trong phiên bằng state machine tái dựng hoàn
/// toàn từ nến ĐÃ ĐÓNG. Không giữ mutable state nên backtest và live cho cùng kết quả sau restart.
/// </summary>
public static class IntradayRegimeOverridePolicy
{
    private const int RangeLookbackBars = 32;
    private const int VolumeLookbackBars = 20;
    private const int ConfirmBars = 2;
    private const int ReleaseBars = 2;
    private const int CooldownBars = 8;

    public static IntradayRegimeDecision Resolve(
        DailyPlan plan,
        IReadOnlyList<Candle> entryCandles,
        EngineSetting setting)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(entryCandles);
        ArgumentNullException.ThrowIfNull(setting);

        if (plan.DayRegime != DayRegime.Range)
            return Original(plan, "Kế hoạch ngày không phải Range; không áp override trong phiên.");

        var candles = entryCandles.OrderBy(c => c.OpenTime).ToList();
        var dayStart = plan.PlanDateUtc.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var firstToday = candles.FindIndex(c => c.OpenTime >= dayStart);
        if (firstToday < 0 || candles.Count < RangeLookbackBars + ConfirmBars)
            return Original(plan, "Chưa đủ nến đã đóng để xác nhận breakout trong phiên.");

        TradeDirection? active = null;
        decimal breakoutBoundary = 0m;
        var upConfirm = 0;
        var downConfirm = 0;
        var releaseConfirm = 0;
        var cooldown = 0;

        for (var i = Math.Max(firstToday, RangeLookbackBars); i < candles.Count; i++)
        {
            var candle = candles[i];
            if (candle.OpenTime < dayStart) continue;

            if (cooldown > 0) cooldown--;

            if (active is { } trend)
            {
                var failed = trend == TradeDirection.Long
                    ? candle.Close <= breakoutBoundary
                    : candle.Close >= breakoutBoundary;
                releaseConfirm = failed ? releaseConfirm + 1 : 0;

                if (releaseConfirm >= ReleaseBars)
                {
                    active = null;
                    releaseConfirm = 0;
                    upConfirm = 0;
                    downConfirm = 0;
                    cooldown = CooldownBars;
                }

                continue;
            }

            if (cooldown > 0) continue;

            var baseline = candles.Skip(i - RangeLookbackBars).Take(RangeLookbackBars).ToList();
            var high = baseline.Max(c => c.High);
            var low = baseline.Min(c => c.Low);
            var averageVolume = candles.Skip(Math.Max(0, i - VolumeLookbackBars))
                .Take(Math.Min(VolumeLookbackBars, i))
                .Select(c => c.Volume)
                .DefaultIfEmpty(0m)
                .Average();
            var range = candle.High - candle.Low;
            var bodyRatio = range <= 0m ? 0m : Math.Abs(candle.Close - candle.Open) / range;
            var volumeConfirmed = averageVolume > 0m
                                  && candle.Volume >= averageVolume * setting.VolumeBreakoutMultiple;
            var bodyConfirmed = bodyRatio >= setting.MinCandleBodyRatio;

            var breaksUp = candle.Close > high && candle.Close > candle.Open
                           && volumeConfirmed && bodyConfirmed;
            var breaksDown = candle.Close < low && candle.Close < candle.Open
                             && volumeConfirmed && bodyConfirmed;

            upConfirm = breaksUp ? upConfirm + 1 : 0;
            downConfirm = breaksDown ? downConfirm + 1 : 0;

            if (upConfirm >= ConfirmBars)
            {
                active = TradeDirection.Long;
                breakoutBoundary = high;
                releaseConfirm = 0;
            }
            else if (downConfirm >= ConfirmBars)
            {
                active = TradeDirection.Short;
                breakoutBoundary = low;
                releaseConfirm = 0;
            }
        }

        return active switch
        {
            TradeDirection.Long => new IntradayRegimeDecision(
                DayRegime.TrendUp,
                AllowedDirections.LongOnly,
                true,
                $"Override Range→TrendUp: 2 breakout liên tiếp có volume; biên xác nhận {breakoutBoundary:N2}."),
            TradeDirection.Short => new IntradayRegimeDecision(
                DayRegime.TrendDown,
                AllowedDirections.ShortOnly,
                true,
                $"Override Range→TrendDown: 2 breakout liên tiếp có volume; biên xác nhận {breakoutBoundary:N2}."),
            _ => Original(plan, "Range chưa có hai breakout volume liên tiếp còn hiệu lực."),
        };
    }

    /// <summary>Tạo view hiệu lực; tuyệt đối không sửa DailyPlan bất biến đã lưu.</summary>
    public static DailyPlan Apply(DailyPlan source, IntradayRegimeDecision decision)
    {
        if (!decision.IsOverride) return source;

        return new DailyPlan
        {
            Id = source.Id,
            CreatedDate = source.CreatedDate,
            CreatedUser = source.CreatedUser,
            UpdatedDate = source.UpdatedDate,
            UpdatedUser = source.UpdatedUser,
            TradingAccountId = source.TradingAccountId,
            TradingAccount = source.TradingAccount,
            PlanDateUtc = source.PlanDateUtc,
            GeneratedAtUtc = source.GeneratedAtUtc,
            DayRegime = decision.Regime,
            VolatilityRegime = source.VolatilityRegime,
            AllowedDirections = decision.AllowedDirections,
            // Override chỉ đổi cách đọc regime/chiều; không được dùng nó để tự tăng risk/quota.
            RiskMultiplier = source.RiskMultiplier,
            MaxTradesToday = source.MaxTradesToday,
            PreviousDayHigh = source.PreviousDayHigh,
            PreviousDayLow = source.PreviousDayLow,
            WeeklyOpen = source.WeeklyOpen,
            DailyOpen = source.DailyOpen,
            BtcStructure = source.BtcStructure,
            AtrPercentile = source.AtrPercentile,
            FundingRate = source.FundingRate,
            OpenInterestChange24hPercent = source.OpenInterestChange24hPercent,
            LongShortAccountRatio = source.LongShortAccountRatio,
            FearGreedIndex = source.FearGreedIndex,
            MissingInputs = source.MissingInputs,
            IsComplete = source.IsComplete,
            AiDayRiskLevel = source.AiDayRiskLevel,
            AiNarrative = source.AiNarrative,
            AiConfidence = source.AiConfidence,
            AiAnswered = source.AiAnswered,
        };
    }

    private static IntradayRegimeDecision Original(DailyPlan plan, string reason) =>
        new(plan.DayRegime, plan.AllowedDirections, false, reason);
}
