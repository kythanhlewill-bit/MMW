using System.ComponentModel.DataAnnotations;

namespace MMW.Domain.Entities;

/// <summary>
/// Base cho mọi entity: khoá chính + cột audit cơ bản (code-first).
/// </summary>
public abstract class BaseEntity
{
    [Key]
    public long Id { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public string? CreatedUser { get; set; }
    public DateTime? UpdatedDate { get; set; }
    public string? UpdatedUser { get; set; }
}
