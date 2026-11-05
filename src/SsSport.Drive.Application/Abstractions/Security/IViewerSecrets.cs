namespace SsSport.Drive.Application.Abstractions.Security;

/// <summary>
/// The secrets of the email gate (L-2): random device and session tokens kept only as keyed hashes, and 6-digit codes
/// kept only as salted keyed hashes, compared in constant time (decisions §1b). Infrastructure implements it with
/// HMAC-SHA256 and the <c>Viewer__TokenKey</c> pepper.
/// </summary>
public interface IViewerSecrets
{
    /// <summary>Draws a new device or session token: 32 random bytes in base64url.</summary>
    /// <returns>The token; it goes to the browser and is never stored.</returns>
    string NewToken();

    /// <summary>Hashes a token for storage and lookup; the same token always gives the same hash.</summary>
    /// <param name="token">The token.</param>
    /// <returns>The keyed hash.</returns>
    string HashToken(string token);

    /// <summary>Draws a new 6-digit code.</summary>
    /// <returns>The code; it goes into the email and is never stored.</returns>
    string NewCode();

    /// <summary>Hashes a code with a fresh random salt.</summary>
    /// <param name="code">The code.</param>
    /// <returns>The salt and keyed hash, as one text.</returns>
    string HashCode(string code);

    /// <summary>Checks a typed code against a stored hash, in constant time.</summary>
    /// <param name="code">The typed code.</param>
    /// <param name="storedHash">The stored hash, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the code is the one hashed.</returns>
    bool CodeMatches(string code, string? storedHash);
}
