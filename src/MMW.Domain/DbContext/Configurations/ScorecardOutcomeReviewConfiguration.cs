using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMW.Domain.Entities;

namespace MMW.Domain.DbContext.Configurations;

public class ScorecardOutcomeReviewConfiguration : IEntityTypeConfiguration<ScorecardOutcomeReview>
{
    public void Configure(EntityTypeBuilder<ScorecardOutcomeReview> builder)
    {
        builder.ToTable("ScorecardOutcomeReviews");
        builder.HasKey(x => x.Id);

        // Một phiếu chỉ có MỘT kết cục cho mỗi phiên bản luật. Ràng buộc này là thứ giữ cho job
        // chạy lại được vô hại: chạy hai lần không sinh ra hai bản ghi rồi nhân đôi mọi thống kê.
        builder.HasIndex(x => new { x.EntryScorecardId, x.ResolverVersion }).IsUnique();

        builder.HasIndex(x => new { x.ResolverVersion, x.Outcome, x.ResolvedAtUtc });

        builder.HasOne(x => x.EntryScorecard)
            .WithMany()
            .HasForeignKey(x => x.EntryScorecardId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
