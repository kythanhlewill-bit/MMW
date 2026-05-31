using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMW.Domain.Entities;

namespace MMW.Domain.DbContext.Configurations;

public class RiskSettingConfiguration : IEntityTypeConfiguration<RiskSetting>
{
    public void Configure(EntityTypeBuilder<RiskSetting> builder)
    {
        builder.ToTable("RiskSettings");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.TradingAccountId).IsUnique();
    }
}
