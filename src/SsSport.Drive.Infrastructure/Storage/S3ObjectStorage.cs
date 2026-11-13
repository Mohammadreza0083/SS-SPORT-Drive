using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Microsoft.Extensions.Options;
using SsSport.Drive.Application.Abstractions.Storage;

namespace SsSport.Drive.Infrastructure.Storage;

/// <summary>
/// <see cref="IObjectStorage"/> over the S3 API (AWSSDK.S3), for RustFS locally and Cloudflare R2 in production.
/// The client is created on first use, so hosts without storage settings still start.
/// </summary>
public sealed class S3ObjectStorage : IObjectStorage, IDisposable
{
    /// <summary>The size of one part of a large upload: 16 MB.</summary>
    private const long MultipartPartSize = 16 * 1024 * 1024;

    /// <summary>The settings.</summary>
    private readonly StorageOptions _options;

    /// <summary>The S3 client, created on first use.</summary>
    private readonly Lazy<AmazonS3Client> _client;

    /// <summary>Initializes the adapter.</summary>
    /// <param name="options">The storage settings.</param>
    public S3ObjectStorage(IOptions<StorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _client = new Lazy<AmazonS3Client>(CreateClient);
    }

    /// <inheritdoc/>
    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        await _client.Value.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = _options.Bucket,
                Key = key,
                InputStream = content,
                ContentType = contentType,
                AutoCloseStream = false,
            },
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<StoredObject?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _client.Value.GetObjectAsync(_options.Bucket, key, cancellationToken);
            var copy = new MemoryStream();
            await response.ResponseStream.CopyToAsync(copy, cancellationToken);
            copy.Position = 0;
            return new StoredObject(copy, response.Headers.ContentType ?? "application/octet-stream", copy.Length);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Value.GetObjectMetadataAsync(_options.Bucket, key, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task DownloadToFileAsync(string key, string filePath, CancellationToken cancellationToken = default)
    {
        using var response = await _client.Value.GetObjectAsync(_options.Bucket, key, cancellationToken);
        await using var file = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await response.ResponseStream.CopyToAsync(file, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UploadFileAsync(string filePath, string key, string contentType, CancellationToken cancellationToken = default)
    {
        using var transfer = new TransferUtility(_client.Value);
        await transfer.UploadAsync(
            new TransferUtilityUploadRequest
            {
                BucketName = _options.Bucket,
                Key = key,
                FilePath = filePath,
                ContentType = contentType,
                PartSize = MultipartPartSize,
            },
            cancellationToken);
    }

    /// <summary>Disposes the client if it was created.</summary>
    public void Dispose()
    {
        if (_client.IsValueCreated)
        {
            _client.Value.Dispose();
        }
    }

    /// <summary>
    /// Builds a client: path-style addressing (RustFS, R2), and checksums only where the API requires them, since
    /// S3-compatible servers do not all support the default CRC checksums of the SDK.
    /// </summary>
    /// <param name="options">The storage settings.</param>
    /// <returns>The client.</returns>
    /// <exception cref="InvalidOperationException">The storage settings are incomplete.</exception>
    internal static AmazonS3Client CreateClient(StorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ServiceUrl) || string.IsNullOrWhiteSpace(options.Bucket))
        {
            throw new InvalidOperationException("Storage__ServiceUrl and Storage__Bucket must be set.");
        }

        return new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKey, options.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = options.ServiceUrl,
                ForcePathStyle = true,
                AuthenticationRegion = options.Region,
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            });
    }

    /// <summary>Builds the client of this adapter.</summary>
    /// <returns>The client.</returns>
    private AmazonS3Client CreateClient() => CreateClient(_options);
}
