namespace MMW.Domain.Enums;

public enum NotificationType
{
    EconomicCalendarHighImpact = 100,
    CentralBankEvent = 110,
    WarConflictEscalation = 200,
    SanctionAlert = 210,
    MarketSignalCreated = 300,
    TradeRiskWarning = 400,
    TradeAdvisorWarning = 410,
    SystemHealth = 900,
}

public enum NotificationSeverity
{
    Info = 1,
    Warning = 2,
    Critical = 3,
}

public enum NotificationChannel
{
    InApp = 1,
    Email = 2,
}

public enum NotificationDeliveryStatus
{
    Pending = 1,
    Sent = 2,
    Failed = 3,
    Skipped = 4,
}
