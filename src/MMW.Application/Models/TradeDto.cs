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
    public OrderType OrderType { get; set; }

    public decimal EntryPrice { get; set; }
    public decimal? ExitPrice { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? TakeProfit { get; set; }

    // --- Chốt hai phần (V7) ---
    public decimal? FirstTakeProfit { get; set; }
    public decimal? FirstTakeProfitFraction { get; set; }
    public decimal? FirstTakeProfitQuantity { get; set; }
    public DateTime? FirstTargetFilledAt { get; set; }
    public decimal? InitialStopLoss { get; set; }
    public int TrailPivotBars { get; set; }
    public int TrailUpdateCount { get; set; }

    /// <summary>Nhóm lệnh: lệnh ngắn hay lệnh swing 4h.</summary>
    public TradeStyle Style { get; set; } = TradeStyle.Intraday;

    public decimal Quantity { get; set; }
    public decimal? Leverage { get; set; } = 20m;
    public decimal Fee { get; set; }
    public decimal? RealizedPnl { get; set; }

    public decimal? RiskAmount { get; set; }
    public decimal? RiskPercent { get; set; }
    public decimal? PlannedRiskReward { get; set; }
    public decimal? RMultiple { get; set; }
    public TradeOutcome? Outcome { get; set; }

    public EmotionState EmotionBefore { get; set; }
    public EmotionState EmotionAfter { get; set; }

    /// <summary>Thời điểm bản ghi lệnh được tạo — sớm hơn OpenedAt đúng bằng thời gian chờ khớp.</summary>
    public DateTime CreatedDate { get; set; }

    public DateTime? OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    public string? Note { get; set; }
    public string? ImageUrl { get; set; }
    public string? ExternalId { get; set; }

    // --- Live trading ---
    public bool IsLive { get; set; }
    public LiveOrderStatus LiveStatus { get; set; }
    public string? LiveNote { get; set; }
}
