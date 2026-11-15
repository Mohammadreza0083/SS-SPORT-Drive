using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google;
using Microsoft.Extensions.Options;
using SsSport.Drive.Infrastructure.Drive;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Drive;

/// <summary>
/// I-2b (found 2026-11-15): <see cref="GoogleDriveApi.DownloadAsync"/> must return a file's <b>contents</b>
/// (<c>files.get?alt=media</c>), not its JSON metadata (plain <c>files.get</c>), or every copy fails the md5 check.
/// The real Google client runs against a stubbed Google: the token endpoint and the Drive files endpoint.
/// </summary>
public sealed class GoogleDriveApiDownloadTests : IDisposable
{
    /// <summary>The file's contents.</summary>
    private static readonly byte[] _contents = Encoding.UTF8.GetBytes("%PDF-1.7 real file bytes");

    /// <summary>A throwaway service account key, written for the client to read.</summary>
    private readonly string _keyPath = Path.Combine(Path.GetTempPath(), $"ssd-test-key-{Guid.NewGuid():N}.json");

    /// <summary>The stubbed Google.</summary>
    private readonly StubGoogle _google = new();

    /// <summary>Writes the service account key.</summary>
    public GoogleDriveApiDownloadTests()
    {
        using var rsa = RSA.Create(2048);
        File.WriteAllText(_keyPath, JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["type"] = "service_account",
            ["project_id"] = "ssd-test",
            ["private_key_id"] = "key-1",
            ["private_key"] = rsa.ExportPkcs8PrivateKeyPem(),
            ["client_email"] = "ingest@ssd-test.iam.gserviceaccount.com",
            ["client_id"] = "1",
            ["token_uri"] = "https://oauth2.googleapis.com/token",
        }));
    }

    /// <summary>Deletes the key.</summary>
    public void Dispose() => File.Delete(_keyPath);

    /// <summary>The download is the file's bytes, asked for with <c>alt=media</c> in the Shared Drive.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Downloads_the_contents_not_the_metadata()
    {
        using var api = CreateApi();

        await using var stream = await api.DownloadAsync("file-1", TestContext.Current.CancellationToken);

        using var copy = new MemoryStream();
        await stream!.CopyToAsync(copy, TestContext.Current.CancellationToken);
        copy.ToArray().Should().Equal(_contents);
        var request = _google.Requests.Should().ContainSingle(uri => uri.AbsolutePath.EndsWith("/files/file-1", StringComparison.Ordinal)).Subject;
        request.Query.Should().Contain("alt=media").And.Contain("supportsAllDrives=true");
    }

    /// <summary>A file Drive no longer has (404) is <see langword="null"/>.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task A_missing_file_is_null()
    {
        using var api = CreateApi();

        var stream = await api.DownloadAsync("gone", TestContext.Current.CancellationToken);

        stream.Should().BeNull();
    }

    /// <summary>Any other error status is an exception, never a body to store, so the copy is retried later.</summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task An_error_status_throws()
    {
        using var api = CreateApi();

        var download = () => api.DownloadAsync("forbidden", TestContext.Current.CancellationToken);

        (await download.Should().ThrowAsync<GoogleApiException>()).Which.HttpStatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Builds the client against the stub.</summary>
    /// <returns>The client.</returns>
    private GoogleDriveApi CreateApi() =>
        new(Options.Create(new GoogleDriveOptions { ServiceAccountKeyPath = _keyPath, SharedDriveId = "drive-1" }), () => _google);

    /// <summary>
    /// Stands in for Google: issues a token, answers <c>files.get</c> with metadata, <c>files.get?alt=media</c> with the
    /// contents, and 404 for the file <c>gone</c>.
    /// </summary>
    private sealed class StubGoogle : HttpMessageHandler
    {
        /// <summary>Gets every Drive request it answered.</summary>
        public ConcurrentQueue<Uri> Requests { get; } = new();

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.AbsolutePath.EndsWith("/token", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """{"access_token":"test-token","expires_in":3600,"token_type":"Bearer"}""");
            }

            Requests.Enqueue(uri);
            if (uri.AbsolutePath.EndsWith("/files/gone", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.NotFound, """{"error":{"code":404,"message":"File not found: gone.","errors":[{"reason":"notFound"}]}}""");
            }

            if (uri.AbsolutePath.EndsWith("/files/forbidden", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.Forbidden, """{"error":{"code":403,"message":"The user does not have sufficient permissions.","errors":[{"reason":"insufficientFilePermissions"}]}}""");
            }

            if (uri.Query.Contains("alt=media", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_contents) });
            }

            return Json(HttpStatusCode.OK, """{"kind":"drive#file","id":"file-1","name":"report.pdf","mimeType":"application/pdf"}""");
        }

        /// <summary>Answers with JSON.</summary>
        /// <param name="status">The status.</param>
        /// <param name="json">The body.</param>
        /// <returns>The response.</returns>
        private static Task<HttpResponseMessage> Json(HttpStatusCode status, string json) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}
