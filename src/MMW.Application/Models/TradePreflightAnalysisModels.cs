using MMW.Domain.Enums;

namespace MMW.Application.Models;

public class TradePreflightAnalysisRequest
{
    public long TradingAccountId { get; set; }
    public string Symbol { get; set; } = "";
    public TradeDirection Direction { get; set; }
    public OrderType OrderType { get; set; }
    public TradeStatus Status { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? TakeProfit { get; set; }
    public decimal Quantity { get; set; }
    public decimal? Leverage { get; set; }
    public decimal Fee { get; set; }
    public EmotionState EmotionBefore { get; set; }
    public string? Note { get; set; }
}

public class TradePreflightAnalysisResult
{
    public bool IsAiConfigured { get; set; }
    /// <summary>True CHỈ khi AI thật trả lời hợp lệ (không phải fallback rule nội bộ).</summary>
    public bool AiAnswered { get; set; }
    public string Decision { get; set; } = "wait";
    public int Score { get; set; }
    public decimal Confidence { get; set; }
    public string Advice { get; set; } = "";
    public List<string> Reasons { get; set; } = [];
    public List<string> RiskWarnings { get; set; } = [];
    public string Invalidation { get; set; } = "";

    /// <summary>SL/TP do AI đề xuất lại (hợp lý hơn theo ATR/cấu trúc). Null nếu AI không đổi.</summary>
    public decimal? SuggestedStopLoss { get; set; }
    public decimal? SuggestedTakeProfit { get; set; }

    public TradePreflightMetrics Metrics { get; set; } = new();
}

public class TradePreflightMetrics
{
    public decimal? CurrentPrice { get; set; }
    public decimal? AccountBalance { get; set; }
    public decimal? RiskAmount { get; set; }
    public decimal? RiskPercent { get; set; }
    public decimal? PlannedRiskReward { get; set; }
    public decimal? Rsi14 { get; set; }
    public decimal? Ema20 { get; set; }
    public decimal? Ema50 { get; set; }
    public decimal? Atr14 { get; set; }
    public decimal? MacdHistogram { get; set; }
    public MarketBias Bias { get; set; } = MarketBias.Neutral;
}
