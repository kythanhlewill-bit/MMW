using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMW.Domain.Entities;

namespace MMW.Domain.DbContext.Configurations;

public class TradingAccountConfiguration : IEntityTypeConfiguration<TradingAccount>
{
    public void Configure(EntityTypeBuilder<TradingAccount> builder)
    {
        builder.ToTable("TradingAccounts");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.Name);

        builder.HasOne(x => x.RiskSetting)
            .WithOne(x => x.TradingAccount)
            .HasForeignKey<RiskSetting>(x => x.TradingAccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Strategies)
            .WithOne(x => x.TradingAccount)
            .HasForeignKey(x => x.TradingAccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Trades)
            .WithOne(x => x.TradingAccount)
            .HasForeignKey(x => x.TradingAccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.TradingDays)
            .WithOne(x => x.TradingAccount)
            .HasForeignKey(x => x.TradingAccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Flags)
            .WithOne(x => x.TradingAccount)
            .HasForeignKey(x => x.TradingAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
