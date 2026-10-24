using Microsoft.EntityFrameworkCore;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Persistence;

/// <summary>The migrations build the whole schema on a clean database and match the current model.</summary>
/// <param name="fixture">The migrated database.</param>
public sealed class MigrationTests(AppDbContextFixture fixture) : IClassFixture<AppDbContextFixture>
{
    /// <summary>Every migration is applied and none is pending.</summary>
    [Fact]
    public async Task Migrations_apply_on_a_clean_database()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = fixture.CreateContext();

        (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).Should().Contain(m => m.EndsWith("_InitialCreate", StringComparison.Ordinal));
        (await context.Database.GetPendingMigrationsAsync(cancellationToken)).Should().BeEmpty();
    }

    /// <summary>
    /// The model has no changes that a migration does not cover. If this fails, run
    /// <c>dotnet ef migrations add &lt;Name&gt;</c> (see the README).
    /// </summary>
    [Fact]
    public void Model_has_no_pending_changes()
    {
        using var context = fixture.CreateContext();

        context.Database.HasPendingModelChanges().Should().BeFalse();
    }

    /// <summary>Tables and columns use snake_case names.</summary>
    [Fact]
    public async Task Tables_use_snake_case()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = fixture.CreateContext();

        var tables = await context.Database
            .SqlQueryRaw<string>("SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public'")
            .ToListAsync(cancellationToken);
        var playerColumns = await context.Database
            .SqlQueryRaw<string>("SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_name = 'players'")
            .ToListAsync(cancellationToken);

        tables.Should().Contain(["players", "drive_folders", "assets", "renditions", "users", "audit_entries"]);
        playerColumns.Should().Contain(["id", "full_name", "slug", "volleybox_player_id", "photo_key", "qr_code_key", "analyst_id", "hero_asset_id"]);
    }
}
