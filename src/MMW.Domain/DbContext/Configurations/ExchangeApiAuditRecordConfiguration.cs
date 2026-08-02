using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMW.Domain.Entities;

namespace MMW.Domain.DbContext.Configurations;

public class ExchangeApiAuditRecordConfiguration : IEntityTypeConfiguration<ExchangeApiAuditRecord>
{
    public void Configure(EntityTypeBuilder<ExchangeApiAuditRecord> builder)
    {
        builder.ToTable("ExchangeApiAuditRecords");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.Exchange, x.Symbol, x.RequestedAtUtc });
        builder.HasIndex(x => x.ClientOrderId);
        builder.Property(x => x.RequestJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.ResponseJson).HasColumnType("nvarchar(max)");
    }
}
