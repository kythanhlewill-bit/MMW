using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMW.Domain.Entities;

namespace MMW.Domain.DbContext.Configurations;

public class FlagConfiguration : IEntityTypeConfiguration<Flag>
{
    public void Configure(EntityTypeBuilder<Flag> builder)
    {
        builder.ToTable("Flags");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.TradingAccountId, x.Category, x.IsAcknowledged });
        builder.HasIndex(x => x.TradeId);
        builder.HasIndex(x => x.TradingDayId);
    }
}
