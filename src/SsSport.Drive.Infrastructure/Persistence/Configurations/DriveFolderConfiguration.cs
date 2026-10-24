using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SsSport.Drive.Domain.DriveFolders;

namespace SsSport.Drive.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="DriveFolder"/> to <c>drive_folders</c>; a Drive folder can be linked only once.</summary>
internal sealed class DriveFolderConfiguration : IEntityTypeConfiguration<DriveFolder>
{
    /// <summary>Configures the folder table.</summary>
    /// <param name="builder">The entity builder.</param>
    public void Configure(EntityTypeBuilder<DriveFolder> builder)
    {
        builder.ToTable("drive_folders");
        builder.HasKey(folder => folder.Id);

        // Get-only properties are mapped explicitly; EF Core skips them by convention.
        builder.Property(folder => folder.PlayerId);
        builder.Property(folder => folder.LinkedAt);

        builder.Property(folder => folder.DriveFolderId).HasMaxLength(200);
        builder.Property(folder => folder.Name).HasMaxLength(500);
        builder.Property(folder => folder.Access).HasConversion<string>().HasMaxLength(10);

        builder.HasIndex(folder => folder.DriveFolderId).IsUnique();
        builder.HasIndex(folder => folder.PlayerId);
    }
}
