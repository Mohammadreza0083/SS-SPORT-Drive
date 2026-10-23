using SsSport.Drive.Infrastructure.Persistence;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// The generic repository evaluates specifications (criteria, ordering, paging) in PostgreSQL,
/// so handlers express query conditions as specifications instead of ad-hoc <c>Where</c> chains.
/// </summary>
/// <param name="database">The shared PostgreSQL container.</param>
public sealed class RepositorySpecificationTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    /// <summary>A prefix unique to this test instance, so tests sharing the container never see each other's rows.</summary>
    private readonly string _prefix = $"{Guid.NewGuid():N}-";

    /// <summary>Only matching aggregates are returned, in the specification's order.</summary>
    [Fact]
    public async Task List_applies_criteria_and_ordering()
    {
        await SeedAsync("c", "a", "b");
        await SeedOtherAsync("a");

        var notes = await Repository().ListAsync(new NotesByTitlePrefixSpec(_prefix), TestContext.Current.CancellationToken);

        notes.Select(note => note.Title).Should().Equal($"{_prefix}a", $"{_prefix}b", $"{_prefix}c");
    }

    /// <summary>Skip and take from the specification are applied in the database.</summary>
    [Fact]
    public async Task List_applies_paging()
    {
        await SeedAsync("a", "b", "c", "d");

        var page = await Repository().ListAsync(new NotesPageSpec(_prefix, skip: 1, take: 2), TestContext.Current.CancellationToken);

        page.Select(note => note.Title).Should().Equal($"{_prefix}b", $"{_prefix}c");
    }

    /// <summary>The first match in the specification's order is returned.</summary>
    [Fact]
    public async Task First_or_default_returns_the_first_match()
    {
        await SeedAsync("b", "a");

        var first = await Repository().FirstOrDefaultAsync(new NotesByTitlePrefixSpec(_prefix), TestContext.Current.CancellationToken);

        first!.Title.Should().Be($"{_prefix}a");
    }

    /// <summary>With no match, null is returned instead of throwing.</summary>
    [Fact]
    public async Task First_or_default_returns_null_without_a_match()
    {
        var first = await Repository().FirstOrDefaultAsync(new NotesByTitlePrefixSpec(_prefix), TestContext.Current.CancellationToken);

        first.Should().BeNull();
    }

    /// <summary>Count and any use the specification's criteria but ignore its paging.</summary>
    [Fact]
    public async Task Count_and_any_ignore_paging()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SeedAsync("a", "b", "c");
        var onePage = new NotesPageSpec(_prefix, skip: 0, take: 1);

        (await Repository().CountAsync(onePage, cancellationToken)).Should().Be(3);
        (await Repository().AnyAsync(onePage, cancellationToken)).Should().BeTrue();
        (await Repository().AnyAsync(new NotesByTitlePrefixSpec($"{_prefix}missing"), cancellationToken)).Should().BeFalse();
    }

    /// <summary>Aggregates loaded through a specification are tracked, so edits are saved by the unit of work.</summary>
    [Fact]
    public async Task Aggregates_loaded_by_specification_are_tracked()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SeedAsync("a");

        await using (var context = database.CreateContext())
        {
            var note = await new Repository<Note, Guid>(context).FirstOrDefaultAsync(new NotesByTitlePrefixSpec(_prefix), cancellationToken);
            note!.Rename($"{_prefix}renamed");
            await new UnitOfWork(context, new RecordingDomainEventDispatcher()).SaveChangesAsync(cancellationToken);
        }

        var titles = (await Repository().ListAsync(new NotesByTitlePrefixSpec(_prefix), cancellationToken)).Select(note => note.Title);
        titles.Should().Equal($"{_prefix}renamed");
    }

    /// <summary>Creates a repository on a fresh context; contexts here are short-lived and read-only.</summary>
    /// <returns>The repository.</returns>
    private Repository<Note, Guid> Repository() => new(database.CreateContext());

    /// <summary>Saves notes titled with this test's prefix.</summary>
    /// <param name="suffixes">The title suffixes.</param>
    /// <returns>A task that completes when the notes are saved.</returns>
    private Task SeedAsync(params string[] suffixes) => SaveAsync(suffixes.Select(suffix => $"{_prefix}{suffix}"));

    /// <summary>Saves notes that belong to a different prefix and must never match this test's specifications.</summary>
    /// <param name="suffixes">The title suffixes.</param>
    /// <returns>A task that completes when the notes are saved.</returns>
    private Task SeedOtherAsync(params string[] suffixes) => SaveAsync(suffixes.Select(suffix => $"other-{_prefix}{suffix}"));

    /// <summary>Saves one note per title in a single unit of work.</summary>
    /// <param name="titles">The note titles.</param>
    /// <returns>A task that completes when the notes are saved.</returns>
    private async Task SaveAsync(IEnumerable<string> titles)
    {
        await using var context = database.CreateContext();
        var repository = new Repository<Note, Guid>(context);
        foreach (var title in titles)
        {
            repository.Add(new Note(Guid.NewGuid(), title));
        }

        await new UnitOfWork(context, new RecordingDomainEventDispatcher()).SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
