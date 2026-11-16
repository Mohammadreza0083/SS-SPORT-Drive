using Ardalis.Specification;
using SsSport.Drive.Application.Abstractions.Authentication;
using SsSport.Drive.Application.Abstractions.Drive;
using SsSport.Drive.Application.Abstractions.Ingest;
using SsSport.Drive.Application.Abstractions.Persistence;
using SsSport.Drive.Domain.Assets;
using SsSport.Drive.Domain.Primitives;

namespace SsSport.Drive.Application.UnitTests.Archive;

/// <summary>A repository over a list that runs the real specifications in memory, so handlers are tested with their own queries.</summary>
/// <typeparam name="T">The aggregate.</typeparam>
internal sealed class InMemoryRepository<T> : IRepository<T, Guid>
    where T : AggregateRoot<Guid>
{
    /// <summary>The stored aggregates.</summary>
    public List<T> Items { get; } = [];

    /// <summary>Gets the aggregates added through the repository (not the seeded ones).</summary>
    public List<T> Added { get; } = [];

    /// <summary>Seeds aggregates as if they were saved already.</summary>
    /// <param name="items">The aggregates.</param>
    /// <returns>The same repository.</returns>
    public InMemoryRepository<T> Seed(params T[] items)
    {
        Items.AddRange(items);
        return this;
    }

    /// <inheritdoc/>
    public Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Items.FirstOrDefault(item => item.Id == id));

    /// <inheritdoc/>
    public Task<T?> FirstOrDefaultAsync(ISpecification<T> specification, CancellationToken cancellationToken = default) =>
        Task.FromResult(SqlLike.Evaluate(specification, Items).FirstOrDefault());

    /// <inheritdoc/>
    public Task<IReadOnlyList<T>> ListAsync(ISpecification<T> specification, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<T>>(SqlLike.Evaluate(specification, Items).ToList());

    /// <inheritdoc/>
    public Task<int> CountAsync(ISpecification<T> specification, CancellationToken cancellationToken = default) =>
        Task.FromResult(SqlLike.Evaluate(specification, Items).Count());

    /// <inheritdoc/>
    public Task<bool> AnyAsync(ISpecification<T> specification, CancellationToken cancellationToken = default) =>
        Task.FromResult(SqlLike.Evaluate(specification, Items).Any());

    /// <inheritdoc/>
    public void Add(T aggregate)
    {
        Items.Add(aggregate);
        Added.Add(aggregate);
    }

    /// <inheritdoc/>
    public void Remove(T aggregate) => Items.Remove(aggregate);

    /// <inheritdoc/>
    public Task<int> DeleteAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        var doomed = SqlLike.Evaluate(specification, Items).ToList();
        doomed.ForEach(item => Items.Remove(item));
        return Task.FromResult(doomed.Count);
    }
}

/// <summary>A Shared Drive in memory: folders with child folders and loose files, and player folders with their listings.</summary>
internal sealed class FakeArchiveDrive : IDriveArchiveBrowser, IDriveFolderLister
{
    /// <summary>The root's children.</summary>
    private readonly DriveChildrenBuilder _root = new();

    /// <summary>Each folder's children, by id.</summary>
    private readonly Dictionary<string, DriveChildrenBuilder> _folders = [];

    /// <summary>Each player folder's listing, by id.</summary>
    private readonly Dictionary<string, DriveFolderListing> _listings = [];

    /// <summary>Gets or sets a value indicating whether every call fails as a Drive outage.</summary>
    public bool Down { get; set; }

    /// <summary>Gets the player folders listed so far.</summary>
    public List<string> Listed { get; } = [];

    /// <summary>Adds a top-level folder.</summary>
    /// <param name="id">The id.</param>
    /// <param name="name">The name.</param>
    /// <returns>The same drive.</returns>
    public FakeArchiveDrive TopFolder(string id, string name)
    {
        _root.Folders.Add(new DriveSubfolder(id, name));
        _folders[id] = new DriveChildrenBuilder();
        return this;
    }

