using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SsSport.Drive.Domain.Assets;

namespace SsSport.Drive.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Asset"/> to <c>assets</c> (the source file in the same row) and its <see cref="Rendition"/>s to
/// <c>renditions</c>. Renditions dropped by the aggregate are deleted.
/// </summary>
internal sealed class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    /// <summary>Configures the asset and rendition tables.</summary>
    /// <param name="builder">The entity builder.</param>
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.ToTable("assets");
        builder.HasKey(asset => asset.Id);

        // Get-only properties are mapped explicitly; EF Core skips them by convention.
        builder.Property(asset => asset.PlayerId);
        builder.Property(asset => asset.FolderId);
        builder.Property(asset => asset.CreatedAt);

        builder.OwnsOne(asset => asset.File, file =>
        {
            file.Property(f => f.OriginalName).HasColumnName("original_name").HasMaxLength(500);
            file.Property(f => f.Md5).HasColumnName("md5").HasMaxLength(32).IsFixedLength();
            file.Property(f => f.SizeBytes).HasColumnName("size_bytes");
            file.Property(f => f.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(10);
            file.HasIndex(f => f.Md5);
        });
        builder.Navigation(asset => asset.File).IsRequired();

        builder.Property(asset => asset.Source).HasConversion<string>().HasMaxLength(10);
        builder.Property(asset => asset.DriveFileId).HasMaxLength(200);
        builder.Property(asset => asset.Title).HasMaxLength(200);
        builder.Property(asset => asset.Visibility).HasConversion<string>().HasMaxLength(10);
        builder.Property(asset => asset.State).HasConversion<string>().HasMaxLength(20);
        builder.Property(asset => asset.FailureReason).HasMaxLength(1000);
        builder.Property(asset => asset.OriginalKey).HasConversion(ValueConverters.StorageKey).HasMaxLength(1024);
        builder.Property(asset => asset.PosterKey).HasConversion(ValueConverters.StorageKey).HasMaxLength(1024);
        builder.Property(asset => asset.ThumbnailKey).HasConversion(ValueConverters.StorageKey).HasMaxLength(1024);
        builder.Property(asset => asset.SpriteKey).HasConversion(ValueConverters.StorageKey).HasMaxLength(1024);

        builder.HasMany(asset => asset.Renditions)
            .WithOne()
            .HasForeignKey("AssetId")
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(asset => asset.Renditions).HasField("_renditions").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(asset => new { asset.PlayerId, asset.SortOrder });
        builder.HasIndex(asset => asset.FolderId);
        builder.HasIndex(asset => asset.DriveFileId);
        builder.HasIndex(asset => asset.State);

        builder.Ignore(asset => asset.Kind);
        builder.Ignore(asset => asset.IsReady);
    }
}

/// <summary>Maps <see cref="Rendition"/> to <c>renditions</c>: one row per asset and quality.</summary>
internal sealed class RenditionConfiguration : IEntityTypeConfiguration<Rendition>
{
    /// <summary>Configures the rendition table.</summary>
    /// <param name="builder">The entity builder.</param>
    public void Configure(EntityTypeBuilder<Rendition> builder)
    {
        builder.ToTable("renditions");
        builder.HasKey(rendition => rendition.Id);

        builder.Property(rendition => rendition.Quality);
        builder.Property(rendition => rendition.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(rendition => rendition.PlaylistKey).HasConversion(ValueConverters.StorageKey).HasMaxLength(1024);

        builder.HasIndex("AssetId", nameof(Rendition.Quality)).IsUnique();
    }
}
