using MMW.Domain.Entities;

namespace MMW.Application.Interfaces;

public interface IAuthService
{
    /// <summary>Xác thực username/password. Trả về User nếu hợp lệ và đang active, ngược lại null.</summary>
    Task<User?> ValidateCredentialsAsync(string username, string password, CancellationToken cancellationToken = default);

    /// <summary>Băm mật khẩu (dùng khi seed/tạo user).</summary>
    string HashPassword(string password);
}
