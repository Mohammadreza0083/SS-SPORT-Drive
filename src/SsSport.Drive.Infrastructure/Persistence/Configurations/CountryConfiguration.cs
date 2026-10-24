using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SsSport.Drive.Domain.Countries;

namespace SsSport.Drive.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Country"/> to the <c>countries</c> lookup and seeds the whole <see cref="CountryCatalogue"/> through the
/// migrations, so every environment has the same list. A change to the catalogue needs a new migration.
/// </summary>
internal sealed class CountryConfiguration : IEntityTypeConfiguration<Country>
{
    /// <summary>Configures and seeds the country table.</summary>
    /// <param name="builder">The entity builder.</param>
    public void Configure(EntityTypeBuilder<Country> builder)
    {
        builder.ToTable("countries");
        builder.HasKey(country => country.Code);

        builder.Property(country => country.Code).HasMaxLength(2).IsFixedLength();
        builder.Property(country => country.Name).HasMaxLength(100);

        builder.HasData(CountryCatalogue.All);
    }
}
