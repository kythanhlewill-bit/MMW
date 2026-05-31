using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMW.Domain.Entities;

namespace MMW.Domain.DbContext.Configurations;

public class StrategyConfiguration : IEntityTypeConfiguration<Strategy>
{
    public void Configure(EntityTypeBuilder<Strategy> builder)
    {
        builder.ToTable("Strategies");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.TradingAccountId, x.Name });

        // Strategy -> Trade: NoAction để tránh nhiều đường cascade tới Trade.
        builder.HasMany(x => x.Trades)
            .WithOne(x => x.Strategy)
            .HasForeignKey(x => x.StrategyId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
