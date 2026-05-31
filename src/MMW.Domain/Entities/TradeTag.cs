using System.ComponentModel.DataAnnotations;
using MMW.Domain.Enums;

namespace MMW.Domain.Entities;

/// <summary>
/// Nhãn gắn vào lệnh để review (lỗi, điều kiện thị trường...). VD: "vào sớm", "dời SL", "không theo plan".
/// </summary>
public class TradeTag : BaseEntity
{
    public long TradeId { get; set; }
    public Trade Trade { get; set; } = null!;

    [Required, MaxLength(50)]
    public string Name { get; set; } = null!;

    public TagKind Kind { get; set; } = TagKind.Mistake;
}
