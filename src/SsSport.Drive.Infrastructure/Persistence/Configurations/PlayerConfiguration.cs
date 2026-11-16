using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SsSport.Drive.Domain.Players;

namespace SsSport.Drive.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Player"/> to <c>players</c>; the Volleybox profile is stored in two columns of the same row.</summary>
internal sealed class PlayerConfiguration : IEntityTypeConfiguration<Player>
{
    /// <summary>The shadow key of the owned Volleybox profile (the owning player's id).</summary>
    private const string VolleyboxOwnerKey = "OwnerId";

    /// <summary>Configures the player table.</summary>
    /// <param name="builder">The entity builder.</param>
    public void Configure(EntityTypeBuilder<Player> builder)
    {
        builder.ToTable("players");
        builder.HasKey(player => player.Id);

        // Get-only properties are mapped explicitly; EF Core skips them by convention.
        builder.Property(player => player.CreatedAt);

        builder.Property(player => player.Category).HasConversion<string>().HasMaxLength(10);
        builder.Property(player => player.Origin).HasConversion<string>().HasMaxLength(10).HasDefaultValue(PlayerOrigin.Team).HasSentinel(PlayerOrigin.Team);
        builder.Property(player => player.Name).HasColumnName("full_name").HasConversion(ValueConverters.FullName).HasMaxLength(100);
        builder.Property(player => player.Slug).HasConversion(ValueConverters.Slug).HasMaxLength(120);
        builder.Property(player => player.Nationality).HasConversion(ValueConverters.Nationality).HasMaxLength(2);
        builder.Property(player => player.Positions).HasConversion(ValueConverters.Positions, ValueConverters.PositionsComparer).HasColumnType("text[]");
        builder.Property(player => player.Photo).HasColumnName("photo_key").HasConversion(ValueConverters.Photo).HasMaxLength(512);
        builder.Property(player => player.QrCode).HasColumnName("qr_code_key").HasConversion(ValueConverters.StorageKey).HasMaxLength(1024);
        builder.Property(player => player.PhotoCutout).HasColumnName("photo_cutout_key").HasConversion(ValueConverters.StorageKey).HasMaxLength(1024);
        builder.Property(player => player.Card).HasColumnName("card_key").HasConversion(ValueConverters.StorageKey).HasMaxLength(1024);
        builder.Property(player => player.Status).HasConversion<string>().HasMaxLength(20);

        builder.OwnsOne(player => player.Volleybox, volleybox =>
        {
            // The profile has its own PlayerId (the Volleybox id), which clashes with EF's default owner-key name, so
            // the owner key is named explicitly. It shares the players.id column: the schema stays the same.
            volleybox.WithOwner().HasForeignKey(VolleyboxOwnerKey);
            volleybox.Property<Guid>(VolleyboxOwnerKey).HasColumnName("id");
            volleybox.Property(profile => profile.PlayerId).HasColumnName("volleybox_player_id");
            volleybox.Property(profile => profile.Url).HasColumnName("volleybox_url").HasMaxLength(300);
            volleybox.HasIndex(profile => profile.PlayerId).IsUnique();
        });

        builder.HasIndex(player => player.Slug).IsUnique();
        builder.HasIndex(player => player.Status);
        builder.HasIndex(player => player.AnalystId);

        builder.Ignore(player => player.IsPublished);
        builder.Ignore(player => player.IsComplete);
    }
}
