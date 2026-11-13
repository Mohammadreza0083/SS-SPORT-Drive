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

    /// <summary>Gets the home page with the search filled in, which a player's QR code opens (I-8).</summary>
    /// <param name="name">The player's name.</param>
    /// <returns>The address of <c>/?q={name}</c>.</returns>
    Uri PlayerSearch(string name);
}
