using Microsoft.AspNetCore.Identity;
using MMW.Application.Interfaces;
using MMW.Domain.Entities;
using MMW.Shared.Interfaces;

namespace MMW.Application.Services;

public class AuthService : IAuthService
{
    private readonly IBaseRepository<User> _users;
    private readonly IPasswordHasher<User> _passwordHasher;

    public AuthService(IBaseRepository<User> users, IPasswordHasher<User> passwordHasher)
    {
        _users = users;
        _passwordHasher = passwordHasher;
    }

    public async Task<User?> ValidateCredentialsAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var user = await _users.FirstOrDefaultAsync(u => u.Username == username && u.IsActive);
        if (user is null)
            return null;

        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        return result == PasswordVerificationResult.Failed ? null : user;
    }

    public string HashPassword(string password) =>
        _passwordHasher.HashPassword(new User(), password);
}
