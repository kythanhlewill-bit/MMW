using MMW.Domain.Enums;

namespace MMW.Application.Models;

public class TradeDto
{
    public long Id { get; set; }
    public long TradingAccountId { get; set; }
    public string? AccountName { get; set; }
    public long? StrategyId { get; set; }

    public string Symbol { get; set; } = null!;
    public TradeDirection Direction { get; set; }
    public TradeStatus Status { get; set; }
    public TradeSource Source { get; set; }

    public decimal EntryPrice { get; set; }
    public decimal? ExitPrice { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? TakeProfit { get; set; }
    public decimal Quantity { get; set; }
    public decimal? Leverage { get; set; }
    public decimal Fee { get; set; }
    public decimal? RealizedPnl { get; set; }

    public decimal? RiskAmount { get; set; }
    public decimal? RiskPercent { get; set; }
    public decimal? PlannedRiskReward { get; set; }
    public decimal? RMultiple { get; set; }
    public TradeOutcome? Outcome { get; set; }

    public EmotionState EmotionBefore { get; set; }
    public EmotionState EmotionAfter { get; set; }

    public DateTime? OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    public string? Note { get; set; }
    public string? ImageUrl { get; set; }
    public string? ExternalId { get; set; }
}
