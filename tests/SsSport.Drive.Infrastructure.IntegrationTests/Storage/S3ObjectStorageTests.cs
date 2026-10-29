using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Options;
using SsSport.Drive.Infrastructure.Storage;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Storage;

/// <summary>
/// The S3 adapter stores and reads objects in a real S3-compatible server: RustFS, the same image as the local stack
/// (R2 speaks the same API).
/// </summary>
public sealed class S3ObjectStorageTests : IAsyncLifetime
{
    /// <summary>The access key of the throwaway server.</summary>
    private const string AccessKey = "ssdrive-test";

    /// <summary>The secret key of the throwaway server.</summary>
    private const string SecretKey = "ssdrive-test-secret";

    /// <summary>The bucket the tests use.</summary>
    private const string Bucket = "ss-sport-drive-test";

    /// <summary>A throwaway RustFS server; keep the image in step with docker-compose.</summary>
    private readonly IContainer _rustfs = new ContainerBuilder("rustfs/rustfs:1.0.3")
        .WithEnvironment("RUSTFS_ACCESS_KEY", AccessKey)
        .WithEnvironment("RUSTFS_SECRET_KEY", SecretKey)
        .WithPortBinding(9000, assignRandomHostPort: true)
        .Build();

    /// <summary>The adapter under test.</summary>
    private S3ObjectStorage _storage = null!;

    /// <summary>Starts RustFS and creates the bucket once the server answers.</summary>
    /// <returns>A task that completes when the bucket exists.</returns>
    public async ValueTask InitializeAsync()
    {
        await _rustfs.StartAsync();
        var options = new StorageOptions
        {
            ServiceUrl = $"http://{_rustfs.Hostname}:{_rustfs.GetMappedPublicPort(9000)}",
            AccessKey = AccessKey,
            SecretKey = SecretKey,
            Bucket = Bucket,
        };
        _storage = new S3ObjectStorage(Options.Create(options));

        using var admin = new AmazonS3Client(new BasicAWSCredentials(AccessKey, SecretKey), new AmazonS3Config { ServiceURL = options.ServiceUrl, ForcePathStyle = true });
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await admin.PutBucketAsync(Bucket, TestContext.Current.CancellationToken);
                return;
            }
            catch (Exception) when (attempt < 30)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            }
        }
    }

    /// <summary>Stops the server.</summary>
    /// <returns>A task that completes when the container is gone.</returns>
    public async ValueTask DisposeAsync()
    {
        _storage.Dispose();
        await _rustfs.DisposeAsync();
    }

    /// <summary>An object comes back with its bytes and content type.</summary>
    [Fact]
    public async Task Stores_and_reads_an_object()
    {
        var bytes = Encoding.UTF8.GetBytes("webp bytes");

        await _storage.PutAsync("players/0199f2b1/photo-1.webp", new MemoryStream(bytes), "image/webp", TestContext.Current.CancellationToken);
        var stored = await _storage.GetAsync("players/0199f2b1/photo-1.webp", TestContext.Current.CancellationToken);

        stored.Should().NotBeNull();
        stored!.ContentType.Should().Be("image/webp");
        stored.Length.Should().Be(bytes.Length);
        using var copy = new MemoryStream();
        await stored.Content.CopyToAsync(copy, TestContext.Current.CancellationToken);
        copy.ToArray().Should().Equal(bytes);
    }

    /// <summary>A missing object is <see langword="null"/>, not an exception.</summary>
    [Fact]
    public async Task Missing_object_is_null()
    {
        (await _storage.GetAsync("players/nobody/photo.webp", TestContext.Current.CancellationToken)).Should().BeNull();
    }
}
