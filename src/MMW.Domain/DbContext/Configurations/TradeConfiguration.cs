using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMW.Domain.Entities;

namespace MMW.Domain.DbContext.Configurations;

public class TradeConfiguration : IEntityTypeConfiguration<Trade>
{
    public void Configure(EntityTypeBuilder<Trade> builder)
    {
        builder.ToTable("Trades");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.TradingAccountId, x.Status });
        builder.HasIndex(x => x.Symbol);
        builder.HasIndex(x => x.OpenedAt);

        // Chống import trùng lệnh từ sàn (chỉ áp dụng khi có ExternalId).
        builder.HasIndex(x => new { x.TradingAccountId, x.ExternalId })
            .IsUnique()
            .HasFilter("[ExternalId] IS NOT NULL");

        builder.HasMany(x => x.Tags)
            .WithOne(x => x.Trade)
            .HasForeignKey(x => x.TradeId)
            .OnDelete(DeleteBehavior.Cascade);

        // Trade -> Flag: NoAction (Flag đã cascade theo Account).
        builder.HasMany(x => x.Flags)
            .WithOne(x => x.Trade)
            .HasForeignKey(x => x.TradeId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
