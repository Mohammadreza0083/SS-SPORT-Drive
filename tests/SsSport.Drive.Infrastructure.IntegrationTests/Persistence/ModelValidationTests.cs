using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using SsSport.Drive.Domain.Players;
using SsSport.Drive.Infrastructure.Persistence;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Persistence;

/// <summary>The EF Core model builds without mapping warnings; no database is needed.</summary>
public sealed class ModelValidationTests
{
    /// <summary>
    /// EF never has to invent a shadow foreign key (warning 10625), e.g. because an owned type has a property with the
    /// same name as its default owner key.
    /// </summary>
    [Fact]
    public void Model_creates_no_shadow_foreign_keys()
    {
        using var context = CreateContext(warnings => warnings.Throw(CoreEventId.ShadowForeignKeyPropertyCreated));

        var build = () => context.Model;

        build.Should().NotThrow();
    }

    /// <summary>The owned Volleybox profile shares the players row: its key is the player's <c>id</c> column, with no extra columns.</summary>
    [Fact]
    public void Volleybox_profile_shares_the_player_key()
    {
        using var context = CreateContext(_ => { });
        var owned = context.Model.FindEntityType(typeof(VolleyboxProfile))!;
        var table = StoreObjectIdentifier.Table("players");

        owned.GetProperties().Select(property => property.Name).Should().NotContain(name => name.EndsWith('1'));
        owned.FindPrimaryKey()!.Properties.Should().ContainSingle().Which.GetColumnName(table).Should().Be("id");
        owned.GetProperties().Select(property => property.GetColumnName(table)).Should().BeEquivalentTo(["id", "volleybox_player_id", "volleybox_url"]);
    }

    /// <summary>Creates a context without a database, with extra warning rules.</summary>
    /// <param name="warnings">Configures how warnings are treated.</param>
    /// <returns>The context.</returns>
    private static AppDbContext CreateContext(Action<WarningsConfigurationBuilder> warnings) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseAppDatabase("Host=localhost;Database=unused;Username=unused")
            .ConfigureWarnings(warnings)
            .Options);
}
