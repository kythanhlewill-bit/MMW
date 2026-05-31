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
    public virtual DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public virtual DbSet<TradeAnalysis> TradeAnalyses => Set<TradeAnalysis>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Áp dụng tất cả IEntityTypeConfiguration trong assembly này.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MmwDbContext).Assembly);

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
