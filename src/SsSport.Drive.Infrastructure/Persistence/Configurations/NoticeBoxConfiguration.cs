using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SsSport.Drive.Domain.Notifications;

namespace SsSport.Drive.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="NoticeBox"/> to <c>notice_boxes</c> (one row per team member) and its held notices to
/// <c>pending_notices</c> (T-8).
/// </summary>
internal sealed class NoticeBoxConfiguration : IEntityTypeConfiguration<NoticeBox>
{
    /// <summary>Configures the tables.</summary>
    /// <param name="builder">The entity builder.</param>
    public void Configure(EntityTypeBuilder<NoticeBox> builder)
    {
        builder.ToTable("notice_boxes");
        builder.HasKey(box => box.Id);

        // Get-only properties are mapped explicitly; EF Core skips them by convention.
        builder.Property(box => box.UserId);
        builder.HasIndex(box => box.UserId).IsUnique();

        // The recurring job looks for due boxes.
        builder.HasIndex(box => box.DueAt);

        builder.HasMany(box => box.Notices)
            .WithOne()
            .HasForeignKey("NoticeBoxId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(box => box.Notices).HasField("_notices").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary>Maps <see cref="PendingNotice"/> to <c>pending_notices</c>.</summary>
internal sealed class PendingNoticeConfiguration : IEntityTypeConfiguration<PendingNotice>
{
    /// <summary>Configures the table.</summary>
    /// <param name="builder">The entity builder.</param>
    public void Configure(EntityTypeBuilder<PendingNotice> builder)
    {
        builder.ToTable("pending_notices");
        builder.HasKey(notice => notice.Id);

        builder.Property(notice => notice.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(notice => notice.PlayerId);
        builder.Property(notice => notice.Text).HasMaxLength(PendingNotice.MaxTextLength);
        builder.Property(notice => notice.CreatedAt);
    }
}
