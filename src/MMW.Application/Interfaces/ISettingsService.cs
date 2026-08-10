using MMW.Domain.Entities;

namespace MMW.Application.Interfaces;

public interface ISettingsService
{
    /// <summary>Lấy cấu hình toàn cục (tạo mặc định nếu chưa có).</summary>
    Task<AppSetting> GetAppSettingAsync(CancellationToken cancellationToken = default);

    Task UpdateAppSettingAsync(
        long? defaultAccountId,
        bool confirmBeforeCreateTrade,
        int minSignalScore,
        bool allowOverrideRisk,
        bool deterministicEngineEnabled,
        bool shadowComparisonEnabled,
        CancellationToken cancellationToken = default);

    /// <summary>Cấu hình rủi ro của một tài khoản (bản mặc định nếu chưa có — chưa lưu).</summary>
    Task<RiskSetting> GetRiskSettingAsync(long accountId, CancellationToken cancellationToken = default);

    Task UpsertRiskSettingAsync(long accountId, RiskSetting values, CancellationToken cancellationToken = default);
}
