using System.Net;
using System.Net.Http.Headers;
using LagersystemLVHome.Application.Services.BackupProviders;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace LagersystemLVHome.UnitTests.Services.BackupProviders;

/// <summary>
/// Covers <see cref="CloudflareR2ProviderUploader"/>. Unlike the SDK-based providers
/// (AWS S3, Azure Blob, Google Drive), this class makes its HTTP calls through the
/// injected <see cref="IHttpClientFactory"/> seam, so its full success/failure/branching
/// behavior (including the hand-rolled AWS SigV4 signing) can be exercised without any
/// real network access by substituting the factory to hand back a client backed by a fake
/// <see cref="HttpMessageHandler"/>.
/// </summary>
public sealed class CloudflareR2ProviderUploaderTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    private string NewSourceFile(string content = "backup-content")
    {
        var path = Path.Combine(Path.GetTempPath(), "r2_uploader_src_" + Guid.NewGuid() + ".zip");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    private static IHttpClientFactory CreateHttpClientFactory(HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));
        return factory;
    }

    private static CloudflareR2ProviderUploader CreateSut(HttpMessageHandler handler)
        => new(NullLogger<CloudflareR2ProviderUploader>.Instance, CreateHttpClientFactory(handler));

    private static string ValidConfigJson(string accessKeyId = "AKIA-TEST", string prefix = "")
        => JsonSerializer.Serialize(new CloudflareR2Config
        {
            AccessKeyId = accessKeyId,
            SecretAccessKey = "secret",
            BucketName = "my-bucket",
            AccountId = "accountid",
            Prefix = prefix
        });

    private static BackupProvider CreateProvider(string configuration)
        => new() { Name = "R2", Type = BackupProviderType.CloudflareR2, Configuration = configuration };

    [Fact]
    public void SupportedProviderType_IsCloudflareR2()
    {
        var sut = new CloudflareR2ProviderUploader(NullLogger<CloudflareR2ProviderUploader>.Instance, Substitute.For<IHttpClientFactory>());
        sut.SupportedProviderType.Should().Be(BackupProviderType.CloudflareR2);
    }

    // ----- UploadAsync -----

    [Fact]
    public async Task UploadAsync_MissingAccessKeyId_ReturnsFalseWithoutHttpCall()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));
        var sut = CreateSut(handler);

        (await sut.UploadAsync(CreateProvider(ValidConfigJson(accessKeyId: "")), NewSourceFile())).Should().BeFalse();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task UploadAsync_InvalidJson_ReturnsFalse()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));
        var sut = CreateSut(handler);

        (await sut.UploadAsync(CreateProvider("not-json"), NewSourceFile())).Should().BeFalse();
    }

    [Fact]
    public async Task UploadAsync_SuccessResponse_ReturnsTrueAndSignsRequestWithSigV4Authorization()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = CreateSut(handler);

        var result = await sut.UploadAsync(CreateProvider(ValidConfigJson()), NewSourceFile());

        result.Should().BeTrue();
        handler.Requests.Should().ContainSingle();
        var request = handler.Requests[0];
        request.Method.Should().Be(HttpMethod.Put);
        request.RequestUri!.ToString().Should().Contain("my-bucket");
        request.Headers.TryGetValues("Authorization", out var authValues).Should().BeTrue();
        authValues!.Single().Should().StartWith("AWS4-HMAC-SHA256 Credential=AKIA-TEST/");
        request.Headers.Contains("x-amz-date").Should().BeTrue();
    }

    [Fact]
    public async Task UploadAsync_WithPrefix_BuildsObjectKeyUnderPrefix()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpMessageHandler(req => { captured = req; return new HttpResponseMessage(HttpStatusCode.OK); });
        var sut = CreateSut(handler);
        var source = NewSourceFile();

        var result = await sut.UploadAsync(CreateProvider(ValidConfigJson(prefix: "nightly/")), source);

        result.Should().BeTrue();
        captured!.RequestUri!.ToString().Should().Contain($"nightly/{Path.GetFileName(source)}");
    }

    [Fact]
    public async Task UploadAsync_FailureStatusCode_ReturnsFalse()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("access denied")
        });
        var sut = CreateSut(handler);

        (await sut.UploadAsync(CreateProvider(ValidConfigJson()), NewSourceFile())).Should().BeFalse();
    }

    // ----- ValidateAsync -----

    [Fact]
    public async Task ValidateAsync_SizeMatches_ReturnsTrue()
    {
        var handler = new FakeHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new ByteArrayContent(Array.Empty<byte>());
            response.Content.Headers.ContentLength = 42;
            return response;
        });
        var sut = CreateSut(handler);
        var history = new BackupHistory { FileName = "backup.zip", SizeBytes = 42, BackupProvider = CreateProvider(ValidConfigJson()) };

        (await sut.ValidateAsync(history)).Should().BeTrue();
        handler.Requests.Single().Method.Should().Be(HttpMethod.Head);
    }

    [Fact]
    public async Task ValidateAsync_SizeMismatch_ReturnsFalse()
    {
        var handler = new FakeHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new ByteArrayContent(Array.Empty<byte>());
            response.Content.Headers.ContentLength = 1;
            return response;
        });
        var sut = CreateSut(handler);
        var history = new BackupHistory { FileName = "backup.zip", SizeBytes = 999, BackupProvider = CreateProvider(ValidConfigJson()) };

        (await sut.ValidateAsync(history)).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_NotFoundStatus_ReturnsFalse()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = CreateSut(handler);
        var history = new BackupHistory { FileName = "backup.zip", SizeBytes = 5, BackupProvider = CreateProvider(ValidConfigJson()) };

        (await sut.ValidateAsync(history)).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_NullConfig_ReturnsFalseWithoutHttpCall()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));
        var sut = CreateSut(handler);
        var history = new BackupHistory { FileName = "backup.zip", BackupProvider = CreateProvider("null") };

        (await sut.ValidateAsync(history)).Should().BeFalse();
        handler.Requests.Should().BeEmpty();
    }

    // ----- DeleteAsync -----

    [Fact]
    public async Task DeleteAsync_NoContentStatus_ReturnsTrue()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = CreateSut(handler);
        var history = new BackupHistory { FileName = "backup.zip", BackupProvider = CreateProvider(ValidConfigJson()) };

        (await sut.DeleteAsync(history)).Should().BeTrue();
        handler.Requests.Single().Method.Should().Be(HttpMethod.Delete);
    }

    [Fact]
    public async Task DeleteAsync_FailureStatus_ReturnsFalse()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var sut = CreateSut(handler);
        var history = new BackupHistory { FileName = "backup.zip", BackupProvider = CreateProvider(ValidConfigJson()) };

        (await sut.DeleteAsync(history)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_InvalidJson_ReturnsFalse()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));
        var sut = CreateSut(handler);
        var history = new BackupHistory { FileName = "backup.zip", BackupProvider = CreateProvider("garbage") };

        (await sut.DeleteAsync(history)).Should().BeFalse();
    }

    // ----- TestConnectionAsync -----

    [Fact]
    public async Task TestConnectionAsync_Success_ReturnsTrue()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = CreateSut(handler);

        (await sut.TestConnectionAsync(CreateProvider(ValidConfigJson()))).Should().BeTrue();
    }

    [Fact]
    public async Task TestConnectionAsync_Failure_ReturnsFalse()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("bad credentials")
        });
        var sut = CreateSut(handler);

        (await sut.TestConnectionAsync(CreateProvider(ValidConfigJson()))).Should().BeFalse();
    }

    [Fact]
    public async Task TestConnectionAsync_MissingAccessKeyId_ReturnsFalseWithoutHttpCall()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));
        var sut = CreateSut(handler);

        (await sut.TestConnectionAsync(CreateProvider(ValidConfigJson(accessKeyId: "")))).Should().BeFalse();
        handler.Requests.Should().BeEmpty();
    }
}

/// <summary>Routes every request through a caller-supplied responder and records every
/// request seen, so tests can both control HTTP responses and assert on what was actually
/// sent (method, URL, headers) without any real network I/O.</summary>
file sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public List<HttpRequestMessage> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(_responder(request));
    }
}
