using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Domain.Countries;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Persistence;

/// <summary>Migrating a clean database seeds every country of the catalogue into <c>countries</c>.</summary>
/// <param name="fixture">The migrated database.</param>
public sealed class CountrySeedTests(AppDbContextFixture fixture) : IClassFixture<AppDbContextFixture>
{
    /// <summary>All 245 countries are present with their names.</summary>
    [Fact]
    public async Task Every_country_is_seeded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = fixture.CreateContext();

        var countries = await context.Set<Country>().AsNoTracking().ToListAsync(cancellationToken);

        countries.Should().HaveCount(245).And.BeEquivalentTo(CountryCatalogue.All);
        countries.Single(country => country.Code == "IR").Name.Should().Be("Iran");
    }
}
