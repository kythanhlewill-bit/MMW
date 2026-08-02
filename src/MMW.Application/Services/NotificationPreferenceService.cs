using MMW.Application.Interfaces;
using MMW.Application.Models;
using MMW.Domain.Constants;
using MMW.Domain.Entities;
using MMW.Shared.Interfaces;

namespace MMW.Application.Services;

public class NotificationPreferenceService : INotificationPreferenceService
{
    private readonly IBaseRepository<User> _users;
    private readonly IBaseRepository<NotificationPreference> _preferences;
    private readonly IUnitOfWork _unitOfWork;

    public NotificationPreferenceService(
        IBaseRepository<User> users,
        IBaseRepository<NotificationPreference> preferences,
        IUnitOfWork unitOfWork)
    {
        _users = users;
        _preferences = preferences;
        _unitOfWork = unitOfWork;
    }

    public async Task<NotificationSettingsModel> GetSettingsAsync(long userId, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindAsync(userId)
            ?? throw new InvalidOperationException($"Không tìm thấy user #{userId}.");

        var existing = await _preferences.FindListAsync(p => p.UserId == userId);
        var map = existing.ToDictionary(p => p.Type);

        var models = NotificationTypeConstant.All.Select(def =>
        {
            map.TryGetValue(def.Type, out var pref);
            return new NotificationPreferenceModel
            {
                Type = def.Type,
                Key = def.Key,
                Name = def.Name,
                Description = def.Description,
                InAppEnabled = pref?.InAppEnabled ?? def.DefaultInAppEnabled,
                EmailEnabled = pref?.EmailEnabled ?? def.DefaultEmailEnabled,
                MinSeverity = pref?.MinSeverity ?? def.DefaultMinSeverity,
            };
        }).ToList();

        return new NotificationSettingsModel
        {
            UserId = user.Id,
            Username = user.Username,
            Email = user.Email,
            Preferences = models,
        };
    }

    public async Task UpdateSettingsAsync(long userId, string? email, IReadOnlyList<NotificationPreferenceUpdateModel> preferences, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindAsync(userId)
            ?? throw new InvalidOperationException($"Không tìm thấy user #{userId}.");

        user.Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        user.EmailConfirmed = !string.IsNullOrWhiteSpace(user.Email);
        _users.Update(user);

        foreach (var update in preferences)
        {
            var existing = (await _preferences.FindListAsync(p => p.UserId == userId && p.Type == update.Type)).FirstOrDefault();
            var pref = existing is null
                ? new NotificationPreference { UserId = userId, Type = update.Type }
                : await _preferences.FindAsync(existing.Id) ?? new NotificationPreference { UserId = userId, Type = update.Type };

            pref.InAppEnabled = update.InAppEnabled;
            pref.EmailEnabled = update.EmailEnabled;
            pref.MinSeverity = update.MinSeverity;

            if (pref.Id == 0)
                await _preferences.AddAsync(pref);
            else
                _preferences.Update(pref);
        }

        await _unitOfWork.CommitAsync(cancellationToken);
    }
}
