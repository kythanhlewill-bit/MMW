using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMW.Domain.Entities;

namespace MMW.Domain.DbContext.Configurations;

public class WatchItemConfiguration : IEntityTypeConfiguration<WatchItem>
{
    public void Configure(EntityTypeBuilder<WatchItem> builder)
    {
        builder.ToTable("WatchItems");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.Symbol, x.Interval }).IsUnique();
    }
}
