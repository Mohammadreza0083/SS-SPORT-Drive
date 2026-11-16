using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SsSport.Drive.Domain.Archive;

namespace SsSport.Drive.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="ArchiveScan"/> to <c>archive_scans</c> (M-1); the totals are columns of the same row.</summary>
internal sealed class ArchiveScanConfiguration : IEntityTypeConfiguration<ArchiveScan>
{
    /// <summary>Configures the scan table.</summary>
    /// <param name="builder">The entity builder.</param>
    public void Configure(EntityTypeBuilder<ArchiveScan> builder)
    {
        builder.ToTable("archive_scans");
        builder.HasKey(scan => scan.Id);

        // Get-only properties are mapped explicitly; EF Core skips them by convention.
        builder.Property(scan => scan.StartedBy);
        builder.Property(scan => scan.StartedAt);

        builder.Property(scan => scan.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(scan => scan.FailureReason).HasMaxLength(500);
        builder.ComplexProperty(scan => scan.Totals, totals =>
        {
            totals.Property(t => t.Folders).HasColumnName("folders");
            totals.Property(t => t.Files).HasColumnName("files");
            totals.Property(t => t.Bytes).HasColumnName("bytes");
            totals.Property(t => t.InBackup).HasColumnName("in_backup");
            totals.Property(t => t.MissingFromBackup).HasColumnName("missing_from_backup");
            totals.Property(t => t.Skipped).HasColumnName("skipped");
        });

        builder.HasIndex(scan => scan.StartedAt);
    }
}

/// <summary>Maps <see cref="ArchiveFolder"/> to <c>archive_folders</c> (M-1, M-2); the counts are columns of the same row.</summary>
internal sealed class ArchiveFolderConfiguration : IEntityTypeConfiguration<ArchiveFolder>
{
    /// <summary>Configures the folder table.</summary>
    /// <param name="builder">The entity builder.</param>
    public void Configure(EntityTypeBuilder<ArchiveFolder> builder)
    {
        builder.ToTable("archive_folders");
        builder.HasKey(folder => folder.Id);

        // Get-only properties are mapped explicitly; EF Core skips them by convention.
        builder.Property(folder => folder.ScanId);
        builder.Property(folder => folder.DriveFolderId).HasMaxLength(200);
        builder.Property(folder => folder.Path).HasMaxLength(1100);
        builder.Property(folder => folder.FolderName).HasMaxLength(500);
        builder.Property(folder => folder.Category).HasConversion<string>().HasMaxLength(10);
        builder.Property(folder => folder.FoundAt);

        builder.Property(folder => folder.SuggestedName).HasMaxLength(100);
        builder.Property(folder => folder.SuggestedPositions).HasConversion(ValueConverters.Positions, ValueConverters.PositionsComparer).HasColumnType("text[]");
        builder.Property(folder => folder.State).HasConversion<string>().HasMaxLength(10);
        builder.ComplexProperty(folder => folder.Counts, counts =>
        {
            counts.Ignore(c => c.Files);
            counts.Property(c => c.Videos).HasColumnName("videos");
            counts.Property(c => c.Documents).HasColumnName("documents");
            counts.Property(c => c.Scouts).HasColumnName("scouts");
            counts.Property(c => c.Bytes).HasColumnName("bytes");
            counts.Property(c => c.InBackup).HasColumnName("in_backup");
            counts.Property(c => c.MissingFromBackup).HasColumnName("missing_from_backup");
            counts.Property(c => c.Skipped).HasColumnName("skipped");
            counts.Property(c => c.IsTruncated).HasColumnName("is_truncated");
        });

        builder.HasOne<ArchiveScan>().WithMany().HasForeignKey(folder => folder.ScanId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(folder => new { folder.ScanId, folder.State, folder.Path });
        builder.HasIndex(folder => new { folder.ScanId, folder.DriveFolderId }).IsUnique();
    }
}
