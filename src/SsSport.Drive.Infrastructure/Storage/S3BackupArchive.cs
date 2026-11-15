using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using SsSport.Drive.Application.Abstractions.Ingest;

namespace SsSport.Drive.Infrastructure.Storage;

/// <summary>
/// <see cref="IBackupArchive"/> over the rclone backup bucket (decisions §4): the bucket is only read. Files are found by
/// md5: the ETag of a single-part object, or, for an object rclone uploaded in parts, the base64 md5 it keeps in the
/// <c>md5chksum</c> metadata (I-2c). Files are copied into the media bucket inside storage, so no bytes travel through the app. The md5 index is rebuilt every 10 minutes. An empty <c>Storage__BackupBucket</c> turns it off.
/// </summary>
public sealed class S3BackupArchive : IBackupArchive, IDisposable
{
    /// <summary>How long the md5 index is reused.</summary>
    private static readonly TimeSpan _indexLifetime = TimeSpan.FromMinutes(10);

    /// <summary>The size above which a copy is done in parts: 4 GB (S3 allows 5 GB in one request).</summary>
    private const long SinglePartCopyLimit = 4L * 1024 * 1024 * 1024;

    /// <summary>The size of one part of a large copy: 512 MB.</summary>
    private const long CopyPartSize = 512L * 1024 * 1024;

    /// <summary>The settings.</summary>
    private readonly StorageOptions _options;

    /// <summary>The S3 client, created on first use.</summary>
    private readonly Lazy<AmazonS3Client> _client;

    /// <summary>Serializes index rebuilds.</summary>
    private readonly SemaphoreSlim _indexLock = new(1, 1);

    /// <summary>The md5 → backup key index.</summary>
    private Dictionary<string, string> _index = [];

    /// <summary>The md5 of each multipart object read from its metadata, by key and ETag, so it is read only once.</summary>
    private Dictionary<(string Key, string ETag), string?> _multipartMd5 = [];

    /// <summary>When the index was built, or <see langword="null"/> before the first build.</summary>
    private DateTimeOffset? _builtAt;

    /// <summary>Initializes the adapter.</summary>
    /// <param name="options">The storage settings.</param>
    public S3BackupArchive(IOptions<StorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _client = new Lazy<AmazonS3Client>(() => S3ObjectStorage.CreateClient(_options));
    }

    /// <inheritdoc/>
    public async Task<string?> FindByMd5Async(string md5, CancellationToken cancellationToken)
    {
        if (_options.BackupBucket.Length == 0)
        {
            return null;
        }

        var index = await IndexAsync(cancellationToken);
        return index.GetValueOrDefault(md5);
    }

    /// <inheritdoc/>
    public async Task CopyToMediaAsync(string backupKey, string mediaKey, CancellationToken cancellationToken)
    {
        var client = _client.Value;
        var size = (await client.GetObjectMetadataAsync(_options.BackupBucket, backupKey, cancellationToken)).ContentLength;
        if (size <= SinglePartCopyLimit)
        {
            await client.CopyObjectAsync(
                new CopyObjectRequest
                {
                    SourceBucket = _options.BackupBucket,
                    SourceKey = backupKey,
                    DestinationBucket = _options.Bucket,
                    DestinationKey = mediaKey,
                },
                cancellationToken);
            return;
        }

        await CopyInPartsAsync(client, backupKey, mediaKey, size, cancellationToken);
    }

    /// <summary>Disposes the client if it was created.</summary>
    public void Dispose()
    {
        _indexLock.Dispose();
        if (_client.IsValueCreated)
        {
            _client.Value.Dispose();
        }
    }

