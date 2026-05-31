using System.ComponentModel.DataAnnotations;

namespace MMW.Domain.Entities;

/// <summary>
/// Một mục trong watchlist — symbol + khung thời gian mà job scan sẽ quét định kỳ.
/// </summary>
public class WatchItem : BaseEntity
{
    [Required, MaxLength(30)]
    public string Symbol { get; set; } = null!;

    [Required, MaxLength(10)]
    public string Interval { get; set; } = "1h";

    public bool IsActive { get; set; } = true;
}
