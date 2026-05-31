using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMW.Domain.Entities;

namespace MMW.Domain.DbContext.Configurations;

public class IndicatorRecordConfiguration : IEntityTypeConfiguration<IndicatorRecord>
{
    public void Configure(EntityTypeBuilder<IndicatorRecord> builder)
    {
        builder.ToTable("IndicatorRecords");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.Symbol, x.Interval, x.ScannedAt });
    }
}
