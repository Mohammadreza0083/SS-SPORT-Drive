using Microsoft.Extensions.Options;
using SsSport.Drive.Application.Abstractions.Web;

namespace SsSport.Drive.Infrastructure.Web;

/// <summary>The <c>Web</c> configuration section: where the public web app lives, for emailed links.</summary>
public sealed record WebOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Web";

    /// <summary>Gets the web app's public address, such as <c>https://drive.ss-sport.co</c> (<c>Web__PublicBaseUrl</c>).</summary>
    public string PublicBaseUrl { get; init; } = string.Empty;

    /// <summary>Tells whether the address is empty (links are off) or an absolute http(s) address.</summary>
    /// <returns><see langword="true"/> when the setting can be used.</returns>
    public bool IsValid() =>
        PublicBaseUrl.Length == 0
        || (Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https");
}

/// <summary>Builds absolute addresses on the public web app from <c>Web__PublicBaseUrl</c>.</summary>
/// <param name="options">The web settings.</param>
public sealed class WebLinks(IOptions<WebOptions> options) : IWebLinks
{
    /// <inheritdoc />
    public Uri ViewerLink(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return Build($"/r/{Uri.EscapeDataString(code)}");
    }

    /// <inheritdoc />
    public Uri AdminSignIn(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return Build($"/admin/sign-in?token={Uri.EscapeDataString(token)}");
    }

    /// <inheritdoc />
    public Uri ViewerSignIn(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return Build($"/sign-in?token={Uri.EscapeDataString(token)}");
    }

    /// <inheritdoc />
    public Uri PlayerPage(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        return Build($"/players/{Uri.EscapeDataString(slug)}");
    }

    /// <summary>Joins the public address and a path.</summary>
    /// <param name="pathAndQuery">The path and query, starting with a slash.</param>
    /// <returns>The absolute address.</returns>
    /// <exception cref="InvalidOperationException">The public address is not set.</exception>
    private Uri Build(string pathAndQuery)
    {
        var baseUrl = options.Value.PublicBaseUrl.TrimEnd('/');
        if (baseUrl.Length == 0)
        {
            throw new InvalidOperationException("Web__PublicBaseUrl is not set, so emailed links cannot be built.");
        }

        return new Uri(baseUrl + pathAndQuery, UriKind.Absolute);
    }
}
