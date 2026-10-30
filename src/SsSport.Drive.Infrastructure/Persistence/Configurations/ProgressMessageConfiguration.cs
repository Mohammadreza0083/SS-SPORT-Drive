using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SsSport.Drive.Domain.Notifications;

namespace SsSport.Drive.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="ProgressMessage"/> to <c>progress_messages</c>: one row per player (T-6a).</summary>
internal sealed class ProgressMessageConfiguration : IEntityTypeConfiguration<ProgressMessage>
{
    /// <summary>Configures the table.</summary>
    /// <param name="builder">The entity builder.</param>
    public void Configure(EntityTypeBuilder<ProgressMessage> builder)
    {
        builder.ToTable("progress_messages");
        builder.HasKey(message => message.Id);

        // Get-only properties are mapped explicitly; EF Core skips them by convention.
        builder.Property(message => message.PlayerId);
        builder.Property(message => message.Text).HasMaxLength(ProgressMessage.MaxTextLength);

        builder.HasIndex(message => message.PlayerId).IsUnique();
    }
}
