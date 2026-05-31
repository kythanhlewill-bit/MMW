using System.ComponentModel.DataAnnotations;
using MMW.Domain.Enums;

namespace MMW.Web.Models;

public class AccountFormModel
{
    public long Id { get; set; }

    [Required, MaxLength(100), Display(Name = "Tên tài khoản")]
    public string Name { get; set; } = "";

    [Display(Name = "Sàn")]
    public Broker Broker { get; set; } = Broker.Binance;

    [MaxLength(10), Display(Name = "Tiền tệ")]
    public string Currency { get; set; } = "USDT";

    [Display(Name = "Số dư ban đầu")]
    public decimal InitialBalance { get; set; }

    [Display(Name = "Số dư hiện tại")]
    public decimal CurrentBalance { get; set; }

    [Display(Name = "Hoạt động")]
    public bool IsActive { get; set; } = true;

    [MaxLength(200), Display(Name = "API Key (read-only)")]
    public string? ApiKey { get; set; }

    [MaxLength(200), Display(Name = "API Secret")]
    public string? ApiSecret { get; set; }
}
