using MMW.Domain.Enums;

namespace MMW.Domain.Constants;

public sealed record NotificationTypeDefinition(
    NotificationType Type,
    string Key,
    string Name,
    string Description,
    NotificationSeverity DefaultMinSeverity,
    bool DefaultInAppEnabled,
    bool DefaultEmailEnabled);

public static class NotificationTypeConstant
{
    public const string EconomicCalendarHighImpact = "economic_calendar_high_impact";
    public const string CentralBankEvent = "central_bank_event";
    public const string WarConflictEscalation = "war_conflict_escalation";
    public const string SanctionAlert = "sanction_alert";
    public const string MarketSignalCreated = "market_signal_created";
    public const string TradeRiskWarning = "trade_risk_warning";
    public const string TradeAdvisorWarning = "trade_advisor_warning";
    public const string SystemHealth = "system_health";

    public static readonly IReadOnlyList<NotificationTypeDefinition> All =
    [
        new(NotificationType.EconomicCalendarHighImpact, EconomicCalendarHighImpact, "Lịch kinh tế quan trọng", "CPI, NFP, GDP, PMI và các công bố có impact cao.", NotificationSeverity.Warning, true, true),
        new(NotificationType.CentralBankEvent, CentralBankEvent, "Họp ngân hàng trung ương", "FOMC, ECB, BoE, BoJ và phát biểu chính sách tiền tệ.", NotificationSeverity.Warning, true, true),
        new(NotificationType.WarConflictEscalation, WarConflictEscalation, "Xung đột/chiến tranh", "Leo thang địa chính trị có thể gây biến động risk-off.", NotificationSeverity.Critical, true, true),
        new(NotificationType.SanctionAlert, SanctionAlert, "Sanction/regulatory", "Cấm vận, hạn chế tài chính hoặc sự kiện pháp lý ảnh hưởng thị trường.", NotificationSeverity.Warning, true, true),
        new(NotificationType.MarketSignalCreated, MarketSignalCreated, "Tín hiệu thị trường", "Hệ thống sinh đề xuất lệnh mới từ Market Scan.", NotificationSeverity.Info, true, false),
        new(NotificationType.TradeRiskWarning, TradeRiskWarning, "Cảnh báo rủi ro lệnh", "Rule Engine phát hiện lệnh vi phạm kỷ luật rủi ro.", NotificationSeverity.Warning, true, false),
        new(NotificationType.TradeAdvisorWarning, TradeAdvisorWarning, "Cố vấn lệnh đang mở", "AI/advisor phát hiện lệnh đang mở cần chú ý.", NotificationSeverity.Warning, true, false),
        new(NotificationType.SystemHealth, SystemHealth, "Hệ thống", "Lỗi job, lỗi API, cấu hình thiếu hoặc cảnh báo vận hành.", NotificationSeverity.Critical, true, true),
    ];

    public static NotificationTypeDefinition Get(NotificationType type) =>
        All.FirstOrDefault(x => x.Type == type)
        ?? new NotificationTypeDefinition(type, type.ToString(), type.ToString(), "", NotificationSeverity.Info, true, false);
}
