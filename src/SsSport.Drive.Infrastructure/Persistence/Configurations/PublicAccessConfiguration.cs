using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SsSport.Drive.Domain.AccessRequests;
using SsSport.Drive.Domain.Viewers;

namespace SsSport.Drive.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="AccessCodeChallenge"/> to <c>access_code_challenges</c>, looked up by email, newest first.</summary>
internal sealed class AccessCodeChallengeConfiguration : IEntityTypeConfiguration<AccessCodeChallenge>
{
    /// <summary>Configures the visitor code table.</summary>
    /// <param name="builder">The entity builder.</param>
    public void Configure(EntityTypeBuilder<AccessCodeChallenge> builder)
    {
        builder.ToTable("access_code_challenges");
        builder.HasKey(challenge => challenge.Id);

        // Get-only properties are mapped explicitly; EF Core skips them by convention.
        builder.Property(challenge => challenge.Email).HasConversion(ValueConverters.RequiredEmail).HasMaxLength(254);
        builder.Property(challenge => challenge.RequestedAt);
        builder.Property(challenge => challenge.CodeHash).HasMaxLength(100);
        builder.Ignore(challenge => challenge.ResendAt);
        builder.Ignore(challenge => challenge.AttemptsLeft);

        builder.HasIndex(challenge => new { challenge.Email, challenge.RequestedAt });

        // The daily clean-up deletes by age.
        builder.HasIndex(challenge => challenge.RequestedAt);
    }
}

/// <summary>Maps <see cref="AccessRequest"/> to <c>access_requests</c>.</summary>
internal sealed class AccessRequestConfiguration : IEntityTypeConfiguration<AccessRequest>
{
    /// <summary>Configures the access request table.</summary>
    /// <param name="builder">The entity builder.</param>
    public void Configure(EntityTypeBuilder<AccessRequest> builder)
    {
        builder.ToTable("access_requests");
        builder.HasKey(request => request.Id);

        // Get-only properties are mapped explicitly; EF Core skips them by convention.
        builder.Property(request => request.PlayerId);
        builder.Property(request => request.Email).HasConversion(ValueConverters.RequiredEmail).HasMaxLength(254);
        builder.Property(request => request.About).HasMaxLength(1000);
        builder.Property(request => request.Source).HasConversion<string>().HasMaxLength(16);
        builder.Property(request => request.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(request => request.CreatedAt);

        // The per-email and per-player limits, and the pending check, count by these.
        builder.HasIndex(request => new { request.Email, request.CreatedAt });
        builder.HasIndex(request => new { request.PlayerId, request.CreatedAt });
        builder.HasIndex(request => new { request.Email, request.PlayerId, request.Status });
    }
}

/// <summary>Maps <see cref="ViewerAccountSession"/> to <c>viewer_account_sessions</c>, looked up by token hash.</summary>
internal sealed class ViewerAccountSessionConfiguration : IEntityTypeConfiguration<ViewerAccountSession>
{
    /// <summary>Configures the signed-in viewer session table.</summary>
    /// <param name="builder">The entity builder.</param>
    public void Configure(EntityTypeBuilder<ViewerAccountSession> builder)
    {
        builder.ToTable("viewer_account_sessions");
        builder.HasKey(session => session.Id);

        // Get-only properties are mapped explicitly; EF Core skips them by convention.
        builder.Property(session => session.TokenHash).HasMaxLength(64);
        builder.Property(session => session.Email).HasConversion(ValueConverters.RequiredEmail).HasMaxLength(254);
        builder.Property(session => session.StartedAt);
        builder.Property(session => session.ExpiresAt);

        builder.HasIndex(session => session.TokenHash).IsUnique();
        builder.HasIndex(session => session.ExpiresAt);
    }
}
