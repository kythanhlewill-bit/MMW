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
    private const decimal WeekendRiskCap = 0.5m;

    // Hai trần dưới đây vốn là 3 (ngày đi ngang) và 2 (cuối tuần). Nâng tạm cho giai đoạn quan
    // sát testnet 2026-08-13 — nếu để nguyên thì hằng số 20 của RegimeTable không có tác dụng,
    // vì Resolve lấy MIN. Hạ lại cùng lúc với RegimeTable.ObservationMaxTradesPerDay.
    private const int RangeMaxTrades = RegimeTable.ObservationMaxTradesPerDay;
    private const int WeekendMaxTrades = RegimeTable.ObservationMaxTradesPerDay;

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
