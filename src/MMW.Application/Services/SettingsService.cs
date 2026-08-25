using MMW.Application.Interfaces;
using MMW.Domain.Entities;
using MMW.Shared.Interfaces;

namespace MMW.Application.Services;

public class SettingsService : ISettingsService
{
    private readonly IBaseRepository<AppSetting> _appSettings;
    private readonly IBaseRepository<RiskSetting> _riskSettings;
    private readonly IUnitOfWork _unitOfWork;

    public SettingsService(
        IBaseRepository<AppSetting> appSettings,
        IBaseRepository<RiskSetting> riskSettings,
        IUnitOfWork unitOfWork)
    {
        _appSettings = appSettings;
        _riskSettings = riskSettings;
        _unitOfWork = unitOfWork;
    }

    public async Task<AppSetting> GetAppSettingAsync(CancellationToken cancellationToken = default)
    {
        var existing = (await _appSettings.GetAllAsync()).FirstOrDefault();
        if (existing is not null)
            return existing;

        var created = new AppSetting();
        await _appSettings.AddAsync(created);
        await _unitOfWork.CommitAsync(cancellationToken);
        return created;
    }

    public async Task UpdateAppSettingAsync(
        long? defaultAccountId,
        bool confirmBeforeCreateTrade,
        int minSignalScore,
        bool allowOverrideRisk,
        bool deterministicEngineEnabled,
        bool shadowComparisonEnabled,
        CancellationToken cancellationToken = default)
    {
        var current = (await _appSettings.GetAllAsync()).FirstOrDefault();
        var tracked = current is null
            ? new AppSetting()
            : await _appSettings.FindAsync(current.Id) ?? new AppSetting();

        tracked.DefaultTradingAccountId = defaultAccountId;
        tracked.ConfirmBeforeCreateTrade = confirmBeforeCreateTrade;
        // Đường AI đã là shadow-only từ Phase 9. Không cho cấu hình cũ mở lại quyền tạo lệnh.
        tracked.AutoCreateTradeFromSignal = false;
        tracked.MinSignalScore = Math.Max(0, minSignalScore);
        tracked.AllowOverrideRisk = allowOverrideRisk;
        tracked.DeterministicEngineEnabled = deterministicEngineEnabled;
        tracked.ShadowComparisonEnabled = shadowComparisonEnabled;

        if (tracked.Id == 0)
            await _appSettings.AddAsync(tracked);
        else
            _appSettings.Update(tracked);

        await _unitOfWork.CommitAsync(cancellationToken);
    }

    public async Task<RiskSetting> GetRiskSettingAsync(long accountId, CancellationToken cancellationToken = default)
    {
        var existing = (await _riskSettings.FindListAsync(r => r.TradingAccountId == accountId)).FirstOrDefault();
        return existing ?? new RiskSetting { TradingAccountId = accountId };
    }

    public async Task UpsertRiskSettingAsync(long accountId, RiskSetting values, CancellationToken cancellationToken = default)
    {
        var existing = (await _riskSettings.FindListAsync(r => r.TradingAccountId == accountId)).FirstOrDefault();
        var tracked = existing is null
            ? new RiskSetting { TradingAccountId = accountId }
            : await _riskSettings.FindAsync(existing.Id) ?? new RiskSetting { TradingAccountId = accountId };

        tracked.MaxRiskPerTradePercent = values.MaxRiskPerTradePercent;
        tracked.MinRiskRewardRatio = values.MinRiskRewardRatio;
        tracked.MaxTradesPerDay = values.MaxTradesPerDay;
        tracked.MaxDailyLossPercent = values.MaxDailyLossPercent;
        tracked.MaxTradesPerDayHtf = values.MaxTradesPerDayHtf;
        tracked.MaxDailyLossPercentHtf = values.MaxDailyLossPercentHtf;
        tracked.MaxRiskPerTradePercentHtf = values.MaxRiskPerTradePercentHtf;
        tracked.LossStreakThresholdHtf = values.LossStreakThresholdHtf;
        tracked.RequireStopLoss = values.RequireStopLoss;
        tracked.RevengeTradeWindowMinutes = values.RevengeTradeWindowMinutes;
        tracked.LossStreakThreshold = values.LossStreakThreshold;
        tracked.TiltSizeIncreasePercent = values.TiltSizeIncreasePercent;

        if (tracked.Id == 0)
            await _riskSettings.AddAsync(tracked);
        else
            _riskSettings.Update(tracked);

        await _unitOfWork.CommitAsync(cancellationToken);
    }
}
