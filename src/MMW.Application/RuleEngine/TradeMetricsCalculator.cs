using MMW.Domain.Entities;
using MMW.Domain.Enums;

namespace MMW.Application.RuleEngine;

public interface ITradeMetricsCalculator
{
    /// <summary>
    /// Tính các chỉ số rủi ro deterministic và ghi thẳng vào entity Trade.
    /// </summary>
    void Compute(Trade trade, decimal accountEquity);
}

/// <summary>
/// Công thức rủi ro thuần — không phụ thuộc DB, dễ unit test.
///   RiskAmount        = |Entry - StopLoss| * Quantity
///   RiskPercent       = RiskAmount / Equity * 100
///   PlannedRiskReward = |TakeProfit - Entry| / |Entry - StopLoss|
///   RMultiple         = RealizedPnl / RiskAmount
/// </summary>
public class TradeMetricsCalculator : ITradeMetricsCalculator
{
    public void Compute(Trade trade, decimal accountEquity)
    {
        decimal? slDistance = trade.StopLoss.HasValue
            ? Math.Abs(trade.EntryPrice - trade.StopLoss.Value)
            : null;

        // Risk amount & percent
        if (slDistance is > 0m)
        {
            trade.RiskAmount = Round8(slDistance.Value * trade.Quantity);

            if (accountEquity > 0m && trade.RiskAmount is > 0m)
                trade.RiskPercent = Round4(trade.RiskAmount.Value / accountEquity * 100m);
        }
        else
        {
            trade.RiskAmount = null;
            trade.RiskPercent = null;
        }

        // Planned Reward:Risk
        if (slDistance is > 0m && trade.TakeProfit.HasValue)
        {
            var rewardDistance = Math.Abs(trade.TakeProfit.Value - trade.EntryPrice);
            trade.PlannedRiskReward = Round4(rewardDistance / slDistance.Value);
        }
        else
        {
            trade.PlannedRiskReward = null;
        }

        // Outcome & R-multiple (chỉ khi đã có kết quả)
        if (trade.RealizedPnl.HasValue)
        {
            trade.Outcome = trade.RealizedPnl.Value > 0m ? TradeOutcome.Win
                          : trade.RealizedPnl.Value < 0m ? TradeOutcome.Loss
                          : TradeOutcome.BreakEven;

            if (trade.RiskAmount is > 0m)
                trade.RMultiple = Round4(trade.RealizedPnl.Value / trade.RiskAmount.Value);
        }
    }

    private static decimal Round8(decimal v) => Math.Round(v, 8, MidpointRounding.AwayFromZero);
    private static decimal Round4(decimal v) => Math.Round(v, 4, MidpointRounding.AwayFromZero);
}
