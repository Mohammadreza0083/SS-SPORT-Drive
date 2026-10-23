using Microsoft.EntityFrameworkCore;
using SsSport.Drive.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// The generic repository stages changes to aggregates; only the unit of work writes them to PostgreSQL.
/// Every test uses fresh contexts, so assertions read what is really in the database.
/// </summary>
public sealed class RepositoryAndUnitOfWorkTests : IAsyncLifetime
{
    /// <summary>A throwaway PostgreSQL 17 server; keep the image in step with docker-compose.</summary>
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>Starts PostgreSQL and creates the test schema.</summary>
    /// <returns>A task that completes when the database is ready.</returns>
    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    /// <summary>Stops and removes the PostgreSQL container.</summary>
    /// <returns>A task that completes when the container is gone.</returns>
    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    /// <summary>An added aggregate is written by the unit of work and can be loaded again by id.</summary>
    [Fact]
    public async Task Added_aggregate_is_saved_and_can_be_loaded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var note = new Note(Guid.NewGuid(), "Highlights");

        await using (var context = CreateContext())
        {
            new Repository<Note, Guid>(context).Add(note);
            var written = await new UnitOfWork(context).SaveChangesAsync(cancellationToken);
            written.Should().Be(1);
        }

        await using var reader = CreateContext();
        var loaded = await new Repository<Note, Guid>(reader).GetByIdAsync(note.Id, cancellationToken);
        loaded.Should().NotBeNull();
        loaded!.Title.Should().Be("Highlights");
    }

    /// <summary>Loading an id that does not exist returns null instead of throwing.</summary>
    [Fact]
    public async Task Unknown_id_returns_null()
    {
        await using var context = CreateContext();

        var loaded = await new Repository<Note, Guid>(context).GetByIdAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        loaded.Should().BeNull();
    }

    /// <summary>Changes made to a loaded aggregate are tracked and written on save, with no explicit update call.</summary>
    [Fact]
    public async Task Changes_to_a_loaded_aggregate_are_saved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = await SeedAsync("Full match");

        await using (var context = CreateContext())
        {
            var note = await new Repository<Note, Guid>(context).GetByIdAsync(id, cancellationToken);
            note!.Rename("Full match (renamed)");
            await new UnitOfWork(context).SaveChangesAsync(cancellationToken);
        }

        await using var reader = CreateContext();
        var reloaded = await new Repository<Note, Guid>(reader).GetByIdAsync(id, cancellationToken);
        reloaded!.Title.Should().Be("Full match (renamed)");
    }

    /// <summary>A removed aggregate is deleted on save.</summary>
    [Fact]
    public async Task Removed_aggregate_is_deleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = await SeedAsync("Stats");

        await using (var context = CreateContext())
        {
            var repository = new Repository<Note, Guid>(context);
            repository.Remove((await repository.GetByIdAsync(id, cancellationToken))!);
            await new UnitOfWork(context).SaveChangesAsync(cancellationToken);
        }

        await using var reader = CreateContext();
        (await new Repository<Note, Guid>(reader).GetByIdAsync(id, cancellationToken)).Should().BeNull();
    }

    /// <summary>The repository only stages changes: without the unit of work nothing reaches the database.</summary>
    [Fact]
    public async Task Nothing_is_written_without_the_unit_of_work()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var note = new Note(Guid.NewGuid(), "Draft");

        await using (var context = CreateContext())
        {
            new Repository<Note, Guid>(context).Add(note);
        }

        await using var reader = CreateContext();
        (await new Repository<Note, Guid>(reader).GetByIdAsync(note.Id, cancellationToken)).Should().BeNull();
    }

    /// <summary>Saving a note with pending domain events works: events are not mapped as data.</summary>
    [Fact]
    public async Task Domain_events_are_not_persisted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var note = new Note(Guid.NewGuid(), "Before");
        note.Rename("After");

        await using var context = CreateContext();
        new Repository<Note, Guid>(context).Add(note);

        var save = async () => await new UnitOfWork(context).SaveChangesAsync(cancellationToken);

        await save.Should().NotThrowAsync();
        context.Model.FindEntityType(typeof(Note))!.GetNavigations().Should().BeEmpty();
    }

    /// <summary>Saves one note in its own context and returns its id.</summary>
    /// <param name="title">The note title.</param>
    /// <returns>The id of the saved note.</returns>
    private async Task<Guid> SeedAsync(string title)
    {
        var note = new Note(Guid.NewGuid(), title);
        await using var context = CreateContext();
        context.Add(note);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return note.Id;
    }

    /// <summary>Creates a fresh context against the test container.</summary>
    /// <returns>The context; the caller disposes it.</returns>
    private TestDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);
}
