namespace MMW.Application.RuleEngine;

public interface IRuleEngine
{
    IReadOnlyList<RuleViolation> Evaluate(RuleEvaluationContext ctx);
}

/// <summary>
/// Chạy toàn bộ ITradeRule đã đăng ký và gom các vi phạm.
/// Thêm rule mới = thêm 1 class ITradeRule + đăng ký DI, không sửa engine.
/// </summary>
public class TradeRuleEngine : IRuleEngine
{
    private readonly IEnumerable<ITradeRule> _rules;

    public TradeRuleEngine(IEnumerable<ITradeRule> rules) => _rules = rules;

    public IReadOnlyList<RuleViolation> Evaluate(RuleEvaluationContext ctx)
    {
        var violations = new List<RuleViolation>();
        foreach (var rule in _rules)
        {
            var v = rule.Evaluate(ctx);
            if (v is not null)
                violations.Add(v);
        }
        return violations;
    }
}
