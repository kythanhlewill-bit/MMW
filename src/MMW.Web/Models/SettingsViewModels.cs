using System.ComponentModel.DataAnnotations;
using MMW.Domain.Entities;
using MMW.Domain.Enums;

namespace MMW.Web.Models;

public class SettingsGeneralForm
{
    [Display(Name = "Tài khoản mặc định")]
    public long? DefaultTradingAccountId { get; set; }

    [Display(Name = "Xác nhận trước khi tạo lệnh")]
    public bool ConfirmBeforeCreateTrade { get; set; } = true;

    [Display(Name = "Điểm tối thiểu sinh đề xuất")]
    [Range(1, 10)]
    public int MinSignalScore { get; set; } = 2;

    [Display(Name = "Cho phép đặt lệnh dù vi phạm rule rủi ro")]
    public bool AllowOverrideRisk { get; set; }

    [Display(Name = "Bật deterministic engine")]
    public bool DeterministicEngineEnabled { get; set; }

    [Display(Name = "Bật so sánh AI shadow")]
    public bool ShadowComparisonEnabled { get; set; } = true;
}

public class SettingsViewModel
{
    public SettingsGeneralForm General { get; set; } = new();
    public IReadOnlyList<TradingAccount> Accounts { get; set; } = new List<TradingAccount>();
}

public class RiskSettingForm
{
    public long AccountId { get; set; }
    public string AccountName { get; set; } = "";

    [Display(Name = "% rủi ro tối đa / lệnh")]
    public decimal MaxRiskPerTradePercent { get; set; }

    [Display(Name = "R:R tối thiểu")]
    public decimal MinRiskRewardRatio { get; set; }

    [Display(Name = "Số lệnh tối đa / ngày")]
    public int MaxTradesPerDay { get; set; }

    [Display(Name = "% lỗ tối đa / ngày")]
    public decimal MaxDailyLossPercent { get; set; }

    [Display(Name = "Bắt buộc Stop Loss")]
    public bool RequireStopLoss { get; set; }

    [Display(Name = "Cửa sổ revenge (phút)")]
    public int RevengeTradeWindowMinutes { get; set; }

    [Display(Name = "Ngưỡng chuỗi thua")]
    public int LossStreakThreshold { get; set; }

    [Display(Name = "% tăng size coi là tilt")]
    public decimal TiltSizeIncreasePercent { get; set; }
}

public class NotificationSettingsForm
{
    [Display(Name = "Email nhận thông báo")]
    [EmailAddress]
    public string? Email { get; set; }

    public List<NotificationPreferenceForm> Preferences { get; set; } = new();
}

public class NotificationPreferenceForm
{
    public NotificationType Type { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool InAppEnabled { get; set; }
    public bool EmailEnabled { get; set; }
    public NotificationSeverity MinSeverity { get; set; }
}
