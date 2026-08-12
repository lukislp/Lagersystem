using System.Net;
using LagersystemLVHome.Application.Services.BackupProviders;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace LagersystemLVHome.UnitTests.Services.BackupProviders;

/// <summary>
/// Covers <see cref="OneDriveProviderUploader"/>. All Microsoft Graph / OAuth token calls
/// go through the injected <see cref="IHttpClientFactory"/> seam, so the full flow
/// (token refresh, simple upload, chunked upload session for large files, validate,
/// delete, connection test incl. folder creation) is exercised via a routing fake
/// <see cref="HttpMessageHandler"/> instead of any real network access.
/// </summary>
public sealed class OneDriveProviderUploaderTests : IDisposable
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

    private string NewSourceFile(int sizeBytes)
    {
        var path = Path.Combine(Path.GetTempPath(), "onedrive_uploader_src_" + Guid.NewGuid() + ".zip");
        File.WriteAllBytes(path, new byte[sizeBytes]);
        _tempFiles.Add(path);
        return path;
    }

    private static IHttpClientFactory CreateHttpClientFactory(HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));
        return factory;
    }

    private static OneDriveProviderUploader CreateSut(HttpMessageHandler handler)
        => new(NullLogger<OneDriveProviderUploader>.Instance, CreateHttpClientFactory(handler));

    private static string ValidConfigJson(string clientId = "client-id") => JsonSerializer.Serialize(new OneDriveConfig
    {
        ClientId = clientId,
        ClientSecret = "secret",
        RefreshToken = "refresh-token",
        FolderPath = "/LagerSystem/Backups"
    });

    private static BackupProvider CreateProvider(string configuration)
        => new() { Name = "OD", Type = BackupProviderType.OneDrive, Configuration = configuration };

    private static HttpResponseMessage TokenResponse(bool success = true) =>
        success
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"fake-token\"}") }
            : new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":\"invalid_grant\"}") };

    [Fact]
    public void SupportedProviderType_IsOneDrive()
    {
        var sut = new OneDriveProviderUploader(NullLogger<OneDriveProviderUploader>.Instance, Substitute.For<IHttpClientFactory>());
        sut.SupportedProviderType.Should().Be(BackupProviderType.OneDrive);
    }

    // ----- UploadAsync -----

    [Fact]
    public async Task UploadAsync_MissingClientId_ReturnsFalseWithoutHttpCall()
    {
        var handler = new RoutingHttpMessageHandler();
        var sut = CreateSut(handler);

        (await sut.UploadAsync(CreateProvider(ValidConfigJson(clientId: "")), NewSourceFile(10))).Should().BeFalse();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task UploadAsync_InvalidJson_ReturnsFalse()
    {
        var handler = new RoutingHttpMessageHandler();
        var sut = CreateSut(handler);

        (await sut.UploadAsync(CreateProvider("not-json"), NewSourceFile(10))).Should().BeFalse();
    }

    [Fact]
    public async Task UploadAsync_TokenFetchFails_ReturnsFalse()
    {
        var handler = new RoutingHttpMessageHandler();
        handler.OnPost("login.microsoftonline.com", _ => TokenResponse(success: false));
        var sut = CreateSut(handler);

        (await sut.UploadAsync(CreateProvider(ValidConfigJson()), NewSourceFile(10))).Should().BeFalse();
    }

    [Fact]
    public async Task UploadAsync_SmallFile_TokenAndUploadSucceed_ReturnsTrue()
    {
        var handler = new RoutingHttpMessageHandler();
        handler.OnPost("login.microsoftonline.com", _ => TokenResponse());
        handler.OnPut("graph.microsoft.com", _ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = CreateSut(handler);

        var result = await sut.UploadAsync(CreateProvider(ValidConfigJson()), NewSourceFile(1024));

        result.Should().BeTrue();
        handler.Requests.Should().Contain(r => r.Method == HttpMethod.Put &&
            r.RequestUri!.ToString().Contains("LagerSystem/Backups"));
    }

    [Fact]
    public async Task UploadAsync_SmallFile_UploadFails_ReturnsFalse()
    {
        var handler = new RoutingHttpMessageHandler();
        handler.OnPost("login.microsoftonline.com", _ => TokenResponse());
        handler.OnPut("graph.microsoft.com", _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var sut = CreateSut(handler);

        (await sut.UploadAsync(CreateProvider(ValidConfigJson()), NewSourceFile(1024))).Should().BeFalse();
    }

    [Fact]
    public async Task UploadAsync_LargeFile_ChunkedUploadSucceeds_ReturnsTrue()
    {
        const int fileSize = 4 * 1024 * 1024 + 500; // just over the 4 MB simple-upload threshold
        var handler = new RoutingHttpMessageHandler();
        handler.OnPost("login.microsoftonline.com", _ => TokenResponse());
        handler.OnPost("createUploadSession", _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"uploadUrl\":\"https://upload.example.com/session-abc\"}")
        });
        handler.OnPut("upload.example.com", _ => new HttpResponseMessage(HttpStatusCode.Accepted));
        var sut = CreateSut(handler);

        var result = await sut.UploadAsync(CreateProvider(ValidConfigJson()), NewSourceFile(fileSize));

        result.Should().BeTrue();
        var chunkRequests = handler.Requests.Where(r => r.RequestUri!.Host == "upload.example.com").ToList();
        chunkRequests.Should().HaveCountGreaterThan(1, "a >4MB file must be uploaded in more than one 320 KB chunk");
    }

    [Fact]
    public async Task UploadAsync_LargeFile_SessionCreationFails_ReturnsFalse()
    {
        const int fileSize = 4 * 1024 * 1024 + 500;
        var handler = new RoutingHttpMessageHandler();
        handler.OnPost("login.microsoftonline.com", _ => TokenResponse());
        handler.OnPost("createUploadSession", _ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var sut = CreateSut(handler);

        (await sut.UploadAsync(CreateProvider(ValidConfigJson()), NewSourceFile(fileSize))).Should().BeFalse();
    }

    [Fact]
    public async Task UploadAsync_LargeFile_FirstChunkFails_ReturnsFalse()
    {
        const int fileSize = 4 * 1024 * 1024 + 500;
        var handler = new RoutingHttpMessageHandler();
        handler.OnPost("login.microsoftonline.com", _ => TokenResponse());
        handler.OnPost("createUploadSession", _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"uploadUrl\":\"https://upload.example.com/session-abc\"}")
        });
        handler.OnPut("upload.example.com", _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var sut = CreateSut(handler);

        (await sut.UploadAsync(CreateProvider(ValidConfigJson()), NewSourceFile(fileSize))).Should().BeFalse();
    }

    // ----- ValidateAsync -----

    [Fact]
    public async Task ValidateAsync_SizeMatches_ReturnsTrue()
    {
        var handler = new RoutingHttpMessageHandler();
        handler.OnPost("login.microsoftonline.com", _ => TokenResponse());
        handler.OnGet("graph.microsoft.com", _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"size\":123}")
        });
        var sut = CreateSut(handler);
        var history = new BackupHistory { FileName = "b.zip", SizeBytes = 123, BackupProvider = CreateProvider(ValidConfigJson()) };

        (await sut.ValidateAsync(history)).Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_SizeMismatch_ReturnsFalse()
    {
        var handler = new RoutingHttpMessageHandler();
        handler.OnPost("login.microsoftonline.com", _ => TokenResponse());
        handler.OnGet("graph.microsoft.com", _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"size\":1}")
        });
        var sut = CreateSut(handler);
        var history = new BackupHistory { FileName = "b.zip", SizeBytes = 999, BackupProvider = CreateProvider(ValidConfigJson()) };

        (await sut.ValidateAsync(history)).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_NotFound_ReturnsFalse()
    {
        var handler = new RoutingHttpMessageHandler();
        handler.OnPost("login.microsoftonline.com", _ => TokenResponse());
        handler.OnGet("graph.microsoft.com", _ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = CreateSut(handler);
        var history = new BackupHistory { FileName = "b.zip", SizeBytes = 5, BackupProvider = CreateProvider(ValidConfigJson()) };

        (await sut.ValidateAsync(history)).Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_TokenFails_ReturnsFalse()
    {
        var handler = new RoutingHttpMessageHandler();
        handler.OnPost("login.microsoftonline.com", _ => TokenResponse(success: false));
        var sut = CreateSut(handler);
        var history = new BackupHistory { FileName = "b.zip", BackupProvider = CreateProvider(ValidConfigJson()) };

        (await sut.ValidateAsync(history)).Should().BeFalse();
    }

    // ----- DeleteAsync -----

    [Fact]
    public async Task DeleteAsync_Success_ReturnsTrue()
    {
        var handler = new RoutingHttpMessageHandler();
        handler.OnPost("login.microsoftonline.com", _ => TokenResponse());
        handler.OnDelete("graph.microsoft.com", _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = CreateSut(handler);
        var history = new BackupHistory { FileName = "b.zip", BackupProvider = CreateProvider(ValidConfigJson()) };

        (await sut.DeleteAsync(history)).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_Failure_ReturnsFalse()
    {
        var handler = new RoutingHttpMessageHandler();
        handler.OnPost("login.microsoftonline.com", _ => TokenResponse());
        handler.OnDelete("graph.microsoft.com", _ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = CreateSut(handler);
        var history = new BackupHistory { FileName = "b.zip", BackupProvider = CreateProvider(ValidConfigJson()) };

        (await sut.DeleteAsync(history)).Should().BeFalse();
    }

    // ----- TestConnectionAsync -----

    [Fact]
    public async Task TestConnectionAsync_DriveInfoAndFolderExist_ReturnsTrue()
    {
        var handler = new RoutingHttpMessageHandler();
        handler.OnPost("login.microsoftonline.com", _ => TokenResponse());
        handler.OnGet("graph.microsoft.com", _ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = CreateSut(handler);

        (await sut.TestConnectionAsync(CreateProvider(ValidConfigJson()))).Should().BeTrue();
    }

    [Fact]
    public async Task TestConnectionAsync_DriveInfoFails_ReturnsFalse()
    {
        var handler = new RoutingHttpMessageHandler();
        handler.OnPost("login.microsoftonline.com", _ => TokenResponse());
        handler.OnGet("graph.microsoft.com", req => req.RequestUri!.ToString().EndsWith("/me/drive")
            ? new HttpResponseMessage(HttpStatusCode.Forbidden)
            : new HttpResponseMessage(HttpStatusCode.OK));
        var sut = CreateSut(handler);

        (await sut.TestConnectionAsync(CreateProvider(ValidConfigJson()))).Should().BeFalse();
    }

    [Fact]
    public async Task TestConnectionAsync_FolderMissing_CreatesItAndReturnsTrue()
    {
        var handler = new RoutingHttpMessageHandler();
        handler.OnPost("login.microsoftonline.com", _ => TokenResponse());
        handler.OnGet("graph.microsoft.com", req => req.RequestUri!.ToString().EndsWith("/me/drive")
            ? new HttpResponseMessage(HttpStatusCode.OK)
            : new HttpResponseMessage(HttpStatusCode.NotFound)); // folder-exists check fails -> triggers create
        handler.OnPost("graph.microsoft.com", req => req.RequestUri!.ToString().Contains("/children")
            ? new HttpResponseMessage(HttpStatusCode.Created)
            : new HttpResponseMessage(HttpStatusCode.OK));
        var sut = CreateSut(handler);

        var result = await sut.TestConnectionAsync(CreateProvider(ValidConfigJson()));

        result.Should().BeTrue();
        handler.Requests.Should().Contain(r => r.Method == HttpMethod.Post && r.RequestUri!.ToString().Contains("/children"));
    }

    [Fact]
    public async Task TestConnectionAsync_MissingClientId_ReturnsFalseWithoutHttpCall()
    {
        var handler = new RoutingHttpMessageHandler();
        var sut = CreateSut(handler);

        (await sut.TestConnectionAsync(CreateProvider(ValidConfigJson(clientId: "")))).Should().BeFalse();
        handler.Requests.Should().BeEmpty();
    }
}

/// <summary>Routes fake HTTP responses by method + host/URL-substring match, in registration
/// order, recording every request seen. Lets a single test wire up distinct canned responses
/// for the several different endpoints (OAuth token, Graph API, upload-session URL) a single
/// OneDrive operation may call in sequence, without any real network access.</summary>
file sealed class RoutingHttpMessageHandler : HttpMessageHandler
{
    private readonly List<(HttpMethod Method, string Match, Func<HttpRequestMessage, HttpResponseMessage> Responder)> _routes = new();

    public List<HttpRequestMessage> Requests { get; } = new();

    public void OnPost(string urlSubstring, Func<HttpRequestMessage, HttpResponseMessage> responder)
        => _routes.Add((HttpMethod.Post, urlSubstring, responder));

    public void OnPut(string urlSubstring, Func<HttpRequestMessage, HttpResponseMessage> responder)
        => _routes.Add((HttpMethod.Put, urlSubstring, responder));

    public void OnGet(string urlSubstring, Func<HttpRequestMessage, HttpResponseMessage> responder)
        => _routes.Add((HttpMethod.Get, urlSubstring, responder));

    public void OnDelete(string urlSubstring, Func<HttpRequestMessage, HttpResponseMessage> responder)
        => _routes.Add((HttpMethod.Delete, urlSubstring, responder));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        var url = request.RequestUri!.ToString();
        foreach (var (method, match, responder) in _routes)
        {
            if (request.Method == method && url.Contains(match, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(responder(request));
            }
        }

        throw new InvalidOperationException($"No fake route configured for {request.Method} {url}");
    }
}
