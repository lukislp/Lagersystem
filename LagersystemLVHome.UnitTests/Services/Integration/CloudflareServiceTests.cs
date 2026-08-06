using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using LagersystemLVHome.Application.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LagersystemLVHome.UnitTests.Services.Integration;

public class CloudflareServiceTests
{
    private const string ZoneId = "zone-123";

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<string> RequestUris { get; } = new();
        public List<string> RequestBodies { get; } = new();
        public List<AuthenticationHeaderValue?> AuthorizationHeaders { get; } = new();
        public int CallCount { get; private set; }

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUris.Add(request.RequestUri?.ToString() ?? string.Empty);
            AuthorizationHeaders.Add(request.Headers.Authorization);
            RequestBodies.Add(request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken) : string.Empty);
            return _responder(request);
        }
    }

    private static CloudflareService Build(
        FakeHandler handler,
        CloudflareSettings? settings = null,
        IServiceProvider? serviceProvider = null)
    {
        // CreateAuthenticatedClient() is called fresh for every single Cloudflare*
        // API call and unconditionally adds an "Authorization" header, exactly like
        // the real IHttpClientFactory.CreateClient() would hand back a brand-new
        // HttpClient each time. Returning the SAME HttpClient instance across calls
        // (as a naive stub would) makes the second Add("Authorization", ...) throw,
        // since it's a single-valued header - so each CreateClient() call here must
        // hand back a new HttpClient wrapping the shared handler.
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient().Returns(_ => new HttpClient(handler, disposeHandler: false));
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));

        var cfSettings = settings ?? new CloudflareSettings
        {
            Enabled = true,
            ApiToken = "test-token",
            ZoneId = ZoneId
        };

        return new CloudflareService(
            NullLogger<CloudflareService>.Instance,
            Options.Create(cfSettings),
            httpClientFactory,
            serviceProvider ?? Substitute.For<IServiceProvider>());
    }

    private static IServiceProvider BuildScopedServiceProvider(INotificationService? notificationService = null)
    {
        var scopeProvider = Substitute.For<IServiceProvider>();
        scopeProvider.GetService(typeof(INotificationService)).Returns(notificationService);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(scopeProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IServiceScopeFactory)).Returns(scopeFactory);
        return serviceProvider;
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private static HttpResponseMessage ServerError() => new(HttpStatusCode.InternalServerError)
    {
        Content = new StringContent("boom")
    };

    private const string AnalyticsJson = """
    {
      "result": {
        "requests": { "all": 1000, "cached": 800, "uncached": 200, "ssl": {}, "http_status": {} },
        "bandwidth": { "all": 5000, "cached": 4000, "uncached": 1000 },
        "threats": { "all": 3, "type": {} },
        "pageviews": { "all": 500, "search_engine": {} }
      },
      "success": true,
      "errors": []
    }
    """;

    private const string SecurityLevelJson = """
    { "result": { "value": "high", "editable": true }, "success": true, "errors": [] }
    """;

    private const string SslJson = """
    { "result": { "value": "full", "editable": true, "certificate_status": "active" }, "success": true, "errors": [] }
    """;

    private static string ZoneJson(string zoneId) => $$"""
    {
      "result": {
        "id": "{{zoneId}}",
        "name": "example.com",
        "status": "active",
        "plan": { "id": "free", "name": "Free" },
        "name_servers": ["ns1.example.com", "ns2.example.com"]
      },
      "success": true,
      "errors": []
    }
    """;

    /// Routes canned successful responses based on the endpoint path, mirroring
    /// what CloudflareService actually hits for each public method.
    private static HttpResponseMessage RouteDefault(HttpRequestMessage req)
    {
        var path = req.RequestUri!.AbsolutePath;
        if (path.Contains("/analytics/dashboard")) return Json(AnalyticsJson);
        if (path.Contains("/settings/security_level")) return Json(SecurityLevelJson);
        if (path.Contains("/settings/ssl")) return Json(SslJson);
        if (path.Contains("/settings/development_mode")) return Json("""{"result":{"value":"off"},"success":true,"errors":[]}""");
        if (path.Contains("/purge_cache")) return Json("""{"result":{"id":"1"},"success":true,"errors":[]}""");
        return Json(ZoneJson(ZoneId));
    }

    // --- IsEnabledAsync ---

    [Fact]
    public async Task IsEnabledAsync_EnabledWithApiToken_ReturnsTrue()
    {
        var sut = Build(new FakeHandler(RouteDefault), new CloudflareSettings { Enabled = true, ApiToken = "tok" });

        (await sut.IsEnabledAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task IsEnabledAsync_Disabled_ReturnsFalse()
    {
        var sut = Build(new FakeHandler(RouteDefault), new CloudflareSettings { Enabled = false, ApiToken = "tok" });

        (await sut.IsEnabledAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task IsEnabledAsync_EnabledWithoutApiToken_ReturnsFalse()
    {
        var sut = Build(new FakeHandler(RouteDefault), new CloudflareSettings { Enabled = true, ApiToken = "" });

        (await sut.IsEnabledAsync()).Should().BeFalse();
    }

    // --- GetAnalyticsAsync ---

    [Fact]
    public async Task GetAnalyticsAsync_Disabled_ReturnsNullWithoutHttpCall()
    {
        var handler = new FakeHandler(RouteDefault);
        var sut = Build(handler, new CloudflareSettings { Enabled = false });

        var result = await sut.GetAnalyticsAsync();

        result.Should().BeNull();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetAnalyticsAsync_Success_ReturnsParsedAnalyticsAndSendsAuthHeader()
    {
        var handler = new FakeHandler(RouteDefault);
        var sut = Build(handler);

        var result = await sut.GetAnalyticsAsync(days: 7);

        result.Should().NotBeNull();
        result!.Requests.All.Should().Be(1000);
        result.Requests.Cached.Should().Be(800);
        result.Bandwidth.All.Should().Be(5000);
        result.Threats.All.Should().Be(3);
        result.PageViews.All.Should().Be(500);
        handler.AuthorizationHeaders[0]!.ToString().Should().Be("Bearer test-token");
    }

    [Fact]
    public async Task GetAnalyticsAsync_NonSuccessStatus_ReturnsNull()
    {
        var sut = Build(new FakeHandler(_ => ServerError()));

        (await sut.GetAnalyticsAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetAnalyticsAsync_HandlerThrows_ReturnsNull()
    {
        var sut = Build(new FakeHandler(_ => throw new HttpRequestException("network down")));

        (await sut.GetAnalyticsAsync()).Should().BeNull();
    }

    // --- GetDashboardDataAsync ---

    [Fact]
    public async Task GetDashboardDataAsync_Disabled_ReturnsNull()
    {
        var sut = Build(new FakeHandler(RouteDefault), new CloudflareSettings { Enabled = false });

        (await sut.GetDashboardDataAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetDashboardDataAsync_Enabled_AggregatesAllSubResults()
    {
        var sut = Build(new FakeHandler(RouteDefault));

        var result = await sut.GetDashboardDataAsync();

        result.Should().NotBeNull();
        result!.Analytics.Should().NotBeNull();
        result.SecurityLevel.Should().NotBeNull();
        result.CacheStats.Should().NotBeNull();
        result.SslInfo.Should().NotBeNull();
        result.ZoneInfo.Should().NotBeNull();
        result.EscalationStatus.Should().NotBeNull();
        result.LastUpdated.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    // --- UpdateSecurityLevelAsync ---

    [Theory]
    [InlineData(CloudflareSecurityLevel.Off, "off")]
    [InlineData(CloudflareSecurityLevel.EssentiallyOff, "essentially_off")]
    [InlineData(CloudflareSecurityLevel.Low, "low")]
    [InlineData(CloudflareSecurityLevel.Medium, "medium")]
    [InlineData(CloudflareSecurityLevel.High, "high")]
    [InlineData(CloudflareSecurityLevel.UnderAttack, "under_attack")]
    public async Task UpdateSecurityLevelAsync_MapsEnumToApiStringValue(CloudflareSecurityLevel level, string expectedValue)
    {
        var handler = new FakeHandler(RouteDefault);
        var sut = Build(handler);

        var result = await sut.UpdateSecurityLevelAsync(level);

        result.Should().BeTrue();
        handler.RequestBodies[0].Should().Contain($"\"value\":\"{expectedValue}\"");
    }

    [Fact]
    public async Task UpdateSecurityLevelAsync_UnmappedEnumValue_DefaultsToMedium()
    {
        var handler = new FakeHandler(RouteDefault);
        var sut = Build(handler);

        var result = await sut.UpdateSecurityLevelAsync((CloudflareSecurityLevel)999);

        result.Should().BeTrue();
        handler.RequestBodies[0].Should().Contain("\"value\":\"medium\"");
    }

    [Fact]
    public async Task UpdateSecurityLevelAsync_Disabled_ReturnsFalseWithoutHttpCall()
    {
        var handler = new FakeHandler(RouteDefault);
        var sut = Build(handler, new CloudflareSettings { Enabled = false });

        var result = await sut.UpdateSecurityLevelAsync(CloudflareSecurityLevel.High);

        result.Should().BeFalse();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdateSecurityLevelAsync_NonSuccessStatus_ReturnsFalse()
    {
        var sut = Build(new FakeHandler(_ => ServerError()));

        (await sut.UpdateSecurityLevelAsync(CloudflareSecurityLevel.High)).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateSecurityLevelAsync_HandlerThrows_ReturnsFalse()
    {
        var sut = Build(new FakeHandler(_ => throw new InvalidOperationException("boom")));

        (await sut.UpdateSecurityLevelAsync(CloudflareSecurityLevel.High)).Should().BeFalse();
    }

    // --- GetCurrentSecurityLevelAsync ---

    [Fact]
    public async Task GetCurrentSecurityLevelAsync_Disabled_ReturnsNull()
    {
        var sut = Build(new FakeHandler(RouteDefault), new CloudflareSettings { Enabled = false });

        (await sut.GetCurrentSecurityLevelAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentSecurityLevelAsync_Success_ReturnsValue()
    {
        var sut = Build(new FakeHandler(RouteDefault));

        var result = await sut.GetCurrentSecurityLevelAsync();

        result.Should().NotBeNull();
        result!.Value.Should().Be("high");
        result.Editable.Should().BeTrue();
    }

    [Fact]
    public async Task GetCurrentSecurityLevelAsync_NonSuccessStatus_ReturnsNull()
    {
        var sut = Build(new FakeHandler(_ => ServerError()));

        (await sut.GetCurrentSecurityLevelAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentSecurityLevelAsync_HandlerThrows_ReturnsNull()
    {
        var sut = Build(new FakeHandler(_ => throw new HttpRequestException("down")));

        (await sut.GetCurrentSecurityLevelAsync()).Should().BeNull();
    }

    // --- PurgeCacheAsync ---

    [Fact]
    public async Task PurgeCacheAsync_Disabled_ReturnsFalseWithoutHttpCall()
    {
        var handler = new FakeHandler(RouteDefault);
        var sut = Build(handler, new CloudflareSettings { Enabled = false });

        (await sut.PurgeCacheAsync()).Should().BeFalse();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task PurgeCacheAsync_NoUrls_PurgesEverything()
    {
        var handler = new FakeHandler(RouteDefault);
        var sut = Build(handler);

        var result = await sut.PurgeCacheAsync();

        result.Should().BeTrue();
        handler.RequestBodies[0].Should().Contain("purge_everything");
    }

    [Fact]
    public async Task PurgeCacheAsync_EmptyUrlList_PurgesEverything()
    {
        var handler = new FakeHandler(RouteDefault);
        var sut = Build(handler);

        var result = await sut.PurgeCacheAsync(new List<string>());

        result.Should().BeTrue();
        handler.RequestBodies[0].Should().Contain("purge_everything");
    }

    [Fact]
    public async Task PurgeCacheAsync_WithUrls_PurgesSpecificFiles()
    {
        var handler = new FakeHandler(RouteDefault);
        var sut = Build(handler);

        var result = await sut.PurgeCacheAsync(new List<string> { "https://example.com/a.js" });

        result.Should().BeTrue();
        handler.RequestBodies[0].Should().Contain("files").And.Contain("a.js");
    }

    [Fact]
    public async Task PurgeCacheAsync_NonSuccessStatus_ReturnsFalse()
    {
        var sut = Build(new FakeHandler(_ => ServerError()));

        (await sut.PurgeCacheAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task PurgeCacheAsync_HandlerThrows_ReturnsFalse()
    {
        var sut = Build(new FakeHandler(_ => throw new InvalidOperationException("boom")));

        (await sut.PurgeCacheAsync()).Should().BeFalse();
    }

    // --- SetDevelopmentModeAsync ---

    [Fact]
    public async Task SetDevelopmentModeAsync_Disabled_ReturnsFalseWithoutHttpCall()
    {
        var handler = new FakeHandler(RouteDefault);
        var sut = Build(handler, new CloudflareSettings { Enabled = false });

        (await sut.SetDevelopmentModeAsync(true)).Should().BeFalse();
        handler.CallCount.Should().Be(0);
    }

    [Theory]
    [InlineData(true, "on")]
    [InlineData(false, "off")]
    public async Task SetDevelopmentModeAsync_EnabledOrDisabled_SendsCorrectValue(bool enabled, string expected)
    {
        var handler = new FakeHandler(RouteDefault);
        var sut = Build(handler);

        var result = await sut.SetDevelopmentModeAsync(enabled);

        result.Should().BeTrue();
        handler.RequestBodies[0].Should().Contain($"\"value\":\"{expected}\"");
    }

    [Fact]
    public async Task SetDevelopmentModeAsync_NonSuccessStatus_ReturnsFalse()
    {
        var sut = Build(new FakeHandler(_ => ServerError()));

        (await sut.SetDevelopmentModeAsync(true)).Should().BeFalse();
    }

    [Fact]
    public async Task SetDevelopmentModeAsync_HandlerThrows_ReturnsFalse()
    {
        var sut = Build(new FakeHandler(_ => throw new InvalidOperationException("boom")));

        (await sut.SetDevelopmentModeAsync(true)).Should().BeFalse();
    }

    // --- GetCacheStatsAsync ---

    [Fact]
    public async Task GetCacheStatsAsync_AnalyticsUnavailable_ReturnsNull()
    {
        var sut = Build(new FakeHandler(RouteDefault), new CloudflareSettings { Enabled = false });

        (await sut.GetCacheStatsAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetCacheStatsAsync_WithRequests_ComputesHitRate()
    {
        var sut = Build(new FakeHandler(RouteDefault));

        var result = await sut.GetCacheStatsAsync();

        result.Should().NotBeNull();
        result!.TotalRequests.Should().Be(1000);
        result.CachedRequests.Should().Be(800);
        result.UncachedRequests.Should().Be(200);
        result.CacheHitRate.Should().Be(80.0);
    }

    [Fact]
    public async Task GetCacheStatsAsync_NoRequests_HitRateIsZero()
    {
        const string zeroAnalytics = """
        {
          "result": {
            "requests": { "all": 0, "cached": 0, "uncached": 0, "ssl": {}, "http_status": {} },
            "bandwidth": { "all": 0, "cached": 0, "uncached": 0 },
            "threats": { "all": 0, "type": {} },
            "pageviews": { "all": 0, "search_engine": {} }
          },
          "success": true,
          "errors": []
        }
        """;
        var sut = Build(new FakeHandler(_ => Json(zeroAnalytics)));

        var result = await sut.GetCacheStatsAsync();

        result.Should().NotBeNull();
        result!.CacheHitRate.Should().Be(0);
    }

    // --- Escalation ---

    [Fact]
    public async Task EscalateToUnderAttackAsync_AutoEscalationDisabled_ReturnsFalseWithoutHttpCall()
    {
        var handler = new FakeHandler(RouteDefault);
        var sut = Build(handler, new CloudflareSettings
        {
            Enabled = true,
            ApiToken = "tok",
            ZoneId = ZoneId,
            AutoEscalation = new AutoEscalationSettings { Enabled = false }
        });

        var result = await sut.EscalateToUnderAttackAsync();

        result.Should().BeFalse();
        handler.CallCount.Should().Be(0);
    }

    [Theory]
    [InlineData("off")]
    [InlineData("essentially_off")]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("under_attack")]
    [InlineData("some-unrecognized-value")] // exercises ParseSecurityLevel's default -> Medium branch
    public async Task EscalateToUnderAttackAsync_ParsesEveryCurrentSecurityLevelValue(string currentLevelValue)
    {
        var handler = new FakeHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.Contains("/settings/security_level") && req.Method == HttpMethod.Get)
            {
                return Json($$"""{ "result": { "value": "{{currentLevelValue}}", "editable": true }, "success": true, "errors": [] }""");
            }
            return RouteDefault(req);
        });
        var sut = Build(handler, new CloudflareSettings
        {
            Enabled = true,
            ApiToken = "tok",
            ZoneId = ZoneId,
            AutoEscalation = new AutoEscalationSettings { Enabled = true, NotifyOnEscalation = false }
        });

        var result = await sut.EscalateToUnderAttackAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task EscalateToUnderAttackAsync_Success_ReturnsTrueAndSetsUnderAttack()
    {
        var handler = new FakeHandler(RouteDefault);
        var sut = Build(handler, new CloudflareSettings
        {
            Enabled = true,
            ApiToken = "tok",
            ZoneId = ZoneId,
            AutoEscalation = new AutoEscalationSettings { Enabled = true, NotifyOnEscalation = false }
        });

        var result = await sut.EscalateToUnderAttackAsync();

        result.Should().BeTrue();
        handler.RequestBodies.Should().Contain(b => b.Contains("under_attack"));
    }

    [Fact]
    public async Task EscalateToUnderAttackAsync_UpdateFails_ReturnsFalse()
    {
        var sut = Build(new FakeHandler(req =>
                req.RequestUri!.AbsolutePath.Contains("/settings/security_level") && req.Method == HttpMethod.Patch
                    ? ServerError()
                    : RouteDefault(req)),
            new CloudflareSettings
            {
                Enabled = true,
                ApiToken = "tok",
                ZoneId = ZoneId,
                AutoEscalation = new AutoEscalationSettings { Enabled = true, NotifyOnEscalation = false }
            });

        var result = await sut.EscalateToUnderAttackAsync();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task EscalateToUnderAttackAsync_NotifyOnEscalation_ResolvesNotificationServiceFromScope()
    {
        var handler = new FakeHandler(RouteDefault);
        var serviceProvider = BuildScopedServiceProvider(Substitute.For<INotificationService>());
        var sut = Build(handler, new CloudflareSettings
        {
            Enabled = true,
            ApiToken = "tok",
            ZoneId = ZoneId,
            AutoEscalation = new AutoEscalationSettings { Enabled = true, NotifyOnEscalation = true }
        }, serviceProvider);

        var result = await sut.EscalateToUnderAttackAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task EscalateToUnderAttackAsync_NotifyOnEscalation_ScopeCreationThrows_StillReturnsTrue()
    {
        var handler = new FakeHandler(RouteDefault);
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IServiceScopeFactory))
            .Returns(_ => throw new InvalidOperationException("no scope factory"));
        var sut = Build(handler, new CloudflareSettings
        {
            Enabled = true,
            ApiToken = "tok",
            ZoneId = ZoneId,
            AutoEscalation = new AutoEscalationSettings { Enabled = true, NotifyOnEscalation = true }
        }, serviceProvider);

        var result = await sut.EscalateToUnderAttackAsync();

        // The notification failure is swallowed internally; escalation itself already succeeded.
        result.Should().BeTrue();
    }

    [Fact]
    public async Task DeEscalateFromUnderAttackAsync_Success_ReturnsTrue()
    {
        var handler = new FakeHandler(RouteDefault);
        var sut = Build(handler);

        var result = await sut.DeEscalateFromUnderAttackAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task DeEscalateFromUnderAttackAsync_UpdateFails_ReturnsFalse()
    {
        var sut = Build(new FakeHandler(_ => ServerError()));

        (await sut.DeEscalateFromUnderAttackAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task GetEscalationStatusAsync_AfterDeEscalation_IsNotEscalated()
    {
        var handler = new FakeHandler(RouteDefault);
        var sut = Build(handler);
        await sut.DeEscalateFromUnderAttackAsync();

        var status = await sut.GetEscalationStatusAsync();

        status.IsEscalated.Should().BeFalse();
    }

    [Fact]
    public async Task GetEscalationStatusAsync_AfterEscalationWithinWindow_IsEscalatedWithRemainingTime()
    {
        var handler = new FakeHandler(RouteDefault);
        var sut = Build(handler, new CloudflareSettings
        {
            Enabled = true,
            ApiToken = "tok",
            ZoneId = ZoneId,
            AutoEscalation = new AutoEscalationSettings { Enabled = true, NotifyOnEscalation = false, AutoDeEscalateAfterMinutes = 60 }
        });
        await sut.EscalateToUnderAttackAsync();

        var status = await sut.GetEscalationStatusAsync();

        status.IsEscalated.Should().BeTrue();
        status.AutoDeEscalateIn.Should().NotBeNull();
        status.AutoDeEscalateIn!.Value.Should().BePositive();
    }

    [Fact]
    public async Task GetEscalationStatusAsync_WindowExpired_AutoDeEscalates()
    {
        var handler = new FakeHandler(RouteDefault);
        var settings = new CloudflareSettings
        {
            Enabled = true,
            ApiToken = "tok",
            ZoneId = ZoneId,
            AutoEscalation = new AutoEscalationSettings { Enabled = true, NotifyOnEscalation = false, AutoDeEscalateAfterMinutes = 0 }
        };
        var sut = Build(handler, settings);
        await sut.EscalateToUnderAttackAsync();

        var status = await sut.GetEscalationStatusAsync();

        status.IsEscalated.Should().BeFalse();
    }

    // --- GetSslTlsInfoAsync ---

    [Fact]
    public async Task GetSslTlsInfoAsync_Disabled_ReturnsNull()
    {
        var sut = Build(new FakeHandler(RouteDefault), new CloudflareSettings { Enabled = false });

        (await sut.GetSslTlsInfoAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetSslTlsInfoAsync_Success_ReturnsParsedInfo()
    {
        var sut = Build(new FakeHandler(RouteDefault));

        var result = await sut.GetSslTlsInfoAsync();

        result.Should().NotBeNull();
        result!.Value.Should().Be("full");
        result.CertificateStatus.Should().Be("active");
    }

    [Fact]
    public async Task GetSslTlsInfoAsync_NonSuccessStatus_ReturnsNull()
    {
        var sut = Build(new FakeHandler(_ => ServerError()));

        (await sut.GetSslTlsInfoAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetSslTlsInfoAsync_HandlerThrows_ReturnsNull()
    {
        var sut = Build(new FakeHandler(_ => throw new InvalidOperationException("boom")));

        (await sut.GetSslTlsInfoAsync()).Should().BeNull();
    }

    // --- UpdateSslModeAsync ---

    [Fact]
    public async Task UpdateSslModeAsync_Disabled_ReturnsFalseWithoutHttpCall()
    {
        var handler = new FakeHandler(RouteDefault);
        var sut = Build(handler, new CloudflareSettings { Enabled = false });

        (await sut.UpdateSslModeAsync("full")).Should().BeFalse();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdateSslModeAsync_Success_SendsModeAndReturnsTrue()
    {
        var handler = new FakeHandler(RouteDefault);
        var sut = Build(handler);

        var result = await sut.UpdateSslModeAsync("strict");

        result.Should().BeTrue();
        handler.RequestBodies[0].Should().Contain("strict");
    }

    [Fact]
    public async Task UpdateSslModeAsync_NonSuccessStatus_ReturnsFalse()
    {
        var sut = Build(new FakeHandler(_ => ServerError()));

        (await sut.UpdateSslModeAsync("strict")).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateSslModeAsync_HandlerThrows_ReturnsFalse()
    {
        var sut = Build(new FakeHandler(_ => throw new InvalidOperationException("boom")));

        (await sut.UpdateSslModeAsync("strict")).Should().BeFalse();
    }

    // --- GetZoneInfoAsync ---

    [Fact]
    public async Task GetZoneInfoAsync_Disabled_ReturnsNull()
    {
        var sut = Build(new FakeHandler(RouteDefault), new CloudflareSettings { Enabled = false });

        (await sut.GetZoneInfoAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetZoneInfoAsync_Success_ReturnsParsedZone()
    {
        var sut = Build(new FakeHandler(RouteDefault));

        var result = await sut.GetZoneInfoAsync();

        result.Should().NotBeNull();
        result!.Id.Should().Be(ZoneId);
        result.Name.Should().Be("example.com");
        result.Plan.Name.Should().Be("Free");
        result.NameServers.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetZoneInfoAsync_NonSuccessStatus_ReturnsNull()
    {
        var sut = Build(new FakeHandler(_ => ServerError()));

        (await sut.GetZoneInfoAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetZoneInfoAsync_HandlerThrows_ReturnsNull()
    {
        var sut = Build(new FakeHandler(_ => throw new InvalidOperationException("boom")));

        (await sut.GetZoneInfoAsync()).Should().BeNull();
    }

    // --- GetRequestHeadersAsync ---

    [Fact]
    public async Task GetRequestHeadersAsync_Disabled_ReturnsEmptyDictionary()
    {
        var sut = Build(new FakeHandler(RouteDefault), new CloudflareSettings { Enabled = false });
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["CF-Connecting-IP"] = "1.2.3.4";

        var result = await sut.GetRequestHeadersAsync(ctx);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRequestHeadersAsync_Enabled_ExtractsOnlyCfPrefixedHeaders()
    {
        var sut = Build(new FakeHandler(RouteDefault));
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["CF-Connecting-IP"] = "1.2.3.4";
        ctx.Request.Headers["CF-IPCountry"] = "DE";
        ctx.Request.Headers["X-Other-Header"] = "ignored";

        var result = await sut.GetRequestHeadersAsync(ctx);

        result.Should().ContainKey("CF-Connecting-IP").WhoseValue.Should().Be("1.2.3.4");
        result.Should().ContainKey("CF-IPCountry").WhoseValue.Should().Be("DE");
        result.Should().NotContainKey("X-Other-Header");
    }

    [Fact]
    public async Task GetRequestHeadersAsync_NoCfHeaders_ReturnsEmptyDictionary()
    {
        var sut = Build(new FakeHandler(RouteDefault));
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Other-Header"] = "ignored";

        var result = await sut.GetRequestHeadersAsync(ctx);

        result.Should().BeEmpty();
    }
}
