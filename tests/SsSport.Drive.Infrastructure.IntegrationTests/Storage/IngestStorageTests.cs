using System.Security.Cryptography;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Options;
using SsSport.Drive.Infrastructure.Storage;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Storage;

/// <summary>
/// I-2 storage against a real S3-compatible server (RustFS; R2 speaks the same API): the media bucket tells whether an
/// object exists and moves large files to and from local disk; the rclone backup bucket (<c>Storage__BackupBucket</c>,
/// read-only to the app) is searched by md5 and copied from inside storage, never written.
/// </summary>
public sealed class IngestStorageTests : IAsyncLifetime
{
    /// <summary>The access key of the throwaway server.</summary>
    private const string AccessKey = "ssdrive-test";

    /// <summary>The secret key of the throwaway server.</summary>
    private const string SecretKey = "ssdrive-test-secret";

    /// <summary>The media bucket.</summary>
    private const string Bucket = "ss-sport-drive-test";

    /// <summary>The backup bucket.</summary>
    private const string BackupBucket = "ssdrivebackup-test";

    /// <summary>A throwaway RustFS server; keep the image in step with docker-compose.</summary>
    private readonly IContainer _rustfs = new ContainerBuilder("rustfs/rustfs:1.0.3")
        .WithEnvironment("RUSTFS_ACCESS_KEY", AccessKey)
        .WithEnvironment("RUSTFS_SECRET_KEY", SecretKey)
        .WithPortBinding(9000, assignRandomHostPort: true)
        .Build();

    /// <summary>The work folder.</summary>
    private readonly string _work = Directory.CreateTempSubdirectory("ssd-storage-test-").FullName;

    /// <summary>The settings.</summary>
    private StorageOptions _options = null!;

    /// <summary>The media bucket adapter.</summary>
    private S3ObjectStorage _storage = null!;

    /// <summary>A plain S3 client, to look at the buckets directly.</summary>
    private AmazonS3Client _admin = null!;

