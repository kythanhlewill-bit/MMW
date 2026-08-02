using System.ComponentModel.DataAnnotations;

namespace MMW.Domain.Entities;

/// <summary>
/// Người dùng đăng nhập hệ thống. Mật khẩu lưu dạng hash (PasswordHasher), không lưu plaintext.
/// </summary>
public class User : BaseEntity
{
    [Required, MaxLength(50)]
    public string Username { get; set; } = null!;

    [Required, MaxLength(500)]
    public string PasswordHash { get; set; } = null!;

    [MaxLength(100)]
    public string? DisplayName { get; set; }

    [MaxLength(200)]
    public string? Email { get; set; }

    public bool EmailConfirmed { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    public ICollection<NotificationPreference> NotificationPreferences { get; set; } = new List<NotificationPreference>();
}
