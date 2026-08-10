using MMW.Domain.Entities;
using MMW.Domain.Enums;

namespace MMW.Application.Trading.Execution;

/// <summary>Economics của đúng execution plan, chuẩn hóa theo 1R risk budget.</summary>
public sealed record ExecutionViability(
    bool Passed,
    decimal GrossFirstTargetR,
    decimal TargetCostR,
    decimal StopCostR,
    decimal ExpectedCostR,
    decimal NetRiskReward,
    decimal CostToTargetPercent,
    decimal StopDistanceBps,
    string DetailVi);

public interface IExecutionViabilityPolicy
{
    ExecutionViability Evaluate(
        TradeExecutionPlan plan,
        TradeDirection direction,
        EngineSetting settings,
        bool enforceV3Gates,
        SetupType setupType = SetupType.None);
}

/// <summary>
/// Tính phí/slippage theo quantity thật của từng tranche. Gross R:R đẹp không được quyền che việc
/// entry gần stop làm quantity và cost/R tăng mạnh.
/// </summary>
public sealed class ExecutionViabilityPolicy : IExecutionViabilityPolicy
{
    public ExecutionViability Evaluate(
        TradeExecutionPlan plan,
        TradeDirection direction,
        EngineSetting settings,
        bool enforceV3Gates,
        SetupType setupType = SetupType.None)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(settings);

        var entryFeeR = 0m;
        var entrySlippageR = 0m;
        var targetFeeR = 0m;
        var stopFeeR = 0m;
        var stopSlippageR = 0m;
        var grossTargetR = 0m;

        foreach (var entry in plan.Entries)
        {
            var stopDistance = Math.Abs(entry.Price - plan.StopLoss);
            if (stopDistance <= 0m)
                return Reject("Entry trùng stop nên không tính được economics.");

            var quantityPerRiskR = entry.RiskWeight / stopDistance;
            var entryFee = entry.IsLimit
                ? settings.BacktestMakerFeePercent
                : settings.BacktestTakerFeePercent;

            entryFeeR += entry.Price * entryFee / 100m * quantityPerRiskR;
            if (!entry.IsLimit)
                entrySlippageR += entry.Price * settings.BacktestEntrySlippageBps / 10_000m * quantityPerRiskR;

            targetFeeR += plan.FirstTakeProfit * settings.BacktestMakerFeePercent / 100m * quantityPerRiskR;
            stopFeeR += plan.StopLoss * settings.BacktestTakerFeePercent / 100m * quantityPerRiskR;
            stopSlippageR += plan.StopLoss * settings.BacktestStopSlippageBps / 10_000m * quantityPerRiskR;

            var move = direction == TradeDirection.Long
                ? plan.FirstTakeProfit - entry.Price
                : entry.Price - plan.FirstTakeProfit;
            grossTargetR += move * quantityPerRiskR;
        }

        var targetCost = entryFeeR + entrySlippageR + targetFeeR;
        var stopCost = entryFeeR + entrySlippageR + stopFeeR + stopSlippageR;
        var expectedCost = Math.Max(targetCost, stopCost);
        var netTarget = grossTargetR - targetCost;
        var netRiskReward = (1m + stopCost) <= 0m ? 0m : netTarget / (1m + stopCost);
        var costToTarget = grossTargetR <= 0m ? decimal.MaxValue : expectedCost / grossTargetR * 100m;
        var first = plan.Entries[0].Price;
        var stopDistanceBps = first <= 0m ? 0m : Math.Abs(first - plan.StopLoss) / first * 10_000m;

        var minNetRr = setupType == SetupType.RectangleRangeFade
            ? settings.V6RangeMinNetRiskReward
            : setupType is SetupType.RectangleBreakout or SetupType.TriangleBreakout
                ? settings.V6BreakoutMinNetRiskReward
                : settings.V3MinNetRiskReward;
        var maxCostToTarget = setupType == SetupType.RectangleRangeFade
            ? settings.V6RangeMaxCostToTargetPercent
            : setupType is SetupType.RectangleBreakout or SetupType.TriangleBreakout
                ? settings.V6BreakoutMaxCostToTargetPercent
                : settings.V3MaxCostToTargetPercent;

        var passed = !enforceV3Gates
            || (netRiskReward >= minNetRr
                && costToTarget <= maxCostToTarget);
        var detail =
            $"grossTP={grossTargetR:N3}R, targetCost={targetCost:N3}R, stopCost={stopCost:N3}R, " +
            $"netRR={netRiskReward:N3}, cost/target={costToTarget:N1}%, stop={stopDistanceBps:N1}bps";

        if (enforceV3Gates && !passed)
            detail += $"; setup {setupType} cần netRR≥{minNetRr:N2} và cost/target≤{maxCostToTarget:N1}%.";

        return new ExecutionViability(
            passed, grossTargetR, targetCost, stopCost, expectedCost,
            netRiskReward, costToTarget, stopDistanceBps, detail);
    }

    private static ExecutionViability Reject(string detail) =>
        new(false, 0m, 0m, 0m, decimal.MaxValue, 0m, decimal.MaxValue, 0m, detail);
}
