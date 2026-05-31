using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMW.Domain.Entities;

namespace MMW.Domain.DbContext.Configurations;

public class TradeTagConfiguration : IEntityTypeConfiguration<TradeTag>
{
    public void Configure(EntityTypeBuilder<TradeTag> builder)
    {
        builder.ToTable("TradeTags");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.TradeId, x.Kind });
    }
}