    /// <summary>Copies a very large object part by part.</summary>
    /// <param name="client">The S3 client.</param>
    /// <param name="backupKey">The key in the backup bucket.</param>
    /// <param name="mediaKey">The key in the media bucket.</param>
    /// <param name="size">The object's size in bytes.</param>
    /// <param name="cancellationToken">Cancels the copy.</param>
    /// <returns>A task that completes when the copy exists.</returns>
    private async Task CopyInPartsAsync(AmazonS3Client client, string backupKey, string mediaKey, long size, CancellationToken cancellationToken)
    {
        var upload = await client.InitiateMultipartUploadAsync(_options.Bucket, mediaKey, cancellationToken);
        try
        {
            var parts = new List<PartETag>();
            for (long start = 0, number = 1; start < size; start += CopyPartSize, number++)
            {
                var response = await client.CopyPartAsync(
                    new CopyPartRequest
                    {
                        SourceBucket = _options.BackupBucket,
                        SourceKey = backupKey,
                        DestinationBucket = _options.Bucket,
                        DestinationKey = mediaKey,
                        UploadId = upload.UploadId,
                        PartNumber = (int)number,
                        FirstByte = start,
                        LastByte = Math.Min(start + CopyPartSize, size) - 1,
                    },
                    cancellationToken);
                parts.Add(new PartETag((int)number, response.ETag));
            }

            await client.CompleteMultipartUploadAsync(
                new CompleteMultipartUploadRequest { BucketName = _options.Bucket, Key = mediaKey, UploadId = upload.UploadId, PartETags = parts },
                cancellationToken);
        }
        catch
        {
            await client.AbortMultipartUploadAsync(_options.Bucket, mediaKey, upload.UploadId, CancellationToken.None);
            throw;
        }
    }

    /// <summary>Gets the md5 index, rebuilding it when it is older than ten minutes.</summary>
    /// <param name="cancellationToken">Cancels the listing.</param>
    /// <returns>The index.</returns>
    private async Task<Dictionary<string, string>> IndexAsync(CancellationToken cancellationToken)
    {
        await _indexLock.WaitAsync(cancellationToken);
        try
        {
            if (_builtAt is { } builtAt && DateTimeOffset.UtcNow - builtAt < _indexLifetime)
            {
                return _index;
            }

            var index = new Dictionary<string, string>(StringComparer.Ordinal);
            var multipartMd5 = new Dictionary<(string Key, string ETag), string?>();
            var request = new ListObjectsV2Request { BucketName = _options.BackupBucket };
            ListObjectsV2Response page;
            do
            {
                page = await _client.Value.ListObjectsV2Async(request, cancellationToken);
                foreach (var item in page.S3Objects ?? [])
                {
                    var etag = item.ETag?.Trim('"').ToLowerInvariant();
                    if (etag is { Length: 32 } && !etag.Contains('-', StringComparison.Ordinal))
                    {
                        index.TryAdd(etag, item.Key);
                    }
                    else if (etag is not null && etag.Contains('-', StringComparison.Ordinal)
                        && await MultipartMd5Async(item.Key, etag, multipartMd5, cancellationToken) is { } md5)
                    {
                        // Multipart objects have "{md5}-{parts}" ETags, which are not the file's md5.
                        index.TryAdd(md5, item.Key);
                    }
                }

                request.ContinuationToken = page.NextContinuationToken;
            }
            while (page.IsTruncated == true);

            _index = index;
            _multipartMd5 = multipartMd5;
            _builtAt = DateTimeOffset.UtcNow;
            return index;
        }
        finally
        {
            _indexLock.Release();
        }
    }

    /// <summary>Reads the md5 rclone stored on a multipart object, reusing the last build's answer for the same ETag.</summary>
    /// <param name="key">The object's key.</param>
    /// <param name="etag">The object's multipart ETag.</param>
    /// <param name="seen">The answers of this build, filled in.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The lower-case hex md5, or <see langword="null"/> when the object has none.</returns>
    private async Task<string?> MultipartMd5Async(
        string key, string etag, Dictionary<(string Key, string ETag), string?> seen, CancellationToken cancellationToken)
    {
        if (!_multipartMd5.TryGetValue((key, etag), out var md5))
        {
            var metadata = await _client.Value.GetObjectMetadataAsync(_options.BackupBucket, key, cancellationToken);
            md5 = ParseRcloneMd5(metadata.Metadata["md5chksum"]);
        }

        seen[(key, etag)] = md5;
        return md5;
    }

    /// <summary>Turns rclone's base64 <c>md5chksum</c> into a lower-case hex md5.</summary>
    /// <param name="value">The metadata value, or <see langword="null"/>.</param>
    /// <returns>The md5, or <see langword="null"/> when it is missing or not a 16-byte base64 value.</returns>
    private static string? ParseRcloneMd5(string? value)
    {
        Span<byte> bytes = stackalloc byte[16];
        return value is { Length: > 0 } && Convert.TryFromBase64String(value, bytes, out var written) && written == 16
            ? Convert.ToHexStringLower(bytes)
            : null;
    }
}