    /// <summary>Starts RustFS and creates both buckets once the server answers.</summary>
    /// <returns>A task that completes when the buckets exist.</returns>
    public async ValueTask InitializeAsync()
    {
        await _rustfs.StartAsync();
        _options = new StorageOptions
        {
            ServiceUrl = $"http://{_rustfs.Hostname}:{_rustfs.GetMappedPublicPort(9000)}",
            AccessKey = AccessKey,
            SecretKey = SecretKey,
            Bucket = Bucket,
            BackupBucket = BackupBucket,
        };
        _storage = new S3ObjectStorage(Options.Create(_options));
        _admin = new AmazonS3Client(new BasicAWSCredentials(AccessKey, SecretKey), new AmazonS3Config { ServiceURL = _options.ServiceUrl, ForcePathStyle = true });
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await _admin.PutBucketAsync(Bucket, TestContext.Current.CancellationToken);
                await _admin.PutBucketAsync(BackupBucket, TestContext.Current.CancellationToken);
                return;
            }
            catch (Exception) when (attempt < 30)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            }
        }
    }

    /// <summary>Stops the server and deletes the work folder.</summary>
    /// <returns>A task that completes when everything is gone.</returns>
    public async ValueTask DisposeAsync()
    {
        _storage.Dispose();
        _admin.Dispose();
        await _rustfs.DisposeAsync();
        Directory.Delete(_work, recursive: true);
    }

    /// <summary>Existence is checked without downloading.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Existence_is_checked()
    {
        const string key = "originals/9e107d9d372bb6826bd81d3542a419d6.mp4";
        (await _storage.ExistsAsync(key, TestContext.Current.CancellationToken)).Should().BeFalse();

        await _storage.PutAsync(key, new MemoryStream([1, 2, 3]), "video/mp4", TestContext.Current.CancellationToken);

        (await _storage.ExistsAsync(key, TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    /// <summary>A file larger than one upload part goes up and comes back byte for byte, with its content type.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Large_files_go_up_and_come_back()
    {
        var bytes = RandomNumberGenerator.GetBytes(20 * 1024 * 1024);
        var source = Path.Combine(_work, "big.mp4");
        await File.WriteAllBytesAsync(source, bytes, TestContext.Current.CancellationToken);

        await _storage.UploadFileAsync(source, "originals/big.mp4", "video/mp4", TestContext.Current.CancellationToken);
        var copy = Path.Combine(_work, "copy.mp4");
        await _storage.DownloadToFileAsync("originals/big.mp4", copy, TestContext.Current.CancellationToken);

        (await File.ReadAllBytesAsync(copy, TestContext.Current.CancellationToken)).Should().Equal(bytes);
        (await _storage.GetAsync("originals/big.mp4", TestContext.Current.CancellationToken))!.ContentType.Should().Be("video/mp4");
    }

    /// <summary>The backup is searched by md5 (the ETag of a single-part object); an unknown md5 finds nothing.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task The_backup_is_searched_by_md5()
    {
        var bytes = Encoding.UTF8.GetBytes("a full match from the backup");
        const string backupKey = "Men Volleyball/SS-SPORT Ali Hoshmand／OH/Full matches/Game 1.MP4";
        await PutBackupAsync(backupKey, bytes);
        var archive = new S3BackupArchive(Options.Create(_options));

        var found = await archive.FindByMd5Async(Md5(bytes), TestContext.Current.CancellationToken);
        var missing = await archive.FindByMd5Async("00000000000000000000000000000000", TestContext.Current.CancellationToken);

        found.Should().Be(backupKey);
        missing.Should().BeNull();
    }

    /// <summary>Copying from the backup fills the media bucket and leaves the backup untouched.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Copying_from_the_backup_leaves_it_untouched()
    {
        var bytes = Encoding.UTF8.GetBytes("highlights from the backup");
        const string backupKey = "Women Volleyball/SS-SPORT Sara Ahmadi／MB/Highlights/hl.mp4";
        await PutBackupAsync(backupKey, bytes);
        var archive = new S3BackupArchive(Options.Create(_options));
        var mediaKey = $"originals/{Md5(bytes)}.mp4";

        await archive.CopyToMediaAsync(backupKey, mediaKey, TestContext.Current.CancellationToken);

        var stored = await _storage.GetAsync(mediaKey, TestContext.Current.CancellationToken);
        using var copy = new MemoryStream();
        await stored!.Content.CopyToAsync(copy, TestContext.Current.CancellationToken);
        copy.ToArray().Should().Equal(bytes);
        var backup = await _admin.ListObjectsV2Async(new ListObjectsV2Request { BucketName = BackupBucket }, TestContext.Current.CancellationToken);
        backup.S3Objects.Select(o => o.Key).Should().NotContain(mediaKey);
    }

    /// <summary>Without a backup bucket configured nothing is ever found.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Without_a_backup_bucket_nothing_is_found()
    {
        var bytes = Encoding.UTF8.GetBytes("only in the backup");
        await PutBackupAsync("loose.mp4", bytes);
        var archive = new S3BackupArchive(Options.Create(_options with { BackupBucket = string.Empty }));

        (await archive.FindByMd5Async(Md5(bytes), TestContext.Current.CancellationToken)).Should().BeNull();
    }

    /// <summary>Stores an object in the backup bucket, as rclone does.</summary>
    /// <param name="key">The key.</param>
    /// <param name="bytes">The bytes.</param>
    /// <returns>A task that completes when the object is stored.</returns>
    private async Task PutBackupAsync(string key, byte[] bytes) =>
        await _admin.PutObjectAsync(new PutObjectRequest { BucketName = BackupBucket, Key = key, InputStream = new MemoryStream(bytes) }, TestContext.Current.CancellationToken);

    /// <summary>Computes a lower-case md5.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The md5.</returns>
    private static string Md5(byte[] bytes) => Convert.ToHexStringLower(MD5.HashData(bytes));
}
