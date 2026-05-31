namespace MMW.Domain.Enums;

/// <summary>Sàn/nguồn tài khoản giao dịch.</summary>
public enum Broker
{
    Binance = 1,
    Bybit = 2,
    Okx = 3,
    Demo = 98,
    Other = 99,
}

public enum TradeDirection
{
    Long = 1,
    Short = 2,
}

public enum TradeStatus
{
    /// <summary>Mới lên kế hoạch, chưa vào lệnh (dùng cho "Trade Gate").</summary>
    Planned = 1,
    Open = 2,
    Closed = 3,
    Cancelled = 4,
}

public enum TradeSource
{
    Manual = 1,
    Import = 2,
    Api = 3,
}

public enum TradeOutcome
{
    Win = 1,
    Loss = 2,
    BreakEven = 3,
}

/// <summary>Trạng thái tâm lý tự đánh giá — cốt lõi cho journaling kỷ luật.</summary>
public enum EmotionState
{
    Unknown = 0,
    Calm = 1,
    Confident = 2,
    Disciplined = 3,
    Fomo = 4,
    Revenge = 5,
    Fearful = 6,
    Greedy = 7,
    Bored = 8,
    Tilted = 9,
}

/// <summary>Nhãn gắn vào lệnh để review sau.</summary>
public enum TagKind
{
    Mistake = 1,
    Setup = 2,
    Condition = 3,
    Other = 99,
}

/// <summary>Thiên hướng thị trường suy ra từ indicator (deterministic).</summary>
public enum MarketBias
{
    Bearish = -1,
    Neutral = 0,
    Bullish = 1,
}

/// <summary>Phân loại cờ: vi phạm rule cứng hay hành vi phát hiện được.</summary>
public enum FlagCategory
{
    RuleViolation = 1,
    Behavior = 2,
}

public enum FlagSeverity
{
    Info = 1,
    Warning = 2,
    Critical = 3,
}

/// <summary>Loại cờ. 1xx = Rule Engine, 2xx = Behavior detection.</summary>
public enum FlagType
{
    // --- Rule violations (deterministic) ---
    RiskExceeded = 100,
    LowRiskReward = 101,
    NoStopLoss = 102,
    MaxTradesPerDayExceeded = 103,
    DailyLossLimitExceeded = 104,
    PositionSizeExceeded = 105,

    // --- Behaviors ---
    RevengeTrade = 200,
    Overtrade = 201,
    Tilt = 202,
    LossStreak = 203,
    OversizedAfterLoss = 204,
}
