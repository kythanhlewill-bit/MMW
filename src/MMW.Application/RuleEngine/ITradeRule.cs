using MMW.Domain.Enums;

namespace MMW.Application.RuleEngine;

/// <summary>
/// Một luật cứng. Trả về RuleViolation nếu vi phạm, null nếu hợp lệ/không áp dụng.
/// </summary>
public interface ITradeRule
{
    FlagType Type { get; }

    RuleViolation? Evaluate(RuleEvaluationContext ctx);
}
