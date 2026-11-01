using System.Security.Cryptography;
using System.Text;

namespace SsSport.Drive.Infrastructure.Persistence;

/// <summary>
/// Builds PostgreSQL's SCRAM-SHA-256 password verifier (RFC 5802 / RFC 7677) on our side, so a role's password is
/// sent to the server already salted and hashed: the plain password never travels in SQL text or reaches a log.
/// </summary>
internal static class ScramSha256
{
    /// <summary>PBKDF2 iterations; PostgreSQL's default (<c>scram_iterations</c>).</summary>
    private const int Iterations = 4096;

    /// <summary>Length of the random salt in bytes, as PostgreSQL uses.</summary>
    private const int SaltLength = 16;

    /// <summary>Creates a verifier with a fresh random salt.</summary>
    /// <param name="password">The password; ASCII only, so no SASLprep normalisation is needed.</param>
    /// <returns><c>SCRAM-SHA-256$&lt;iterations&gt;:&lt;salt&gt;$&lt;StoredKey&gt;:&lt;ServerKey&gt;</c>, all Base64.</returns>
    public static string CreateVerifier(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var salted = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, SHA256.HashSizeInBytes);
        var clientKey = HMACSHA256.HashData(salted, "Client Key"u8);
        var storedKey = SHA256.HashData(clientKey);
        var serverKey = HMACSHA256.HashData(salted, "Server Key"u8);
        CryptographicOperations.ZeroMemory(salted);

        return $"SCRAM-SHA-256${Iterations}:{Convert.ToBase64String(salt)}${Convert.ToBase64String(storedKey)}:{Convert.ToBase64String(serverKey)}";
    }
}
