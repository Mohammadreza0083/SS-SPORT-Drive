using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SsSport.Drive.Domain.Accounts;
using SsSport.Drive.Domain.Emails;
using SsSport.Drive.Domain.Tracking;

namespace SsSport.Drive.Infrastructure.Persistence.Configurations;

/// <summary>Maps the email queue (L-4): table <c>outgoing_emails</c>.</summary>
internal sealed class OutgoingEmailConfiguration : IEntityTypeConfiguration<OutgoingEmail>
{
    /// <summary>Configures the table.</summary>
    /// <param name="builder">The entity builder.</param>
    public void Configure(EntityTypeBuilder<OutgoingEmail> builder)
    {
        builder.ToTable("outgoing_emails");
        builder.HasKey(email => email.Id);

        // Get-only properties are mapped explicitly; EF Core skips them by convention.
        builder.Property(email => email.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(email => email.Template).HasMaxLength(64);
        builder.Property(email => email.To).HasColumnName("to_address").HasConversion(ValueConverters.RequiredEmail).HasMaxLength(254);
        builder.Property(email => email.Data).HasColumnType("jsonb");
        builder.Property(email => email.QueuedAt);
        builder.Property(email => email.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(email => email.Provider).HasMaxLength(32);
        builder.Property(email => email.LastError).HasMaxLength(OutgoingEmail.MaxErrorLength);

        // The admin queue reads newest first, optionally of one status.
        builder.HasIndex(email => new { email.QueuedAt, email.Id }).IsDescending(true, true);
        builder.HasIndex(email => new { email.Status, email.QueuedAt });
    }
}

/// <summary>Maps one-time sign-in links (A-1, P-5): table <c>sign_in_links</c>.</summary>
internal sealed class SignInLinkConfiguration : IEntityTypeConfiguration<SignInLink>
{
    /// <summary>Configures the table.</summary>
    /// <param name="builder">The entity builder.</param>
    public void Configure(EntityTypeBuilder<SignInLink> builder)
    {
        builder.ToTable("sign_in_links");
        builder.HasKey(link => link.Id);

        // Get-only properties are mapped explicitly; EF Core skips them by convention.
        builder.Property(link => link.Audience).HasConversion<string>().HasMaxLength(16);
        builder.Property(link => link.Email).HasConversion(ValueConverters.RequiredEmail).HasMaxLength(254);
        builder.Property(link => link.CreatedAt);
        builder.Property(link => link.TokenHash).HasMaxLength(64);

        builder.HasIndex(link => link.TokenHash).IsUnique();

        // The per-email hourly cap counts recent links.
        builder.HasIndex(link => new { link.Email, link.Audience, link.CreatedAt });
    }
}

/// <summary>Maps signed-in sessions (A-1, P-5): table <c>account_sessions</c>.</summary>
internal sealed class AccountSessionConfiguration : IEntityTypeConfiguration<AccountSession>
{
    /// <summary>Configures the table.</summary>
    /// <param name="builder">The entity builder.</param>
    public void Configure(EntityTypeBuilder<AccountSession> builder)
    {
        builder.ToTable("account_sessions");
        builder.HasKey(session => session.Id);

        // Get-only properties are mapped explicitly; EF Core skips them by convention.
        builder.Property(session => session.Audience).HasConversion<string>().HasMaxLength(16);
        builder.Property(session => session.SubjectId);
        builder.Property(session => session.Email).HasConversion(ValueConverters.RequiredEmail).HasMaxLength(254);
        builder.Property(session => session.TokenHash).HasMaxLength(64);
        builder.Property(session => session.CreatedAt);
        builder.Property(session => session.ExpiresAt);

        builder.HasIndex(session => session.TokenHash).IsUnique();
        builder.HasIndex(session => session.ExpiresAt);
    }
}

/// <summary>Maps viewer accounts (P-5): table <c>viewer_accounts</c>.</summary>
internal sealed class ViewerAccountConfiguration : IEntityTypeConfiguration<ViewerAccount>
{
    /// <summary>Configures the table.</summary>
    /// <param name="builder">The entity builder.</param>
    public void Configure(EntityTypeBuilder<ViewerAccount> builder)
    {
        builder.ToTable("viewer_accounts");
        builder.HasKey(account => account.Id);

        // Get-only properties are mapped explicitly; EF Core skips them by convention.
        builder.Property(account => account.Email).HasConversion(ValueConverters.RequiredEmail).HasMaxLength(254);
        builder.Property(account => account.CreatedAt);

        builder.HasIndex(account => account.Email).IsUnique();
    }
}

/// <summary>Maps watches (L-5): table <c>watch_sessions</c>, the played ranges as <c>jsonb</c>.</summary>
internal sealed class WatchSessionConfiguration : IEntityTypeConfiguration<WatchSession>
{
    /// <summary>The JSON options of the stored ranges.</summary>
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>Compares ranges by value, so a heartbeat that changes them is saved.</summary>
    private static readonly ValueComparer<List<WatchRange>> _rangesComparer = new(
        (left, right) => left!.SequenceEqual(right!),
        ranges => ranges.Aggregate(0, (hash, range) => HashCode.Combine(hash, range)),
        ranges => ranges.ToList());

    /// <summary>Configures the table.</summary>
    /// <param name="builder">The entity builder.</param>
    public void Configure(EntityTypeBuilder<WatchSession> builder)
    {
        builder.ToTable("watch_sessions");
        builder.HasKey(watch => watch.Id);

        // Get-only properties are mapped explicitly; EF Core skips them by convention. The viewer session is kept as an
        // id only: the daily gate clean-up deletes sessions, but the watch stays.
        builder.Property(watch => watch.ViewerSessionId);
        builder.Property(watch => watch.ShareLinkId);
        builder.Property(watch => watch.PlayerId);
        builder.Property(watch => watch.AssetId);
        builder.Property(watch => watch.Email).HasConversion(ValueConverters.RequiredEmail).HasMaxLength(254);
        builder.Property(watch => watch.DurationSeconds);
        builder.Property(watch => watch.StartedAt);
        builder.Property(watch => watch.LastEventType).HasConversion<string>().HasMaxLength(16);
        builder.Property<List<WatchRange>>("_ranges")
            .HasColumnName("ranges")
            .HasColumnType("jsonb")
            .HasConversion(
                ranges => JsonSerializer.Serialize(ranges, _json),
                json => JsonSerializer.Deserialize<List<WatchRange>>(json, _json) ?? new List<WatchRange>(),
                _rangesComparer);
        builder.Ignore(watch => watch.Ranges);
        builder.Ignore(watch => watch.UniqueSeconds);
        builder.Ignore(watch => watch.WatchSeconds);
        builder.Ignore(watch => watch.Completion);
        builder.Ignore(watch => watch.IsComplete);

        builder.HasIndex(watch => new { watch.ShareLinkId, watch.Email, watch.AssetId });
        builder.HasIndex(watch => watch.ViewerSessionId);
    }
}

/// <summary>Maps player events (L-5): table <c>tracking_events</c>.</summary>
internal sealed class TrackingEventConfiguration : IEntityTypeConfiguration<TrackingEvent>
{
    /// <summary>Configures the table.</summary>
    /// <param name="builder">The entity builder.</param>
    public void Configure(EntityTypeBuilder<TrackingEvent> builder)
    {
        builder.ToTable("tracking_events");
        builder.HasKey(item => item.Id);

        // Get-only properties are mapped explicitly; EF Core skips them by convention.
        builder.Property(item => item.WatchSessionId);
        builder.Property(item => item.Type).HasConversion<string>().HasMaxLength(16);
        builder.Property(item => item.PositionSeconds);
        builder.Property(item => item.OccurredAt);

        builder.HasIndex(item => new { item.WatchSessionId, item.Id });
    }
}
