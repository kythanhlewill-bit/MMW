using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMW.Domain.Entities;

namespace MMW.Domain.DbContext.Configurations;

public class MarketSnapshotConfiguration : IEntityTypeConfiguration<MarketSnapshot>
{
    public void Configure(EntityTypeBuilder<MarketSnapshot> builder)
    {
        builder.ToTable("MarketSnapshots");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.Symbol, x.Interval }).IsUnique();
    }
}
