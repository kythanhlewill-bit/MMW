using MMW.Application.Trading.Scoring;
using MMW.Domain.Entities;
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
        DateTime entryUtc,
        EngineSetting? settings = null);
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
        DateTime entryUtc,
        EngineSetting? settings = null)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        ArgumentNullException.ThrowIfNull(score);

        // Danh sách setup bị cấm đứng TRƯỚC cổng phiên bản. Nó là quyết định của người vận hành
        // dựa trên kết cục đã đo, nên nó phải áp cho mọi bộ luật — kể cả bộ luật không dùng
        // admission V5, vốn sẽ thoát ngay ở dòng dưới và mang setup đã bị cấm đi thẳng vào lệnh.
        if (settings is not null && trigger.SetupType != SetupType.None)
        {
            var disabled = EngineSetting.ParseDisabledSetups(settings.DisabledSetupTypes, out _);
            if (disabled.Contains(trigger.SetupType))
            {
                return new StrategyAdmissionDecision(false, 0,
                    $"Setup {trigger.SetupType} nằm trong DisabledSetupTypes — cấu hình đã tắt " +
                    "theo kết cục đo được, không phải theo phán đoán.");
            }
        }

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
