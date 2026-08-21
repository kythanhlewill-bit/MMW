using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMW.Domain.Entities;

namespace MMW.Domain.DbContext.Configurations;

// Cấu hình EF cho 11 thực thể của Deterministic Intraday Trading Engine.
//
// Gom vào một tệp có chủ ý: các khoá duy nhất ở đây là cơ chế chống trùng của FR-005,
// FR-024 và FR-051, và chúng chỉ đọc được như một bộ khi nằm cạnh nhau. Tách 11 tệp
// khiến việc kiểm tra "đã đủ ràng buộc chống trùng chưa" phải mở 11 chỗ.

public class EngineSettingConfiguration : IEntityTypeConfiguration<EngineSetting>
{
    public void Configure(EntityTypeBuilder<EngineSetting> b)
    {
        b.ToTable("EngineSettings");
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.TradingAccountId).IsUnique();   // 1:1 với TradingAccount
        b.Property(x => x.EntryTimeframe).HasMaxLength(8);
        b.Property(x => x.BiasTimeframe).HasMaxLength(8);

        b.HasOne(x => x.TradingAccount)
            .WithOne()
            .HasForeignKey<EngineSetting>(x => x.TradingAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class SessionQualityRowConfiguration : IEntityTypeConfiguration<SessionQualityRow>
{
    public void Configure(EntityTypeBuilder<SessionQualityRow> b)
    {
        b.ToTable("SessionQualityRows");
        b.HasKey(x => x.Id);
        b.Property(x => x.Label).HasMaxLength(40);
        b.HasIndex(x => new { x.EngineSettingId, x.FromHourUtc }).IsUnique();

        b.HasOne(x => x.EngineSetting)
            .WithMany(x => x.SessionQualityRows)
            .HasForeignKey(x => x.EngineSettingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class BlackoutRuleConfiguration : IEntityTypeConfiguration<BlackoutRule>
{
    public void Configure(EntityTypeBuilder<BlackoutRule> b)
    {
        b.ToTable("BlackoutRules");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.EngineSettingId, x.EventKind }).IsUnique();

        b.HasOne(x => x.EngineSetting)
            .WithMany(x => x.BlackoutRules)
            .HasForeignKey(x => x.EngineSettingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ScheduledEventConfiguration : IEntityTypeConfiguration<ScheduledEvent>
{
    public void Configure(EntityTypeBuilder<ScheduledEvent> b)
    {
        b.ToTable("ScheduledEvents");
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(200);
        b.Property(x => x.Currency).HasMaxLength(8);
        b.Property(x => x.SourceKey).HasMaxLength(120);
        b.Property(x => x.Notes).HasMaxLength(500);

        b.HasIndex(x => x.OccursAtUtc);

        // Duy nhất khi khác null — chống nạp trùng mà vẫn cho phép sự kiện không có khoá nguồn.
        b.HasIndex(x => x.SourceKey)
            .IsUnique()
            .HasFilter("[SourceKey] IS NOT NULL");
    }
}

public class DailyPlanConfiguration : IEntityTypeConfiguration<DailyPlan>
{
    public void Configure(EntityTypeBuilder<DailyPlan> b)
    {
        b.ToTable("DailyPlans");
        b.HasKey(x => x.Id);
        b.Property(x => x.BtcStructure).HasMaxLength(20);
        b.Property(x => x.MissingInputs).HasMaxLength(500);
        b.Property(x => x.AiDayRiskLevel).HasMaxLength(20);
        b.Property(x => x.AiNarrative).HasMaxLength(500);

        // Một kế hoạch duy nhất cho mỗi tài khoản mỗi ngày (FR-024). Đây là cơ chế cưỡng chế
        // tính bất biến: job chạy lại trong cùng ngày sẽ va khoá này thay vì ghi đè âm thầm.
        b.HasIndex(x => new { x.TradingAccountId, x.PlanDateUtc }).IsUnique();

        b.HasOne(x => x.TradingAccount)
            .WithMany()
            .HasForeignKey(x => x.TradingAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class EntryScorecardConfiguration : IEntityTypeConfiguration<EntryScorecard>
{
    public void Configure(EntityTypeBuilder<EntryScorecard> b)
    {
        b.ToTable("EntryScorecards");
        b.HasKey(x => x.Id);
        b.Property(x => x.Symbol).HasMaxLength(30);
        b.Property(x => x.Interval).HasMaxLength(8);
        // Hai cột này là LỜI GIẢI THÍCH, và độ dài của chúng do nội dung quyết định chứ không do
        // ta chọn: mỗi nhánh setup mới lại nối thêm lý do của nó vào chuỗi. Trần 300 ký tự từng
        // làm 27 lượt chấm điểm ném lỗi cắt chuỗi và MẤT HẲN phiếu — đánh đổi tệ nhất có thể, vì
        // đúng những phiếu có nhiều thứ để giải thích mới là những phiếu đáng giữ nhất.
        b.Property(x => x.VetoDetail);
        b.Property(x => x.TriggerDetail);
        b.Property(x => x.SetupEventId).HasMaxLength(120);

        // Chống sinh phiếu trùng cho cùng một cây nến (FR-051).
        b.HasIndex(x => new { x.Symbol, x.CandleCloseTimeUtc, x.IsBacktest }).IsUnique();
        b.HasIndex(x => x.BacktestRunId);

        b.HasOne(x => x.DailyPlan)
            .WithMany(x => x.Scorecards)
            .HasForeignKey(x => x.DailyPlanId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class EntryScorecardLineConfiguration : IEntityTypeConfiguration<EntryScorecardLine>
{
    public void Configure(EntityTypeBuilder<EntryScorecardLine> b)
    {
        b.ToTable("EntryScorecardLines");
        b.HasKey(x => x.Id);
        b.Property(x => x.CriterionKey).HasMaxLength(60);
        b.Property(x => x.Reason).HasMaxLength(300);
        b.Property(x => x.StateCode).HasMaxLength(40);

        b.HasIndex(x => new { x.EntryScorecardId, x.CriterionKey }).IsUnique();

        // Truy vấn "tiêu chí nào hay về 0 điểm nhất" — lý do tồn tại của bảng này.
        b.HasIndex(x => x.CriterionKey);

        b.HasOne(x => x.EntryScorecard)
            .WithMany(x => x.Lines)
            .HasForeignKey(x => x.EntryScorecardId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class MarketContextRecordConfiguration : IEntityTypeConfiguration<MarketContextRecord>
{
    public void Configure(EntityTypeBuilder<MarketContextRecord> b)
    {
        b.ToTable("MarketContextRecords");
        b.HasKey(x => x.Id);
        b.Property(x => x.Severity).HasMaxLength(20);
        b.Property(x => x.AffectedSymbols).HasMaxLength(200);
        b.Property(x => x.Narrative).HasMaxLength(500);
        b.Property(x => x.SourceKey).HasMaxLength(120);
        b.Property(x => x.RejectedFields).HasMaxLength(300);

        b.HasIndex(x => x.ExpiresAtUtc);
        b.HasIndex(x => x.SourceKey)
            .IsUnique()
            .HasFilter("[SourceKey] IS NOT NULL");
    }
}

public class KlineArchiveConfiguration : IEntityTypeConfiguration<KlineArchive>
{
    public void Configure(EntityTypeBuilder<KlineArchive> b)
    {
        b.ToTable("KlineArchives");
        b.HasKey(x => x.Id);
        b.Property(x => x.Symbol).HasMaxLength(30);
        b.Property(x => x.Interval).HasMaxLength(8);

        // Nạp lại cùng một khoảng KHÔNG được sinh bản ghi trùng (FR-005).
        b.HasIndex(x => new { x.Symbol, x.Interval, x.OpenTimeUtc }).IsUnique();
    }
}

public class FundingRateArchiveConfiguration : IEntityTypeConfiguration<FundingRateArchive>
{
    public void Configure(EntityTypeBuilder<FundingRateArchive> b)
    {
        b.ToTable("FundingRateArchives");
        b.HasKey(x => x.Id);
        b.Property(x => x.Symbol).HasMaxLength(30);
        b.HasIndex(x => new { x.Symbol, x.FundingTimeUtc }).IsUnique();
    }
}

public class BacktestRunConfiguration : IEntityTypeConfiguration<BacktestRun>
{
    public void Configure(EntityTypeBuilder<BacktestRun> b)
    {
        b.ToTable("BacktestRuns");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.Symbols).HasMaxLength(200);
        b.Property(x => x.Status).HasMaxLength(20);
        b.Property(x => x.TelemetrySchemaVersion).HasMaxLength(20);
        b.Property(x => x.DecisionFingerprint).HasMaxLength(64);
        b.Property(x => x.TradeFingerprint).HasMaxLength(64);
        b.Property(x => x.DiagnosticsJson).HasColumnType("nvarchar(max)");
        b.Property(x => x.Limitations).HasColumnType("nvarchar(max)");
    }
}
