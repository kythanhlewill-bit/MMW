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
        // AutoMapper 15 giải ILoggerFactory khi dựng MapperConfiguration. Web host đã có sẵn
        // (Serilog) nên TryAdd không đè; đăng ký ở đây để tầng Application vẫn tự chạy được
        // trong test dựng ServiceCollection trần.
        services.TryAddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.TryAddSingleton<ILlmService, NoopLlmService>();
        // AutoMapper 15 bỏ overload nhận thẳng Assembly — nay phải khai qua cấu hình.
        services.AddAutoMapper(cfg => cfg.AddMaps(typeof(DependencyInjection).Assembly));

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
        services.AddSingleton<Trading.Structure.ISidewaysPatternAnalyzer, Trading.Structure.SidewaysPatternAnalyzer>();
        services.AddSingleton<Trading.Structure.IStructuralLevelPlanner, Trading.Structure.StructuralLevelPlanner>();
        services.AddSingleton<Trading.Scoring.PriceActionAnalyzer>();
        services.AddSingleton<Trading.Scoring.IDirectionPolicy, Trading.Scoring.DirectionPolicy>();

        // Tầng 2 — chặn theo khung giờ. Tất định 100%, không AI, không mạng.
        services.AddSingleton<Trading.TimeGuard.IDerivedEventGenerator, Trading.TimeGuard.DerivedEventGenerator>();
        services.AddScoped<Trading.TimeGuard.IScheduledEventCalendar, Trading.TimeGuard.ScheduledEventCalendar>();
        services.AddScoped<Trading.TimeGuard.ISessionQualityProvider, Trading.TimeGuard.SessionQualityProvider>();
        services.AddScoped<Trading.TimeGuard.ITimeGuardService, Trading.TimeGuard.TimeGuardService>();
        services.AddScoped<ICalendarFreshnessMonitor, CalendarFreshnessMonitor>();
        services.AddScoped<IPositionManageService, PositionManageService>();

        // Tầng 1 — kế hoạch ngày. Bộ phân loại thuần nên là singleton.
        services.AddSingleton<Trading.DailyPlanning.IDayRegimeClassifier, Trading.DailyPlanning.DayRegimeClassifier>();
        services.AddScoped<Trading.DailyPlanning.IDailyPlanService, Trading.DailyPlanning.DailyPlanService>();

        // Tầng 3 — chấm điểm. Mỗi tiêu chí là một lớp thuần, thêm tiêu chí = thêm một dòng
        // ở đây và KHÔNG sửa EntryScorer (Nguyên tắc V).
        services.AddSingleton<Trading.Scoring.IScoreCriterion, Trading.Scoring.Criteria.HtfAlignmentCriterion>();
        services.AddSingleton<Trading.Scoring.IScoreCriterion, Trading.Scoring.Criteria.MarketStructureCriterion>();
        services.AddSingleton<Trading.Scoring.IScoreCriterion, Trading.Scoring.Criteria.EntryLocationCriterion>();
        services.AddSingleton<Trading.Scoring.IScoreCriterion, Trading.Scoring.Criteria.MomentumCriterion>();
        services.AddSingleton<Trading.Scoring.IScoreCriterion, Trading.Scoring.Criteria.VolumeConfirmationCriterion>();
        services.AddSingleton<Trading.Scoring.IScoreCriterion, Trading.Scoring.Criteria.DayRegimeMatchCriterion>();
        services.AddSingleton<Trading.Scoring.IScoreCriterion, Trading.Scoring.Criteria.VolatilityRegimeCriterion>();
        services.AddSingleton<Trading.Scoring.IScoreCriterion, Trading.Scoring.Criteria.SessionQualityCriterion>();
        services.AddSingleton<Trading.Scoring.IScoreCriterion, Trading.Scoring.Criteria.LeaderCorrelationCriterion>();
        services.AddSingleton<Trading.Scoring.IScoreCriterion, Trading.Scoring.Criteria.FundingCrowdingCriterion>();
        services.AddSingleton<Trading.Scoring.IScoreCriterion, Trading.Scoring.Criteria.OpenInterestCriterion>();
        services.AddSingleton<Trading.Scoring.IScoreCriterion, Trading.Scoring.Criteria.LiquidityZoneCriterion>();
        services.AddSingleton<Trading.Scoring.IScoreCriterion, Trading.Scoring.Criteria.SpreadDepthCriterion>();
        services.AddSingleton<Trading.Scoring.IScoreCriterion, Trading.Scoring.Criteria.StructuralRoomCriterion>();

        services.AddSingleton<Trading.Scoring.IEntryScorer, Trading.Scoring.EntryScorer>();
        services.AddSingleton<Trading.Sizing.IPositionSizer, Trading.Sizing.ScoreBasedPositionSizer>();
        services.AddSingleton<Trading.Execution.ITradeExecutionPlanner, Trading.Execution.TradeExecutionPlanner>();
        services.AddSingleton<Trading.Execution.ISetupTriggerPolicy, Trading.Execution.SetupTriggerPolicy>();
        services.AddSingleton<Trading.Execution.IStrategyAdmissionPolicy, Trading.Execution.StrategyAdmissionPolicy>();
        services.AddSingleton<Trading.Execution.IExecutionViabilityPolicy, Trading.Execution.ExecutionViabilityPolicy>();
        // Nhóm kỷ luật — mỗi rào chắn là một lớp thuần, thêm rào = thêm một dòng ở đây.
        services.AddSingleton<Trading.Discipline.IDisciplineGate, Trading.Discipline.Gates.LossStreakGate>();
        services.AddSingleton<Trading.Discipline.IDisciplineGate, Trading.Discipline.Gates.DailyLossLimitGate>();
        services.AddSingleton<Trading.Discipline.IDisciplineGate, Trading.Discipline.Gates.RevengeWindowGate>();
        services.AddSingleton<Trading.Discipline.IDisciplineGate, Trading.Discipline.Gates.OversizedGate>();
        services.AddSingleton<Trading.Discipline.IDisciplineGate, Trading.Discipline.Gates.MaxTradesGate>();
        services.AddSingleton<Trading.Discipline.IDisciplineGate, Trading.Discipline.Gates.WorstHoursGate>();
        services.AddSingleton<Trading.Discipline.IDisciplineGate, Trading.Discipline.Gates.OpenPositionGate>();
        services.AddSingleton<Trading.Discipline.IDisciplineGate, Trading.Discipline.Gates.CorrelatedExposureGate>();

        services.AddSingleton<Trading.Discipline.IDisciplineGateRunner, Trading.Discipline.DisciplineGateRunner>();
        services.AddScoped<Trading.Discipline.ITraderStatisticsProvider, Trading.Discipline.TraderStatisticsProvider>();

        services.AddScoped<ISignalEvalService, SignalEvalService>();

        // Kiểm thử lịch sử — KHÔNG đăng ký BacktestClock/ArchiveMarketDataProvider ở đây.
        // Hai cổng đó chỉ được thay trong phạm vi MỘT lần chạy, bằng một scope riêng; đăng ký
        // toàn cục sẽ khiến chạy thật đọc kho lịch sử thay vì đọc sàn.
        services.AddScoped<Backtest.IKlineArchiveReader, Backtest.KlineArchiveReader>();
        services.AddScoped<Backtest.IKlineArchiveService, Backtest.KlineArchiveService>();

        // Lớp bối cảnh AI — chỉ ghi trường Ai* và chỉ sinh hệ số trong [0, 1].
        services.AddSingleton<Ai.IDailyBriefValidator, Ai.DailyBriefValidator>();
        services.AddSingleton<Ai.INewsClassifierValidator, Ai.NewsClassifierValidator>();
        services.AddSingleton<Ai.IMarketContextApplier, Ai.MarketContextApplier>();
        services.AddScoped<Ai.IMarketContextService, Ai.MarketContextService>();
        services.AddScoped<Ai.IDailyBriefEnricher, Ai.DailyBriefEnricher>();

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
        services.AddScoped<IScorecardExecutionService, ScorecardExecutionService>();

        // Trade advisor (phân tích lệnh mở + lời khuyên)
        services.AddScoped<ITradeAdvisorService, TradeAdvisorService>();

        return services;
    }
}
