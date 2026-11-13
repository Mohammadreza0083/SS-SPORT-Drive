using System.Net;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using Microsoft.Extensions.Options;

namespace SsSport.Drive.Infrastructure.Drive;

/// <summary>
/// <see cref="IDriveApi"/> over the Google Drive API v3 with the service account, read-only scope, Shared Drive aware,
/// and through <see cref="GoogleDriveOptions.Proxy"/> where Google needs a VPN. The client is created on first use,
/// so hosts without Drive settings still start.
/// </summary>
public sealed class GoogleDriveApi : IDriveApi, IDisposable
{
    /// <summary>The fields read for every item.</summary>
    private const string ItemFields = "id,name,mimeType,size,driveId,trashed,md5Checksum,modifiedTime";

    /// <summary>The settings.</summary>
    private readonly GoogleDriveOptions _options;

    /// <summary>The Drive client, created on first use.</summary>
    private readonly Lazy<DriveService> _service;

    /// <summary>Initializes the API.</summary>
    /// <param name="options">The Drive settings.</param>
    public GoogleDriveApi(IOptions<GoogleDriveOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _service = new Lazy<DriveService>(CreateService);
    }

    /// <inheritdoc/>
    public async Task<DriveItem?> GetAsync(string id, CancellationToken cancellationToken)
    {
        var request = _service.Value.Files.Get(id);
        request.SupportsAllDrives = true;
        request.Fields = ItemFields;
        try
        {
            return ToItem(await request.ExecuteAsync(cancellationToken));
        }
        catch (GoogleApiException exception) when (exception.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<DrivePage> ListChildrenAsync(string folderId, string? pageToken, CancellationToken cancellationToken)
    {
        var request = _service.Value.Files.List();
        request.Q = $"'{folderId.Replace("'", "\\'", StringComparison.Ordinal)}' in parents and trashed = false";
        request.Corpora = "drive";
        request.DriveId = _options.SharedDriveId;
        request.SupportsAllDrives = true;
        request.IncludeItemsFromAllDrives = true;
        request.PageSize = 1000;
        request.PageToken = pageToken;
        request.Fields = $"nextPageToken, files({ItemFields})";
        var result = await request.ExecuteAsync(cancellationToken);
        return new DrivePage([.. (result.Files ?? []).Select(ToItem)], result.NextPageToken);
    }

    /// <inheritdoc/>
    public async Task<Stream?> DownloadAsync(string fileId, CancellationToken cancellationToken)
    {
        var request = _service.Value.Files.Get(fileId);
        request.SupportsAllDrives = true;
        try
        {
            return await request.ExecuteAsStreamAsync(cancellationToken);
        }
        catch (GoogleApiException exception) when (exception.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>Disposes the client if it was created.</summary>
    public void Dispose()
    {
        if (_service.IsValueCreated)
        {
            _service.Value.Dispose();
        }
    }

    /// <summary>Maps a Drive API file.</summary>
    /// <param name="file">The file.</param>
    /// <returns>The item.</returns>
    private static DriveItem ToItem(Google.Apis.Drive.v3.Data.File file) =>
        new(
            file.Id,
            file.Name ?? string.Empty,
            file.MimeType ?? string.Empty,
            file.Size,
            file.DriveId,
            file.Trashed ?? false,
            file.Md5Checksum,
            file.ModifiedTimeDateTimeOffset);

    /// <summary>Builds the Drive client from the service account key, read-only, through the proxy when set.</summary>
    /// <returns>The client.</returns>
    /// <exception cref="DriveConfigurationException">The key path is missing or the key cannot be read.</exception>
    private DriveService CreateService()
    {
        if (string.IsNullOrWhiteSpace(_options.ServiceAccountKeyPath) || !File.Exists(_options.ServiceAccountKeyPath))
        {
            throw new DriveConfigurationException("GoogleDrive__ServiceAccountKeyPath must point to the service account's JSON key.");
        }

        var httpClientFactory = new ProxyHttpClientFactory(_options.Proxy);
        var credential = CredentialFactory.FromFile<ServiceAccountCredential>(_options.ServiceAccountKeyPath)
            .ToGoogleCredential()
            .CreateScoped(DriveService.Scope.DriveReadonly)
            .CreateWithHttpClientFactory(httpClientFactory);
        var service = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            HttpClientFactory = httpClientFactory,
            ApplicationName = "SS-SPORT Drive",
        });
        service.HttpClient.Timeout = TimeSpan.FromSeconds(20);
        return service;
    }

    /// <summary>Google's HTTP client factory, sending every call (token requests included) through an optional proxy.</summary>
    /// <param name="proxy">The proxy URL, or empty for a direct connection.</param>
    private sealed class ProxyHttpClientFactory(string proxy) : HttpClientFactory
    {
        /// <summary>Builds the handler with the proxy.</summary>
        /// <param name="args">The client arguments.</param>
        /// <returns>The handler.</returns>
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args)
        {
            var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
            if (proxy.Length > 0)
            {
                handler.Proxy = new WebProxy(new Uri(proxy));
                handler.UseProxy = true;
            }

            return handler;
        }
    }
}

/// <summary>The Drive settings are incomplete, so Drive cannot be used; reported as an outage.</summary>
public sealed class DriveConfigurationException : Exception
{
    /// <summary>Initializes the exception.</summary>
    /// <param name="message">What is missing.</param>
    public DriveConfigurationException(string message)
        : base(message)
    {
    }
}
