namespace SsSport.Drive.Infrastructure.Drive;

/// <summary>The <c>GoogleDrive</c> configuration section (<c>GoogleDrive__*</c> in <c>.env</c>).</summary>
public sealed record GoogleDriveOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "GoogleDrive";

    /// <summary>Gets the path of the service account's JSON key (S-3). Its content is never logged.</summary>
    public string ServiceAccountKeyPath { get; init; } = string.Empty;

    /// <summary>Gets the id of the agency's Shared Drive; player folders must be inside it.</summary>
    public string SharedDriveId { get; init; } = string.Empty;

    /// <summary>
    /// Gets an optional HTTP proxy for Google calls, e.g. <c>http://127.0.0.1:10808</c> where Google is only reachable
    /// through a VPN. Empty means a direct connection (the VPS).
    /// </summary>
    public string Proxy { get; init; } = string.Empty;
}
