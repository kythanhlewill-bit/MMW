using Microsoft.EntityFrameworkCore;
using MMW.Domain.Entities;

namespace MMW.Domain.DbContext;

/// <summary>
/// DbContext chính của MMW — Code-First.
/// (EOffice đặt DbContext trong Domain nên MMW giữ cùng vị trí để cấu trúc tương tự.)
/// </summary>
public partial class MmwDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    public MmwDbContext(DbContextOptions<MmwDbContext> options) : base(options)
    {
    }

    public virtual DbSet<User> Users => Set<User>();
    public virtual DbSet<TradingAccount> TradingAccounts => Set<TradingAccount>();
    public virtual DbSet<RiskSetting> RiskSettings => Set<RiskSetting>();
    public virtual DbSet<Strategy> Strategies => Set<Strategy>();
    public virtual DbSet<Trade> Trades => Set<Trade>();
    public virtual DbSet<TradeTag> TradeTags => Set<TradeTag>();
    public virtual DbSet<TradingDay> TradingDays => Set<TradingDay>();
    public virtual DbSet<Flag> Flags => Set<Flag>();
    public virtual DbSet<WatchItem> WatchItems => Set<WatchItem>();
    public virtual DbSet<MarketSnapshot> MarketSnapshots => Set<MarketSnapshot>();
    public virtual DbSet<IndicatorRecord> IndicatorRecords => Set<IndicatorRecord>();
    public virtual DbSet<TradeSignal> TradeSignals => Set<TradeSignal>();
    public virtual DbSet<AiSignalScanRecord> AiSignalScanRecords => Set<AiSignalScanRecord>();
    public virtual DbSet<ExchangeApiAuditRecord> ExchangeApiAuditRecords => Set<ExchangeApiAuditRecord>();
    public virtual DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public virtual DbSet<TradeAnalysis> TradeAnalyses => Set<TradeAnalysis>();
    public virtual DbSet<Notification> Notifications => Set<Notification>();
    public virtual DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public virtual DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();

    // --- Deterministic Intraday Trading Engine ---
    public virtual DbSet<EngineSetting> EngineSettings => Set<EngineSetting>();
    public virtual DbSet<SessionQualityRow> SessionQualityRows => Set<SessionQualityRow>();
    public virtual DbSet<BlackoutRule> BlackoutRules => Set<BlackoutRule>();
    public virtual DbSet<ScheduledEvent> ScheduledEvents => Set<ScheduledEvent>();
    public virtual DbSet<DailyPlan> DailyPlans => Set<DailyPlan>();
    public virtual DbSet<EntryScorecard> EntryScorecards => Set<EntryScorecard>();
    public virtual DbSet<EntryScorecardLine> EntryScorecardLines => Set<EntryScorecardLine>();
    public virtual DbSet<ScorecardOutcomeReview> ScorecardOutcomeReviews => Set<ScorecardOutcomeReview>();
    public virtual DbSet<MarketContextRecord> MarketContextRecords => Set<MarketContextRecord>();
    public virtual DbSet<KlineArchive> KlineArchives => Set<KlineArchive>();
    public virtual DbSet<FundingRateArchive> FundingRateArchives => Set<FundingRateArchive>();
    public virtual DbSet<BacktestRun> BacktestRuns => Set<BacktestRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Áp dụng tất cả IEntityTypeConfiguration trong assembly này.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MmwDbContext).Assembly);

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
