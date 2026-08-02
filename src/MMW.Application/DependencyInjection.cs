using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using MMW.Application.Behavior;
using MMW.Application.Behavior.Detectors;
using MMW.Application.Indicators;
using MMW.Application.Interfaces;
using MMW.Application.MarketData;
using MMW.Application.RuleEngine;
using MMW.Application.RuleEngine.Rules;
using MMW.Application.Services;
using MMW.Domain.Entities;

namespace MMW.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Đăng ký AutoMapper, Rule Engine và các service nghiệp vụ của tầng Application.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddAutoMapper(typeof(DependencyInjection).Assembly);

        // Rule Engine — mỗi rule là 1 ITradeRule; thêm rule mới chỉ cần thêm 1 dòng.
        services.AddScoped<ITradeRule, RequireStopLossRule>();
        services.AddScoped<ITradeRule, MaxRiskPerTradeRule>();
        services.AddScoped<ITradeRule, MinRiskRewardRule>();
        services.AddScoped<ITradeRule, MaxTradesPerDayRule>();
        services.AddScoped<ITradeRule, DailyLossLimitRule>();

        services.AddScoped<ITradeMetricsCalculator, TradeMetricsCalculator>();
        services.AddScoped<IRuleEngine, TradeRuleEngine>();

        // Behavior detection — mỗi hành vi là 1 IBehaviorDetector.
        services.AddScoped<IBehaviorDetector, RevengeTradeDetector>();
        services.AddScoped<IBehaviorDetector, LossStreakDetector>();
        services.AddScoped<IBehaviorDetector, OversizedAfterLossDetector>();
        services.AddScoped<IBehaviorAnalyzer, BehaviorAnalyzer>();

        // Auth
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<IAuthService, AuthService>();

        // Settings (config động)
        services.AddScoped<ISettingsService, SettingsService>();

        // Số dư thật từ sàn để tính rủi ro (cache ngắn, fallback CurrentBalance)
        services.AddScoped<ILiveBalanceService, LiveBalanceService>();

        // Services
        services.AddScoped<ITradeService, TradeService>();
        services.AddScoped<ITradePreflightAnalysisService, TradePreflightAnalysisService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<INotificationPreferenceService, NotificationPreferenceService>();
        services.AddScoped<INotificationEmailJob, NotificationEmailJob>();
        services.AddScoped<IRealtimeNotificationSender, NoopRealtimeNotificationSender>();
        services.AddScoped<INotificationEmailQueue, NoopNotificationEmailQueue>();
        services.AddScoped<IMacroEventProvider, NoopMacroEventProvider>();
        services.AddScoped<IMacroEventService, MacroEventService>();
        services.AddScoped<IRuleEvaluationService, RuleEvaluationService>();
        services.AddScoped<IBehaviorAnalysisService, BehaviorAnalysisService>();
        services.AddScoped<ITradingDayService, TradingDayService>();
        services.AddScoped<ITradeWorkflowService, TradeWorkflowService>();

        // Cấu trúc thị trường tất định — thuần, không trạng thái, không I/O (R-007).
        services.AddSingleton<Trading.Structure.ISwingDetector, Trading.Structure.SwingDetector>();
        services.AddSingleton<Trading.Structure.MarketStructureAnalyzer>();

        // Market data & indicators
        services.AddSingleton<IIndicatorService, IndicatorService>();
        services.AddScoped<IMarketAnalyzer, MarketAnalyzer>();
        services.AddScoped<ISignalGenerator, SignalGenerator>();
        services.AddScoped<IMarketImportService, MarketImportService>();
        services.AddScoped<IMarketScanService, MarketScanService>();

        // Trade result sync (auto-fetch PnL từ sàn)
        services.AddScoped<ITradeResultSyncService, TradeResultSyncService>();

        // Đặt lệnh thật (live trading) — chỉ chạy khi LiveTrading.Enabled=true
        services.AddScoped<ILiveOrderService, LiveOrderService>();

        // Trade advisor (phân tích lệnh mở + lời khuyên)
        services.AddScoped<ITradeAdvisorService, TradeAdvisorService>();

        return services;
    }
}
