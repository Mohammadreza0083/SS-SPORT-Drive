using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SsSport.Drive.Domain.Users;

namespace SsSport.Drive.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="User"/> to <c>users</c>; Telegram ids and emails are unique.</summary>
internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <summary>Configures the user table.</summary>
    /// <param name="builder">The entity builder.</param>
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(user => user.Id);

        // Get-only properties are mapped explicitly; EF Core skips them by convention.
        builder.Property(user => user.TelegramId);

        builder.Property(user => user.Username).HasMaxLength(64);
        builder.Property(user => user.DisplayName).HasMaxLength(100);
        builder.Property(user => user.Email).HasConversion(ValueConverters.Email).HasMaxLength(254);
        builder.Property(user => user.Role).HasConversion(ValueConverters.Role).HasMaxLength(20);
        builder.Property(user => user.Status).HasConversion<string>().HasMaxLength(10);

        builder.HasIndex(user => user.TelegramId).IsUnique();
        builder.HasIndex(user => user.Email).IsUnique();
        builder.HasIndex(user => user.Status);

        builder.Ignore(user => user.IsActive);
        builder.Ignore(user => user.IsAdmin);
    }
}
