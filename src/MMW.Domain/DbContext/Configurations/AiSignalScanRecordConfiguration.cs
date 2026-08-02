using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMW.Domain.Entities;

namespace MMW.Domain.DbContext.Configurations;

public class AiSignalScanRecordConfiguration : IEntityTypeConfiguration<AiSignalScanRecord>
{
    public void Configure(EntityTypeBuilder<AiSignalScanRecord> builder)
    {
        builder.ToTable("AiSignalScanRecords");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.Symbol, x.Interval, x.ScannedAt });
        builder.Property(x => x.SystemPrompt).HasColumnType("nvarchar(max)");
        builder.Property(x => x.RequestJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.ResponseJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.RepairResponseJson).HasColumnType("nvarchar(max)");
    }
}
