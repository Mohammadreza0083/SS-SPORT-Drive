using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using SsSport.Drive.Application.Abstractions.Storage;

namespace SsSport.Drive.Infrastructure.Storage;

/// <summary>
/// <see cref="IObjectStorage"/> over the S3 API (AWSSDK.S3), for RustFS locally and Cloudflare R2 in production.
/// The client is created on first use, so hosts without storage settings still start.
/// </summary>
public sealed class S3ObjectStorage : IObjectStorage, IDisposable
{
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

    /// <summary>Disposes the client if it was created.</summary>
    public void Dispose()
    {
        if (_client.IsValueCreated)
        {
            _client.Value.Dispose();
        }
    }

    /// <summary>
    /// Builds the client: path-style addressing (RustFS, R2), and checksums only where the API requires them, since
    /// S3-compatible servers do not all support the default CRC checksums of the SDK.
    /// </summary>
    /// <returns>The client.</returns>
    /// <exception cref="InvalidOperationException">The storage settings are incomplete.</exception>
    private AmazonS3Client CreateClient()
    {
        if (string.IsNullOrWhiteSpace(_options.ServiceUrl) || string.IsNullOrWhiteSpace(_options.Bucket))
        {
            throw new InvalidOperationException("Storage__ServiceUrl and Storage__Bucket must be set.");
        }

        return new AmazonS3Client(
            new BasicAWSCredentials(_options.AccessKey, _options.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = _options.ServiceUrl,
                ForcePathStyle = true,
                AuthenticationRegion = _options.Region,
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            });
    }
}
