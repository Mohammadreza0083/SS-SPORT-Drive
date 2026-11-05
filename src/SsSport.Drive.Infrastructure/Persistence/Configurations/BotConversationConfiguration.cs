using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SsSport.Drive.Domain.Conversations;

namespace SsSport.Drive.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="BotConversation"/> to <c>bot_conversations</c> (T-10a): one row per user, the step by name and the
/// draft as <c>jsonb</c>.
/// </summary>
internal sealed class BotConversationConfiguration : IEntityTypeConfiguration<BotConversation>
{
    /// <summary>How the draft is written to its column.</summary>
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>Configures the table.</summary>
    /// <param name="builder">The entity builder.</param>
    public void Configure(EntityTypeBuilder<BotConversation> builder)
    {
        builder.ToTable("bot_conversations");
        builder.HasKey(conversation => conversation.Id);

        // Get-only properties are mapped explicitly; EF Core skips them by convention.
        builder.Property(conversation => conversation.TelegramUserId);
        builder.Property(conversation => conversation.Step).HasConversion<string>().HasMaxLength(20);
        builder.Property(conversation => conversation.Draft)
            .HasColumnType("jsonb")
            .HasConversion(
                draft => JsonSerializer.Serialize(draft, _json),
                json => JsonSerializer.Deserialize<NewPlayerDraft>(json, _json) ?? new NewPlayerDraft());

        builder.HasIndex(conversation => conversation.TelegramUserId).IsUnique();
    }
}
