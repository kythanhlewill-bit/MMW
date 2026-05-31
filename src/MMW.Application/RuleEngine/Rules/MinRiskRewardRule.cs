using System.Globalization;
using System.Text.Json;
using MMW.Domain.Enums;

namespace MMW.Application.RuleEngine.Rules;

/// <summary>Tỷ lệ Reward:Risk dự kiến không được thấp hơn ngưỡng tối thiểu.</summary>
public class MinRiskRewardRule : ITradeRule
{
    public FlagType Type => FlagType.LowRiskReward;

    public RuleViolation? Evaluate(RuleEvaluationContext ctx)
    {
        var rr = ctx.Trade.PlannedRiskReward;
        var min = ctx.Settings.MinRiskRewardRatio;

        if (rr is null || min <= 0m || rr.Value >= min)
            return null;

        var detail = JsonSerializer.Serialize(new
        {
            actualRR = rr.Value,
            minRR = min,
        });

        return new RuleViolation(
            Type,
            FlagSeverity.Warning,
            $"Reward:Risk {rr.Value.ToString("0.##", CultureInfo.InvariantCulture)} thấp hơn mức tối thiểu " +
            $"{min.ToString("0.##", CultureInfo.InvariantCulture)}.",
            detail);
    }
}
