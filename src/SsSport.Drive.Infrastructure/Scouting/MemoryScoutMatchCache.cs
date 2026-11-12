using Microsoft.Extensions.Caching.Memory;
using SsSport.Drive.Application.Links;
using SsSport.Drive.Application.Scouting;

namespace SsSport.Drive.Infrastructure.Scouting;

/// <summary>
/// Keeps parsed scout files in the process's memory for ten minutes (V-8). The key holds the asset's content version, so
/// a replaced file is read again. A file that cannot be parsed is not cached.
/// </summary>
/// <param name="cache">The memory cache.</param>
public sealed class MemoryScoutMatchCache(IMemoryCache cache) : IScoutMatchCache
{
    /// <summary>How long a parsed match is kept.</summary>
    private static readonly TimeSpan _lifetime = TimeSpan.FromMinutes(10);

    /// <inheritdoc />
    public async Task<ScoutMatch> GetOrAddAsync(string key, Func<Task<ScoutMatch>> load)
    {
        ArgumentNullException.ThrowIfNull(load);
        if (cache.TryGetValue(key, out ScoutMatch? cached) && cached is not null)
        {
            return cached;
        }

        var match = await load();
        cache.Set(key, match, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = _lifetime });
        return match;
    }
}
