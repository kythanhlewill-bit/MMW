using System.Globalization;
using System.Text.Json;
using MMW.Domain.Enums;

namespace MMW.Application.RuleEngine.Rules;

/// <summary>% rủi ro mỗi lệnh không được vượt ngưỡng. Vượt ≥2 lần ngưỡng → Critical.</summary>
public class MaxRiskPerTradeRule : ITradeRule
{
    public FlagType Type => FlagType.RiskExceeded;

    public RuleViolation? Evaluate(RuleEvaluationContext ctx)
    {
        var risk = ctx.Trade.RiskPercent;
        var max = ctx.Settings.MaxRiskPerTradePercent;

        if (risk is null || max <= 0m || risk.Value <= max)
            return null;

        var severity = risk.Value >= max * 2m ? FlagSeverity.Critical : FlagSeverity.Warning;

        var detail = JsonSerializer.Serialize(new
        {
            actualPercent = risk.Value,
            maxPercent = max,
        });

        return new RuleViolation(
            Type,
            severity,
            $"Rủi ro {risk.Value.ToString("0.##", CultureInfo.InvariantCulture)}% vượt ngưỡng " +
            $"{max.ToString("0.##", CultureInfo.InvariantCulture)}% mỗi lệnh.",
            detail);
    }
}
