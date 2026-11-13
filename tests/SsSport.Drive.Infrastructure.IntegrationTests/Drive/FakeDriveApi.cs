using System.Globalization;
using SsSport.Drive.Infrastructure.Drive;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Drive;

/// <summary>A stand-in for the Drive API: an in-memory tree with paging.</summary>
internal sealed class FakeDriveApi : IDriveApi
{
    /// <summary>The items by id.</summary>
    private readonly Dictionary<string, DriveItem> _items = [];

    /// <summary>The children of each folder, in insertion order.</summary>
    private readonly Dictionary<string, List<string>> _children = [];

    /// <summary>Gets or sets how many children one page holds.</summary>
    public int PageSize { get; set; } = 1000;

    /// <summary>Gets or sets an exception every call throws, to simulate an outage.</summary>
    public Exception? Failure { get; set; }

    /// <summary>Gets how many pages were listed.</summary>
    public int ListCalls { get; private set; }

    /// <summary>Gets the bytes of each file, by id, for downloads (I-2).</summary>
    public Dictionary<string, byte[]> Contents { get; } = [];

    /// <summary>Downloads a file's bytes.</summary>
    /// <param name="fileId">The Drive file id.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>The bytes, or <see langword="null"/> when there is no such file.</returns>
    public Task<Stream?> DownloadAsync(string fileId, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        return Task.FromResult<Stream?>(Contents.TryGetValue(fileId, out var bytes) ? new MemoryStream(bytes, writable: false) : null);
    }

    /// <summary>Adds an item under a parent.</summary>
    /// <param name="item">The item.</param>
    /// <param name="parent">The parent folder id, or <see langword="null"/> for a top-level item.</param>
    public void Add(DriveItem item, string? parent)
    {
        _items[item.Id] = item;
        if (parent is not null)
        {
            (_children.TryGetValue(parent, out var list) ? list : _children[parent] = []).Add(item.Id);
        }
    }

    /// <inheritdoc/>
    public Task<DriveItem?> GetAsync(string id, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        return Task.FromResult(_items.GetValueOrDefault(id));
    }

    /// <inheritdoc/>
    public Task<DrivePage> ListChildrenAsync(string folderId, string? pageToken, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        ListCalls++;
        var all = _children.GetValueOrDefault(folderId) ?? [];
        var start = pageToken is null ? 0 : int.Parse(pageToken, CultureInfo.InvariantCulture);
        var page = all.Skip(start).Take(PageSize).Select(id => _items[id]).ToList();
        var next = start + PageSize < all.Count ? (start + PageSize).ToString(CultureInfo.InvariantCulture) : null;
        return Task.FromResult(new DrivePage(page, next));
    }
}
