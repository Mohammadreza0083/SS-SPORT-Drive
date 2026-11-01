namespace SsSport.Drive.Api;

/// <summary>
/// Reads a list setting given either as an array (<c>Key__0</c>, <c>Key__1</c>, ...) or as one comma-separated value
/// (<c>Key=a,b</c>), the two shapes a <c>.env</c> file can hold.
/// </summary>
public static class ConfigurationList
{
    /// <summary>Reads the list at <paramref name="key"/>.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="key">The setting's key.</param>
    /// <returns>The trimmed, non-empty entries; empty when the setting is missing.</returns>
    public static string[] Read(IConfiguration configuration, string key)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(key);
        var values = section.Value is { } single
            ? single.Split(',')
            : section.GetChildren().Select(child => child.Value ?? string.Empty);

        return values.Select(entry => entry.Trim()).Where(entry => entry.Length > 0).ToArray();
    }
}
