using MMW.Domain.Enums;

namespace MMW.Application.RuleEngine.Rules;

/// <summary>Lệnh bắt buộc phải có Stop Loss (rủi ro không giới hạn nếu thiếu).</summary>
public class RequireStopLossRule : ITradeRule
{
    public FlagType Type => FlagType.NoStopLoss;

    public RuleViolation? Evaluate(RuleEvaluationContext ctx)
    {
        if (!ctx.Settings.RequireStopLoss)
            return null;

        if (ctx.Trade.StopLoss.HasValue)
            return null;

        return new RuleViolation(
            Type,
            FlagSeverity.Critical,
            "Lệnh không đặt Stop Loss — rủi ro không giới hạn.");
    }
}
