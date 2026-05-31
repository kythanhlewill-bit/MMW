using MMW.Application.MarketData.Models;
using MMW.Domain.Enums;

namespace MMW.Application.MarketData;

public class SignalGenerator : ISignalGenerator
{
    /// <summary>SL cách entry = 1.5 × ATR.</summary>
    private const decimal StopAtrMultiple = 1.5m;

    /// <summary>TP theo Reward:Risk = 2.</summary>
    private const decimal RewardRisk = 2m;

    public SuggestedSignal? Generate(MarketAnalysis a, int minScore = 2)
    {
        if (a.Bias == MarketBias.Neutral || Math.Abs(a.Score) < Math.Max(1, minScore))
            return null;

        if (a.Atr is not > 0m || a.Price <= 0m)
            return null;

        var atr = a.Atr.Value;
        var stopDistance = StopAtrMultiple * atr;
        var takeDistance = stopDistance * RewardRisk;

        var direction = a.Bias == MarketBias.Bullish ? TradeDirection.Long : TradeDirection.Short;

        decimal stopLoss, takeProfit;
        if (direction == TradeDirection.Long)
        {
            stopLoss = a.Price - stopDistance;
            takeProfit = a.Price + takeDistance;
        }
        else
        {
            stopLoss = a.Price + stopDistance;
            takeProfit = a.Price - takeDistance;
        }

        return new SuggestedSignal(
            direction,
            a.Bias,
            a.Score,
            a.Price,
            Round8(stopLoss),
            Round8(takeProfit),
            RewardRisk,
            a.Notes);
    }

    private static decimal Round8(decimal v) => Math.Round(v, 8, MidpointRounding.AwayFromZero);
}
