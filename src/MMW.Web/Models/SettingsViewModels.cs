using System.ComponentModel.DataAnnotations;
using MMW.Domain.Entities;

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
