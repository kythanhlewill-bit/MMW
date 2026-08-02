using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMW.Domain.Entities;

namespace MMW.Domain.DbContext.Configurations;

public class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.HasIndex(d => new { d.NotificationId, d.Channel });
        builder.HasIndex(d => d.Status);

        builder.Property(d => d.LastError).HasMaxLength(2000);

        builder.HasOne(d => d.Notification)
            .WithMany(n => n.Deliveries)
            .HasForeignKey(d => d.NotificationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