    /// <summary>Adds loose files at the root.</summary>
    /// <param name="count">How many.</param>
    /// <returns>The same drive.</returns>
    public FakeArchiveDrive LooseRootFiles(int count)
    {
        _root.Files += count;
        return this;
    }

    /// <summary>Adds loose files in a category folder.</summary>
    /// <param name="categoryId">The category folder.</param>
    /// <param name="count">How many.</param>
    /// <returns>The same drive.</returns>
    public FakeArchiveDrive LooseFiles(string categoryId, int count)
    {
        _folders[categoryId].Files += count;
        return this;
    }

    /// <summary>Adds a player folder under a category folder.</summary>
    /// <param name="categoryId">The category folder.</param>
    /// <param name="id">The player folder id.</param>
    /// <param name="name">The player folder name.</param>
    /// <param name="files">Its files.</param>
    /// <param name="skipped">Files the listing skipped.</param>
    /// <param name="truncated">Whether the listing hit its limit.</param>
    /// <returns>The same drive.</returns>
    public FakeArchiveDrive PlayerFolder(string categoryId, string id, string name, IReadOnlyList<DriveFileEntry> files, int skipped = 0, bool truncated = false)
    {
        _folders[categoryId].Folders.Add(new DriveSubfolder(id, name));
        _listings[id] = new DriveFolderListing(DriveFolderStatus.Found, name, files, skipped, truncated);
        return this;
    }

    /// <inheritdoc/>
    public Task<DriveChildren> ListRootAsync(CancellationToken cancellationToken = default) => Answer(_root.Build());

    /// <inheritdoc/>
    public Task<DriveChildren> ListFolderAsync(string folderId, CancellationToken cancellationToken = default) =>
        Answer(_folders.TryGetValue(folderId, out var children) ? children.Build() : new DriveChildren([], 0));

    /// <inheritdoc/>
    public Task<DriveFolderListing> ListAsync(string folderId, CancellationToken cancellationToken = default)
    {
        Listed.Add(folderId);
        return Answer(_listings.TryGetValue(folderId, out var listing) ? listing : DriveFolderListing.Missing(DriveFolderStatus.NotFound));
    }

    /// <summary>Answers, or fails as an outage.</summary>
    /// <typeparam name="TResult">The answer.</typeparam>
    /// <param name="result">The answer.</param>
    /// <returns>The task.</returns>
    private Task<TResult> Answer<TResult>(TResult result) =>
        Down ? Task.FromException<TResult>(new Exceptions.ServiceUnavailableException("drive.unavailable", "Google Drive cannot be reached right now.")) : Task.FromResult(result);

    /// <summary>Collects a folder's children.</summary>
    private sealed class DriveChildrenBuilder
    {
        /// <summary>Gets the child folders.</summary>
        public List<DriveSubfolder> Folders { get; } = [];

        /// <summary>Gets or sets the number of loose files.</summary>
        public int Files { get; set; }

        /// <summary>Builds the answer.</summary>
        /// <returns>The children.</returns>
        public DriveChildren Build() => new(Folders.ToList(), Files);
    }
}

/// <summary>The backup bucket in memory: the md5s it holds.</summary>
/// <param name="md5s">The md5s.</param>
internal sealed class FakeBackup(params string[] md5s) : IBackupArchive
{
    /// <inheritdoc/>
    public Task<string?> FindByMd5Async(string md5, CancellationToken cancellationToken) =>
        Task.FromResult(md5s.Contains(md5) ? $"backup/{md5}" : null);

