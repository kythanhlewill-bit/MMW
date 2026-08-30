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

        // Kế hoạch chốt hai phần của V7 được chấm trên CẢ HAI mục tiêu, theo đúng tỉ lệ đóng ở
        // mỗi mức. Chấm riêng mục tiêu gần là đo nửa lệnh rồi kết luận cho cả lệnh, và với V7 nó
        // sai theo hướng chí mạng: bộ luật đó cố tình đặt mục tiêu gần ở khoảng 1,0R và dồn phần
        // lãi vào runner ở 2,5R trở lên, nên cổng chi phí sẽ đánh trượt gần như MỌI setup của nó
        // vì "R:R quá thấp" — trong khi kinh tế thật rất tốt.
        //
        // Đây là giá bình quân gia quyền của hai lần thoát, không phải kỳ vọng có xác suất: nó
        // giả định cả hai mục tiêu đều chạm. Lạc quan, nhưng lạc quan đúng bằng mức mà cách tính
        // một-mục-tiêu vẫn luôn giả định.
        //
        // ⚠️ CHỈ áp cho V7, và điều kiện là phiên bản chứ không phải "kế hoạch có runner hay
        // không". Kế hoạch V6 cũng có runner, nên nới điều kiện ra sẽ đổi luôn kinh tế của bộ
        // luật đang chạy thật: cùng phiếu 13:31 ngày 14/08 nhảy từ gross 1,960R lên 2,049R, tức
        // cổng chi phí bắt đầu cho qua những setup mà nó đã từng chặn. Đó là một thay đổi chiến
        // lược, và nó không được phép xảy ra như tác dụng phụ của việc thêm một bộ luật mới.
        var blendedTarget = plan.RunnerTakeProfit is { } runner && settings.StrategyVersion.UsesHtfSwing()
            ? plan.FirstTakeProfit * plan.FirstTakeProfitFraction + runner * (1m - plan.FirstTakeProfitFraction)
            : plan.FirstTakeProfit;

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

            targetFeeR += blendedTarget * settings.BacktestMakerFeePercent / 100m * quantityPerRiskR;
            stopFeeR += plan.StopLoss * settings.BacktestTakerFeePercent / 100m * quantityPerRiskR;
            stopSlippageR += plan.StopLoss * settings.BacktestStopSlippageBps / 10_000m * quantityPerRiskR;

            var move = direction == TradeDirection.Long
                ? blendedTarget - entry.Price
                : entry.Price - blendedTarget;
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

        // ── Hai cổng TUYỆT ĐỐI, đứng cạnh hai cổng tương đối ở trên ─────────
        //
        // netRR và cost/target đều là tỉ lệ, nên cả hai đều có thể thoả bằng cách kéo mục tiêu ra
        // xa thay vì bằng việc lệnh thật sự rẻ. Phiếu #3496 làm đúng như thế: phí 1,573R mà
        // cost/target vẫn chỉ 15% vì gross target lên tới 10R. Lệnh #63 sinh ra từ nó mất 1,77R.
        //
        // Hai ngưỡng dưới đây không đo hình học của setup mà đo cái giá bước vào, nên chúng bịt
        // đúng chỗ mà tỉ lệ không nhìn thấy. Chúng cũng là lớp chặn CUỐI cho khoảng cách dừng lỗ:
        // MinStopDistancePercent được áp trong từng nhánh trigger, nhưng chỉ 4/8 nhánh áp nó, và
        // ngay cả nhánh có áp cũng bị mức chờ thụ động gặm lại — xem PassiveLimitEntry. Ở đây là
        // nơi duy nhất mọi kế hoạch đều đi qua, nên là nơi duy nhất chặn được cả hai lỗ hổng.
        var minStopBps = settings.MinStopDistancePercent * 100m;
        var stopTooTight = stopDistanceBps < minStopBps;
        var costTooHigh = expectedCost > settings.MaxExpectedCostR;

        var passed = !enforceV3Gates
            || (netRiskReward >= minNetRr
                && costToTarget <= maxCostToTarget
                && !stopTooTight
                && !costTooHigh);
        var detail =
            $"grossTP={grossTargetR:N3}R, targetCost={targetCost:N3}R, stopCost={stopCost:N3}R, " +
            $"netRR={netRiskReward:N3}, cost/target={costToTarget:N1}%, stop={stopDistanceBps:N1}bps";

        if (enforceV3Gates && !passed)
        {
            detail += $"; setup {setupType} cần netRR≥{minNetRr:N2} và cost/target≤{maxCostToTarget:N1}%.";

            if (stopTooTight)
                detail += $" Dừng lỗ {stopDistanceBps:N1}bps dưới sàn {minStopBps:N0}bps — " +
                          "khối lượng = rủi ro/khoảng dừng lỗ, nên dừng lỗ hẹp là phí cao.";

            if (costTooHigh)
                detail += $" Chi phí dự kiến {expectedCost:N3}R vượt trần {settings.MaxExpectedCostR:N2}R.";
        }

        return new ExecutionViability(
            passed, grossTargetR, targetCost, stopCost, expectedCost,
            netRiskReward, costToTarget, stopDistanceBps, detail);
    }

    private static ExecutionViability Reject(string detail) =>
        new(false, 0m, 0m, 0m, decimal.MaxValue, 0m, decimal.MaxValue, 0m, detail);
}
