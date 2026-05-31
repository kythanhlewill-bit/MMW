using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMW.Domain.Entities;

namespace MMW.Domain.DbContext.Configurations;

public class TradeSignalConfiguration : IEntityTypeConfiguration<TradeSignal>
{
    public void Configure(EntityTypeBuilder<TradeSignal> builder)
    {
        builder.ToTable("TradeSignals");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.Symbol, x.CreatedAt });
    }
}
