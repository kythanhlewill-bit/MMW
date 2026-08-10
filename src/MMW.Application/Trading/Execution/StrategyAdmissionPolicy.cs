using MMW.Application.Trading.Scoring;
using MMW.Domain.Enums;

namespace MMW.Application.Trading.Execution;

public sealed record StrategyAdmissionDecision(bool Passed, int ExhaustionCount, string DetailVi)
{
    public static StrategyAdmissionDecision Allow(int exhaustion = 0) =>
        new(true, exhaustion, "Qua admission của strategy version.");
}

public interface IStrategyAdmissionPolicy
{
    StrategyAdmissionDecision Evaluate(
        TradingStrategyVersion version,
        SetupTriggerDecision trigger,
        ScoringOutcome score,
        DateTime entryUtc);
}

/// <summary>Admission V5 đã đóng băng; V6 giữ nó cho trend và bổ sung playbook sideway riêng.</summary>
public sealed class StrategyAdmissionPolicy : IStrategyAdmissionPolicy
{
    private static readonly string[] ExhaustionKeys =
    {
        "technical.htf_alignment",
        "technical.momentum",
        "market.volatility_regime",
    };

    public StrategyAdmissionDecision Evaluate(
        TradingStrategyVersion version,
        SetupTriggerDecision trigger,
        ScoringOutcome score,
        DateTime entryUtc)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        ArgumentNullException.ThrowIfNull(score);

        if (!version.UsesV5Admission()) return StrategyAdmissionDecision.Allow();

        var exhaustion = score.Lines.Count(line =>
            ExhaustionKeys.Contains(line.Key, StringComparer.Ordinal)
            && line.Result.DataAvailable
            && line.Result.AwardedPoints >= line.MaxPoints);

        if (entryUtc.DayOfWeek == DayOfWeek.Sunday)
            return new StrategyAdmissionDecision(false, exhaustion,
                "V5/V6 không admission lệnh mới vào Chủ nhật UTC.");

        var trendSetup = trigger.SetupType is SetupType.TrendPullback or SetupType.StrongTrendBreakout;
        if (trigger.SetupType == SetupType.TrendPullback)
            return new StrategyAdmissionDecision(false, exhaustion,
                "TrendPullback bị giữ ở shadow từ V5; chỉ StrongTrendBreakout được admission.");

        if (trendSetup && exhaustion >= 2)
            return new StrategyAdmissionDecision(false, exhaustion,
                $"ExhaustionCount={exhaustion} vượt trần 1 của V5/V6.");

        return StrategyAdmissionDecision.Allow(exhaustion);
    }
}
