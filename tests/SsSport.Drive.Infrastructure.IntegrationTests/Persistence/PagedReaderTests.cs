using Ardalis.Specification;
using SsSport.Drive.Application.Exceptions;
using SsSport.Drive.Application.Paging;
using SsSport.Drive.Infrastructure.Persistence;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Persistence;

/// <summary>Notes whose title starts with a prefix, without any ordering (cursor lists order by their own key).</summary>
internal sealed class NotesWithPrefixSpec : Specification<Note>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="prefix">The title prefix to match.</param>
    public NotesWithPrefixSpec(string prefix) => Query.Where(note => note.Title.StartsWith(prefix));
}

/// <summary>
/// The paged reader returns one page at a time from PostgreSQL: the count and the page are computed in SQL,
/// the order is stable (the id breaks ties), nothing is tracked, and cursors walk every row exactly once.
/// </summary>
/// <param name="database">The shared PostgreSQL container.</param>
public sealed class PagedReaderTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    /// <summary>A prefix unique to this test instance, so tests sharing the container never see each other's rows.</summary>
    private readonly string _prefix = $"{Guid.NewGuid():N}-";

    /// <summary>45 rows in pages of 20 are 20, 20 and 5, in order, with the total on every page.</summary>
    [Fact]
    public async Task Offset_pages_split_the_list_in_order()
    {
        var titles = await SeedAsync(Enumerable.Range(0, 45).Select(index => $"{index:D2}"));

        var pages = new List<PagedResult<string>>();
        for (var page = 1; page <= 3; page++)
        {
            pages.Add(await PageAsync(new PageRequest(page, 20)));
        }

        pages.Select(page => page.Items.Count).Should().Equal(20, 20, 5);
        pages.SelectMany(page => page.Items).Should().Equal(titles.Order(StringComparer.Ordinal));
        pages.Should().AllSatisfy(page =>
        {
            page.TotalCount.Should().Be(45);
            page.TotalPages.Should().Be(3);
        });
    }

    /// <summary>A page after the last one is empty but still reports the total.</summary>
    [Fact]
    public async Task Page_after_the_last_is_empty()
    {
        await SeedAsync("a", "b", "c");

        var page = await PageAsync(new PageRequest(5, 2));

        page.Items.Should().BeEmpty();
        page.TotalCount.Should().Be(3);
        page.TotalPages.Should().Be(2);
    }

    /// <summary>With equal sort values, the id breaks ties, so no row repeats or goes missing across pages.</summary>
    [Fact]
    public async Task Equal_sort_values_never_repeat_or_vanish()
    {
        var ids = await SeedSameTitleAsync(30);

        var seen = new List<Guid>();
        for (var page = 1; page <= 5; page++)
        {
            await using var context = database.CreateContext();
            var result = await new PagedReader(context).PageAsync(
                new NotesByTitlePrefixSpec(_prefix), new PageRequest(page, 7), note => note.Id, TestContext.Current.CancellationToken);
            seen.AddRange(result.Items);
        }

        seen.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(ids);
    }

    /// <summary>Rows read for a page are not tracked: the read side never writes.</summary>
    [Fact]
    public async Task Pages_are_read_without_tracking()
    {
        await SeedAsync("a", "b");
        await using var context = database.CreateContext();

        await new PagedReader(context).PageAsync(new NotesByTitlePrefixSpec(_prefix), new PageRequest(), note => note.Title, TestContext.Current.CancellationToken);

        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    /// <summary>An unordered offset list would page unpredictably, so it is refused.</summary>
    [Fact]
    public async Task Offset_list_without_an_order_is_refused()
    {
        await using var context = database.CreateContext();

        var read = () => new PagedReader(context).PageAsync(new NotesWithPrefixSpec(_prefix), new PageRequest(), note => note.Title, TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>Paging belongs to the request, not the specification, so a specification with its own paging is refused.</summary>
    [Fact]
    public async Task Specification_with_its_own_paging_is_refused()
    {
        await using var context = database.CreateContext();

        var read = () => new PagedReader(context).PageAsync(new NotesPageSpec(_prefix, 0, 5), new PageRequest(), note => note.Title, TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>A newest-first cursor walk returns 10, 10 and 5 rows, in order, and ends with no next cursor.</summary>
    [Fact]
    public async Task Cursor_walks_descending_to_the_end()
    {
        var titles = await SeedAsync(Enumerable.Range(0, 25).Select(index => $"{index:D2}"));

        var pages = await WalkAsync(CursorDirection.Descending, pageSize: 10);

        pages.Select(page => page.Items.Count).Should().Equal(10, 10, 5);
        pages.SelectMany(page => page.Items).Should().Equal(titles.OrderDescending(StringComparer.Ordinal));
        pages[^1].NextCursor.Should().BeNull();
        pages[..^1].Should().AllSatisfy(page => page.NextCursor.Should().NotBeNull());
    }

    /// <summary>An ascending cursor walk returns every row in ascending order.</summary>
    [Fact]
    public async Task Cursor_walks_ascending()
    {
        var titles = await SeedAsync("c", "a", "d", "b", "e");

        var pages = await WalkAsync(CursorDirection.Ascending, pageSize: 2);

        pages.SelectMany(page => page.Items).Should().Equal(titles.Order(StringComparer.Ordinal));
    }

    /// <summary>A list that fits on one page has no next cursor.</summary>
    [Fact]
    public async Task Exactly_one_full_page_has_no_next_cursor()
    {
        await SeedAsync("a", "b", "c");

        var pages = await WalkAsync(CursorDirection.Descending, pageSize: 3);

        pages.Should().ContainSingle().Which.NextCursor.Should().BeNull();
    }

    /// <summary>With equal sort keys, the cursor still visits every row exactly once.</summary>
    [Fact]
    public async Task Cursor_with_equal_keys_visits_every_row_once()
    {
        var ids = await SeedSameTitleAsync(10);

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            await using var context = database.CreateContext();
            var page = await new PagedReader(context).CursorPageAsync(
                new NotesWithPrefixSpec(_prefix), note => note.Title, CursorDirection.Descending,
                new CursorRequest(cursor, 4), note => note.Id, TestContext.Current.CancellationToken);
            seen.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        seen.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(ids);
    }

    /// <summary>A cursor issued for another list's key type is a validation error under <c>cursor</c>.</summary>
    [Fact]
    public async Task Cursor_for_another_key_type_is_a_validation_error()
    {
        await using var context = database.CreateContext();
        var foreign = CursorToken.Encode(DateTimeOffset.UnixEpoch, Guid.CreateVersion7());

        var read = () => new PagedReader(context).CursorPageAsync(
            new NotesWithPrefixSpec(_prefix), note => note.Title, CursorDirection.Descending,
            new CursorRequest(foreign), note => note.Title, TestContext.Current.CancellationToken);

        var failure = await read.Should().ThrowAsync<RequestValidationException>();
        failure.Which.Errors.Keys.Should().Equal("cursor");
    }

    /// <summary>A cursor list orders by its key, so a specification with its own order is refused.</summary>
    [Fact]
    public async Task Cursor_list_with_its_own_order_is_refused()
    {
        await using var context = database.CreateContext();

        var read = () => new PagedReader(context).CursorPageAsync(
            new NotesByTitlePrefixSpec(_prefix), note => note.Title, CursorDirection.Descending,
            new CursorRequest(), note => note.Title, TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>Reads one offset page of titles, ordered by title.</summary>
    /// <param name="request">The page to read.</param>
    /// <returns>The page.</returns>
    private async Task<PagedResult<string>> PageAsync(PageRequest request)
    {
        await using var context = database.CreateContext();
        return await new PagedReader(context).PageAsync(new NotesByTitlePrefixSpec(_prefix), request, note => note.Title, TestContext.Current.CancellationToken);
    }

    /// <summary>Follows the cursor from the first page to the last.</summary>
    /// <param name="direction">The order to walk in.</param>
    /// <param name="pageSize">Rows per page.</param>
    /// <returns>Every page, in order.</returns>
    private async Task<List<CursorResult<string>>> WalkAsync(CursorDirection direction, int pageSize)
    {
        var pages = new List<CursorResult<string>>();
        string? cursor = null;
        do
        {
            await using var context = database.CreateContext();
            var page = await new PagedReader(context).CursorPageAsync(
                new NotesWithPrefixSpec(_prefix), note => note.Title, direction,
                new CursorRequest(cursor, pageSize), note => note.Title, TestContext.Current.CancellationToken);
            pages.Add(page);
            cursor = page.NextCursor;
        }
        while (cursor is not null && pages.Count < 100);

        return pages;
    }

    /// <summary>Saves notes titled with this test's prefix.</summary>
    /// <param name="suffixes">The title suffixes.</param>
    /// <returns>The saved titles.</returns>
    private async Task<IReadOnlyList<string>> SeedAsync(params IEnumerable<string> suffixes)
    {
        var titles = suffixes.Select(suffix => $"{_prefix}{suffix}").ToList();
        await SaveAsync(titles.Select(title => new Note(Guid.CreateVersion7(), title)));
        return titles;
    }

    /// <summary>Saves notes that all have the same title.</summary>
    /// <param name="count">How many notes to save.</param>
    /// <returns>Their ids.</returns>
    private async Task<IReadOnlyList<Guid>> SeedSameTitleAsync(int count)
    {
        var notes = Enumerable.Range(0, count).Select(_ => new Note(Guid.NewGuid(), $"{_prefix}same")).ToList();
        await SaveAsync(notes);
        return [.. notes.Select(note => note.Id)];
    }

    /// <summary>Saves notes in a single unit of work.</summary>
    /// <param name="notes">The notes.</param>
    /// <returns>A task that completes when the notes are saved.</returns>
    private async Task SaveAsync(IEnumerable<Note> notes)
    {
        await using var context = database.CreateContext();
        var repository = new Repository<Note, Guid>(context);
        foreach (var note in notes)
        {
            repository.Add(note);
        }

        await new UnitOfWork(context, new RecordingDomainEventDispatcher()).SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
