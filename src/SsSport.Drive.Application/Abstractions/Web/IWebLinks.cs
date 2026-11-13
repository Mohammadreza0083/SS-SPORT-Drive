namespace SsSport.Drive.Application.Abstractions.Web;

/// <summary>Builds absolute addresses on the public web app, for emailed links (<c>Web__PublicBaseUrl</c>).</summary>
public interface IWebLinks
{
    /// <summary>Gets the page of a personal link.</summary>
    /// <param name="code">The link code.</param>
    /// <returns>The address of <c>/r/{code}</c>.</returns>
    Uri ViewerLink(string code);

    /// <summary>Gets the admin console's sign-in page with a token.</summary>
    /// <param name="token">The one-time token.</param>
    /// <returns>The address.</returns>
    Uri AdminSignIn(string token);

    /// <summary>Gets the viewer sign-in page with a token.</summary>
    /// <param name="token">The one-time token.</param>
    /// <returns>The address.</returns>
    Uri ViewerSignIn(string token);

    /// <summary>Gets a player's public page (T-6b result card).</summary>
    /// <param name="slug">The player's slug.</param>
    /// <returns>The address of <c>/players/{slug}</c>.</returns>
    Uri PlayerPage(string slug);
}