    /// <inheritdoc/>
    public Task CopyToMediaAsync(string backupKey, string mediaKey, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>A signed-in admin.</summary>
/// <param name="id">The admin's id.</param>
internal sealed class AdminUser(Guid id) : ICurrentUser
{
    /// <inheritdoc/>
    public bool IsAuthenticated => true;

    /// <inheritdoc/>
    public string? UserId => id.ToString();

    /// <inheritdoc/>
    public bool IsInRole(string role) => role == "Admin";
}

/// <summary>Builds Drive file entries.</summary>
internal static class ArchiveFiles
{
    /// <summary>Builds a file entry.</summary>
    /// <param name="name">The file name.</param>
    /// <param name="md5">Its md5, or <see langword="null"/>.</param>
    /// <param name="size">Its size.</param>
    /// <returns>The entry.</returns>
    public static DriveFileEntry File(string name, string? md5, long size = 1000)
    {
        AssetFile.TryDetectKind(name, out var kind);
        return new DriveFileEntry($"f-{Guid.NewGuid():N}", name, kind, size, md5, null, ["Highlights"]);
    }
}

/// <summary>
/// Wraps an in-memory repository so that queries see only what was saved before the request, as EF Core does: an
/// aggregate added in this request is invisible to queries until the transaction commits.
/// </summary>
/// <typeparam name="T">The aggregate.</typeparam>
/// <param name="inner">The repository that stores the aggregates.</param>
internal sealed class UnsavedBlind<T>(InMemoryRepository<T> inner) : IRepository<T, Guid>
    where T : AggregateRoot<Guid>
{
    /// <summary>Gets what was saved before the request.</summary>
    private List<T> Saved => inner.Items.Except(inner.Added).ToList();

    /// <inheritdoc/>
    public Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => inner.GetByIdAsync(id, cancellationToken);

    /// <inheritdoc/>
    public Task<T?> FirstOrDefaultAsync(ISpecification<T> specification, CancellationToken cancellationToken = default) =>
        Task.FromResult(SqlLike.Evaluate(specification, Saved).FirstOrDefault());

    /// <inheritdoc/>
    public Task<IReadOnlyList<T>> ListAsync(ISpecification<T> specification, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<T>>(SqlLike.Evaluate(specification, Saved).ToList());

    /// <inheritdoc/>
    public Task<int> CountAsync(ISpecification<T> specification, CancellationToken cancellationToken = default) =>
        Task.FromResult(SqlLike.Evaluate(specification, Saved).Count());

    /// <inheritdoc/>
    public Task<bool> AnyAsync(ISpecification<T> specification, CancellationToken cancellationToken = default) =>
        Task.FromResult(SqlLike.Evaluate(specification, Saved).Any());

    /// <inheritdoc/>
    public void Add(T aggregate) => inner.Add(aggregate);

    /// <inheritdoc/>
    public void Remove(T aggregate) => inner.Remove(aggregate);

    /// <inheritdoc/>
    public Task<int> DeleteAsync(ISpecification<T> specification, CancellationToken cancellationToken = default) => inner.DeleteAsync(specification, cancellationToken);
}

/// <summary>Runs a specification over a list the way SQL would: a condition that reaches through a missing value is false, not an error.</summary>
internal static class SqlLike
{
    /// <summary>Filters with null-safe criteria, then applies the specification's order and paging.</summary>
    /// <typeparam name="T">The entity.</typeparam>
    /// <param name="specification">The specification.</param>
    /// <param name="items">The items.</param>
    /// <returns>The result.</returns>
    public static IEnumerable<T> Evaluate<T>(ISpecification<T> specification, IEnumerable<T> items) =>
        specification.Evaluate(items.Where(item => Matches(specification, item)).ToList());

    /// <summary>Tests one item; a <see cref="NullReferenceException"/> counts as no match, like NULL in SQL.</summary>
    /// <typeparam name="T">The entity.</typeparam>
    /// <param name="specification">The specification.</param>
    /// <param name="item">The item.</param>
    /// <returns>Whether the item matches.</returns>
    private static bool Matches<T>(ISpecification<T> specification, T item)
    {
        try
        {
            return specification.IsSatisfiedBy(item);
        }
        catch (NullReferenceException)
        {
            return false;
        }
    }
}
