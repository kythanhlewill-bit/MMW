using MMW.Domain.Enums;

namespace MMW.Application.Trading.DailyPlanning;

public sealed record AdaptiveRegimeLimits(decimal RiskMultiplier, int MaxTradesToday);

/// <summary>
/// Điều chỉnh nhịp giao dịch theo regime và thanh khoản lịch. Đây là cap mềm, không phải cấm:
/// setup tốt vẫn được vào nhưng nhỏ và ít hơn ở range/cuối tuần.
/// </summary>
public static class AdaptiveRegimePolicy
{
    private const decimal RangeRiskCap = 0.6m;
    private const int RangeMaxTrades = 3;
    private const decimal WeekendRiskCap = 0.5m;
    private const int WeekendMaxTrades = 2;

    public static AdaptiveRegimeLimits Apply(
        DateOnly planDateUtc,
        DayRegime regime,
        decimal riskMultiplier,
        int maxTradesToday)
    {
        var risk = Math.Clamp(riskMultiplier, 0m, 1m);
        var maxTrades = Math.Max(0, maxTradesToday);

        if (regime == DayRegime.Range)
        {
            risk = Math.Min(risk, RangeRiskCap);
            maxTrades = Math.Min(maxTrades, RangeMaxTrades);
        }

        var day = planDateUtc.DayOfWeek;
        if (day is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            risk = Math.Min(risk, WeekendRiskCap);
            maxTrades = Math.Min(maxTrades, WeekendMaxTrades);
        }

        return new AdaptiveRegimeLimits(risk, maxTrades);
    }
}
