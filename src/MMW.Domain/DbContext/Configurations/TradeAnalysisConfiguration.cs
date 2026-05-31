using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMW.Domain.Entities;

namespace MMW.Domain.DbContext.Configurations;

public class TradeAnalysisConfiguration : IEntityTypeConfiguration<TradeAnalysis>
{
    public void Configure(EntityTypeBuilder<TradeAnalysis> builder)
    {
        builder.ToTable("TradeAnalyses");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.TradeId).IsUnique();

        builder.HasOne(x => x.Trade)
            .WithOne()
            .HasForeignKey<TradeAnalysis>(x => x.TradeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
