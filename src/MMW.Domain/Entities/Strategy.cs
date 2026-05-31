using System.ComponentModel.DataAnnotations;

namespace MMW.Domain.Entities;

/// <summary>
/// Catalog setup/chiến lược để gắn vào lệnh → sau này phân tích winrate theo từng setup.
/// </summary>
public class Strategy : BaseEntity
{
    public long TradingAccountId { get; set; }
    public TradingAccount TradingAccount { get; set; } = null!;

    [Required, MaxLength(100)]
    public string Name { get; set; } = null!;

    [MaxLength(500)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<Trade> Trades { get; set; } = new List<Trade>();
}
