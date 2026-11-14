using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SsSport.Drive.Domain.Settings;
using SsSport.Drive.Domain.Viewers;

namespace SsSport.Drive.Infrastructure.Persistence.Configurations;

/// <summary>Maps the single settings row (A-10).</summary>
internal sealed class AppSettingsConfiguration : IEntityTypeConfiguration<AppSettings>
{
    /// <summary>Configures the table.</summary>
    /// <param name="builder">The entity builder.</param>
    public void Configure(EntityTypeBuilder<AppSettings> builder)
    {
        builder.ToTable("app_settings");
        builder.HasKey(settings => settings.Id);
        builder.Property(settings => settings.ContactEmail).HasConversion(ValueConverters.RequiredEmail).HasMaxLength(254);
        builder.Property(settings => settings.DigestAt);
        builder.Property(settings => settings.QuietHoursStart);
        builder.Property(settings => settings.QuietHoursEnd);
        builder.Property(settings => settings.DefaultLinkExpiryDays);
        builder.Property(settings => settings.UpdatedAt);
        builder.Property(settings => settings.UpdatedByUserId);
    }
}

/// <summary>Maps viewers: one row per email address that can watch (A-7, A-11).</summary>
internal sealed class ViewerConfiguration : IEntityTypeConfiguration<Viewer>
{
    /// <summary>Configures the table.</summary>
    /// <param name="builder">The entity builder.</param>
    public void Configure(EntityTypeBuilder<Viewer> builder)
    {
        builder.ToTable("viewers");
        builder.HasKey(viewer => viewer.Id);

        // Get-only properties are mapped explicitly; EF Core skips them by convention.
        builder.Property(viewer => viewer.Email).HasConversion(ValueConverters.RequiredEmail).HasMaxLength(254);
        builder.Property(viewer => viewer.FirstSeenAt);

        builder.HasIndex(viewer => viewer.Email).IsUnique();
    }
}
