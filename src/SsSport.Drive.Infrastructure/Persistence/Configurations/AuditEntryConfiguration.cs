using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SsSport.Drive.Domain.Audit;

namespace SsSport.Drive.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="AuditEntry"/> to <c>audit_entries</c>; data is kept as written in a <c>json</c> column.</summary>
internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    /// <summary>Configures the audit table.</summary>
    /// <param name="builder">The entity builder.</param>
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries");
        builder.HasKey(entry => entry.Id);

        // Get-only properties are mapped explicitly; EF Core skips them by convention.
        builder.Property(entry => entry.ActorUserId);
        builder.Property(entry => entry.At);

        builder.Property(entry => entry.Action).HasMaxLength(100);
        builder.Property(entry => entry.TargetType).HasMaxLength(50);
        builder.Property(entry => entry.TargetId).HasMaxLength(100);
        builder.Property(entry => entry.Data).HasColumnType("json");

        builder.HasIndex(entry => new { entry.TargetType, entry.TargetId });
        builder.HasIndex(entry => entry.At);
        builder.HasIndex(entry => entry.ActorUserId);
    }
}
