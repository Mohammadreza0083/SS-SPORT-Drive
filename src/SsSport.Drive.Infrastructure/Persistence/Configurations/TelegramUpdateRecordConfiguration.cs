using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SsSport.Drive.Infrastructure.Telegram;

namespace SsSport.Drive.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="TelegramUpdateRecord"/> to <c>telegram_updates</c>: one row per received update id.</summary>
internal sealed class TelegramUpdateRecordConfiguration : IEntityTypeConfiguration<TelegramUpdateRecord>
{
    /// <summary>Configures the inbox table.</summary>
    /// <param name="builder">The entity builder.</param>
    public void Configure(EntityTypeBuilder<TelegramUpdateRecord> builder)
    {
        builder.ToTable("telegram_updates");
        builder.HasKey(record => record.UpdateId);
        builder.Property(record => record.UpdateId).ValueGeneratedNever();
        builder.Property(record => record.ReceivedAt);

        // Old rows are pruned by received_at (a later clean-up job).
        builder.HasIndex(record => record.ReceivedAt);
    }
}
