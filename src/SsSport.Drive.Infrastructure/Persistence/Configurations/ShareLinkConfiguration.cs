using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SsSport.Drive.Domain.Links;

namespace SsSport.Drive.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="ShareLink"/> to <c>share_links</c>; codes are unique and the players are a <c>uuid[]</c> in page order.</summary>
internal sealed class ShareLinkConfiguration : IEntityTypeConfiguration<ShareLink>
{
    /// <summary>The backing field of <see cref="ShareLink.PlayerIds"/>.</summary>
    private const string PlayerIdsField = "_playerIds";

    /// <summary>Configures the share link table.</summary>
    /// <param name="builder">The entity builder.</param>
    public void Configure(EntityTypeBuilder<ShareLink> builder)
    {
        builder.ToTable("share_links");
        builder.HasKey(link => link.Id);

        // Get-only properties are mapped explicitly; EF Core skips them by convention.
        builder.Property(link => link.Code).HasConversion(ValueConverters.ShareLinkCode).HasMaxLength(ShareLinkCode.Length);
        builder.Property(link => link.RecipientId);
        builder.Property(link => link.CreatedAt);

        builder.Property(link => link.Type).HasConversion<string>().HasMaxLength(10);
        builder.Property(link => link.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(link => link.ForwardingScope).HasConversion<string>().HasMaxLength(12);
        builder.Property(link => link.Note).HasMaxLength(ShareLink.MaxNoteLength);
        builder.Property<List<Guid>>(PlayerIdsField).HasColumnName("player_ids");
        builder.Ignore(link => link.PlayerIds);

        builder.HasIndex(link => link.Code).IsUnique();
        builder.HasIndex(link => link.RecipientId);
        builder.HasIndex(link => link.CreatedAt);
        builder.HasIndex(PlayerIdsField).HasMethod("gin");
    }
}
