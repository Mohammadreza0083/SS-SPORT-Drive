using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SsSport.Drive.Domain.Links;

namespace SsSport.Drive.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Recipient"/> to <c>recipients</c>; the emails are a <c>text[]</c> in the order given.</summary>
internal sealed class RecipientConfiguration : IEntityTypeConfiguration<Recipient>
{
    /// <summary>Configures the recipient table.</summary>
    /// <param name="builder">The entity builder.</param>
    public void Configure(EntityTypeBuilder<Recipient> builder)
    {
        builder.ToTable("recipients");
        builder.HasKey(recipient => recipient.Id);

        // Get-only properties are mapped explicitly; EF Core skips them by convention.
        builder.Property(recipient => recipient.CreatedAt);

        builder.Property(recipient => recipient.Name).HasMaxLength(Recipient.MaxNameLength);
        builder.Property(recipient => recipient.Organisation).HasMaxLength(Recipient.MaxOrganisationLength);
        builder.Property(recipient => recipient.Country).HasColumnName("country_code").HasConversion(ValueConverters.Nationality).HasMaxLength(2);
        builder.Property<List<Domain.Primitives.EmailAddress>>("_emails")
            .HasColumnName("emails")
            .HasConversion(ValueConverters.Emails, ValueConverters.EmailsComparer)
            .HasColumnType("text[]");
        builder.Ignore(recipient => recipient.Emails);
    }
}
