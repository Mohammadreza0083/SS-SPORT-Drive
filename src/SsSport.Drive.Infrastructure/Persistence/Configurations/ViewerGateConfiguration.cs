using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SsSport.Drive.Domain.Links;

namespace SsSport.Drive.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="EmailChallenge"/> to <c>email_challenges</c>, looked up by link and email, newest first.</summary>
internal sealed class EmailChallengeConfiguration : IEntityTypeConfiguration<EmailChallenge>
{
    /// <summary>Configures the email code table.</summary>
    /// <param name="builder">The entity builder.</param>
    public void Configure(EntityTypeBuilder<EmailChallenge> builder)
    {
        builder.ToTable("email_challenges");
        builder.HasKey(challenge => challenge.Id);

        // Get-only properties are mapped explicitly; EF Core skips them by convention.
        builder.Property(challenge => challenge.ShareLinkId);
        builder.Property(challenge => challenge.Email).HasConversion(ValueConverters.RequiredEmail).HasMaxLength(254);
        builder.Property(challenge => challenge.IsForward);
        builder.Property(challenge => challenge.RequestedAt);
        builder.Property(challenge => challenge.CodeHash).HasMaxLength(100);

        builder.HasIndex(challenge => new { challenge.ShareLinkId, challenge.Email, challenge.RequestedAt });
    }
}

/// <summary>Maps <see cref="TrustedDevice"/> to <c>trusted_devices</c>: one row per device token and link.</summary>
internal sealed class TrustedDeviceConfiguration : IEntityTypeConfiguration<TrustedDevice>
{
    /// <summary>Configures the trusted device table.</summary>
    /// <param name="builder">The entity builder.</param>
    public void Configure(EntityTypeBuilder<TrustedDevice> builder)
    {
        builder.ToTable("trusted_devices");
        builder.HasKey(device => device.Id);

        // Get-only properties are mapped explicitly; EF Core skips them by convention.
        builder.Property(device => device.TokenHash).HasMaxLength(64);
        builder.Property(device => device.ShareLinkId);
        builder.Property(device => device.Email).HasConversion(ValueConverters.RequiredEmail).HasMaxLength(254);

        builder.HasIndex(device => new { device.TokenHash, device.ShareLinkId }).IsUnique();
    }
}

/// <summary>Maps <see cref="ViewerSession"/> to <c>viewer_sessions</c>, looked up by token hash.</summary>
internal sealed class ViewerSessionConfiguration : IEntityTypeConfiguration<ViewerSession>
{
    /// <summary>Configures the viewer session table.</summary>
    /// <param name="builder">The entity builder.</param>
    public void Configure(EntityTypeBuilder<ViewerSession> builder)
    {
        builder.ToTable("viewer_sessions");
        builder.HasKey(session => session.Id);

        // Get-only properties are mapped explicitly; EF Core skips them by convention.
        builder.Property(session => session.TokenHash).HasMaxLength(64);
        builder.Property(session => session.ShareLinkId);
        builder.Property(session => session.Email).HasConversion(ValueConverters.RequiredEmail).HasMaxLength(254);
        builder.Property(session => session.CreatedAt);
        builder.Property(session => session.ExpiresAt);
        builder.Property(session => session.IsForward);
        builder.Property(session => session.DeviceId);

        builder.HasIndex(session => session.TokenHash).IsUnique();
        builder.HasIndex(session => session.ExpiresAt);
    }
}
