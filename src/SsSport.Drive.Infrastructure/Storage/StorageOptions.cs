namespace SsSport.Drive.Infrastructure.Storage;

/// <summary>The <c>Storage</c> configuration section (<c>Storage__*</c> in <c>.env</c>): the S3-compatible media bucket.</summary>
public sealed record StorageOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Storage";

    /// <summary>Gets the S3 endpoint: RustFS locally (<c>http://localhost:9000</c>), the R2 account endpoint in production.</summary>
    public string ServiceUrl { get; init; } = string.Empty;

    /// <summary>Gets the access key id. Never logged.</summary>
    public string AccessKey { get; init; } = string.Empty;

    /// <summary>Gets the secret access key. Never logged.</summary>
    public string SecretKey { get; init; } = string.Empty;

    /// <summary>Gets the bucket name.</summary>
    public string Bucket { get; init; } = string.Empty;

    /// <summary>Gets the signing region; R2 maps <c>us-east-1</c> to its own <c>auto</c>.</summary>
    public string Region { get; init; } = "us-east-1";
}
