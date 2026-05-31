using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMW.Domain.Entities;

namespace MMW.Domain.DbContext.Configurations;

public class TradingDayConfiguration : IEntityTypeConfiguration<TradingDay>
{
    public void Configure(EntityTypeBuilder<TradingDay> builder)
    {
        builder.ToTable("TradingDays");
        builder.HasKey(x => x.Id);

        // Mỗi tài khoản chỉ có 1 bản ghi cho mỗi ngày.
        builder.HasIndex(x => new { x.TradingAccountId, x.Date }).IsUnique();

        // TradingDay -> Flag: NoAction (Flag đã cascade theo Account).
        builder.HasMany(x => x.Flags)
            .WithOne(x => x.TradingDay)
            .HasForeignKey(x => x.TradingDayId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
