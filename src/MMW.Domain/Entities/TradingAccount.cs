using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using MMW.Domain.Enums;

namespace MMW.Domain.Entities;

/// <summary>
/// Tài khoản giao dịch — gốc của mọi dữ liệu. Cho phép nhiều tài khoản (Binance, demo...).
/// Số dư dùng để tính % rủi ro của từng lệnh.
/// </summary>
public class TradingAccount : BaseEntity
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = null!;

    public Broker Broker { get; set; } = Broker.Binance;

    [MaxLength(10)]
    public string Currency { get; set; } = "USDT";

    [Precision(18, 8)]
    public decimal InitialBalance { get; set; }

    [Precision(18, 8)]
    public decimal CurrentBalance { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>API key read-only (dùng cho fetch trade history). Lưu bằng User Secrets hoặc encrypted.</summary>
    [MaxLength(200)]
    public string? ApiKey { get; set; }

    [MaxLength(200)]
    public string? ApiSecret { get; set; }

    // Navigation
    public RiskSetting? RiskSetting { get; set; }
    public ICollection<Strategy> Strategies { get; set; } = new List<Strategy>();
    public ICollection<Trade> Trades { get; set; } = new List<Trade>();
    public ICollection<TradingDay> TradingDays { get; set; } = new List<TradingDay>();
    public ICollection<Flag> Flags { get; set; } = new List<Flag>();
}
