using System.Net;
using System.Net.Http;
using System.Text.Json;
using LagersystemLVHome.Application.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LagersystemLVHome.UnitTests.Services.Notification;

public class TeamsServiceTests
{
    // Records every posted payload so tests can assert on the JSON body sent to the webhook.
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<string> RequestBodies { get; } = new();
        public List<string?> RequestUris { get; } = new();
        public int CallCount { get; private set; }

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUris.Add(request.RequestUri?.ToString());
            if (request.Content != null)
            {
                RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }
            return _responder(request);
        }
    }

    private static TeamsService Build(
        FakeHandler handler,
        TeamsSettings? teamsSettings = null,
        NotificationChannels? channels = null)
    {
        var client = new HttpClient(handler);
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(client);
        httpClientFactory.CreateClient().Returns(client);

        var settings = teamsSettings ?? new TeamsSettings
        {
            EnableTeams = true,
            WebhookUrl = "https://teams.example.com/webhook",
            MaxRetries = 1
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailSettings:ApplicationUrl"] = "https://app.example.com"
            })
            .Build();

        return new TeamsService(
            Options.Create(settings),
            Options.Create(channels ?? new NotificationChannels()),
            NullLogger<TeamsService>.Instance,
            httpClientFactory,
            configuration);
    }

    private static HttpResponseMessage Ok() => new(HttpStatusCode.OK);
    private static HttpResponseMessage ServerError() => new(HttpStatusCode.InternalServerError)
    {
        Content = new StringContent("boom")
    };

    // --- IsEnabled / IsEnabledForType ---

    [Fact]
    public void IsEnabled_TeamsDisabled_ReturnsFalse()
    {
        var sut = Build(new FakeHandler(_ => Ok()), new TeamsSettings { EnableTeams = false, WebhookUrl = "https://x" });
        sut.IsEnabled().Should().BeFalse();
    }

    [Fact]
    public void IsEnabled_NoWebhookUrl_ReturnsFalse()
    {
        var sut = Build(new FakeHandler(_ => Ok()), new TeamsSettings { EnableTeams = true, WebhookUrl = "" });
        sut.IsEnabled().Should().BeFalse();
    }

    [Fact]
    public void IsEnabled_EnabledWithWebhook_ReturnsTrue()
    {
        var sut = Build(new FakeHandler(_ => Ok()), new TeamsSettings { EnableTeams = true, WebhookUrl = "https://x" });
        sut.IsEnabled().Should().BeTrue();
    }

    [Theory]
    [InlineData("lowstock", true, true, true)]
    [InlineData("lowstock", true, false, false)]
    [InlineData("expiry", true, true, true)]
    [InlineData("anomaly", true, true, true)]
    [InlineData("securityrisk", true, true, true)]
    [InlineData("system", true, true, true)]
    [InlineData("unknown-type", true, true, false)]
    public void IsEnabledForType_RespectsSettingsAndChannelConfig(
        string type, bool enableTeams, bool enableForType, bool expected)
    {
        var teamsSettings = new TeamsSettings
        {
            EnableTeams = enableTeams,
            WebhookUrl = "https://x",
            EnableForLowStock = enableForType,
            EnableForExpiry = enableForType,
            EnableForAnomalies = enableForType,
            EnableForSecurityRisks = enableForType,
            EnableForSystemAlerts = enableForType
        };
        var channels = new NotificationChannels();
        channels.LowStockAlerts.Teams = enableForType;
        channels.ExpiryAlerts.Teams = enableForType;
        channels.SecurityAlerts.Teams = enableForType;
        channels.SystemAlerts.Teams = enableForType;

        var sut = Build(new FakeHandler(_ => Ok()), teamsSettings, channels);

        sut.IsEnabledForType(type).Should().Be(expected);
    }

    [Fact]
    public void IsEnabledForType_GloballyDisabled_ReturnsFalseRegardlessOfType()
    {
        var sut = Build(new FakeHandler(_ => Ok()), new TeamsSettings { EnableTeams = false, WebhookUrl = "" });
        sut.IsEnabledForType("lowstock").Should().BeFalse();
    }

    [Fact]
    public void IsEnabledForType_IsCaseInsensitive()
    {
        var teamsSettings = new TeamsSettings { EnableTeams = true, WebhookUrl = "https://x", EnableForLowStock = true };
        var channels = new NotificationChannels();
        channels.LowStockAlerts.Teams = true;
        var sut = Build(new FakeHandler(_ => Ok()), teamsSettings, channels);

        sut.IsEnabledForType("LowStock").Should().BeTrue();
    }

    // --- SendMessageAsync ---

    [Fact]
    public async Task SendMessageAsync_Disabled_ReturnsFalseWithoutHttpCall()
    {
        var handler = new FakeHandler(_ => Ok());
        var sut = Build(handler, new TeamsSettings { EnableTeams = false, WebhookUrl = "" });

        var result = await sut.SendMessageAsync("Title", "Message");

        result.Should().BeFalse();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SendMessageAsync_Enabled_PostsAdaptiveCardAndReturnsTrue()
    {
        var handler = new FakeHandler(_ => Ok());
        var sut = Build(handler);

        var result = await sut.SendMessageAsync("Hello", "World");

        result.Should().BeTrue();
        handler.CallCount.Should().Be(1);
        handler.RequestBodies[0].Should().Contain("Hello").And.Contain("World").And.Contain("AdaptiveCard");
    }

    // --- SendLowStockAlertAsync ---

    [Fact]
    public async Task SendLowStockAlertAsync_DisabledForType_ReturnsFalseWithoutHttpCall()
    {
        var handler = new FakeHandler(_ => Ok());
        var channels = new NotificationChannels();
        channels.LowStockAlerts.Teams = false;
        var sut = Build(handler, channels: channels);

        var result = await sut.SendLowStockAlertAsync("Widget", 2, 10);

        result.Should().BeFalse();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SendLowStockAlertAsync_WithWarehouseName_IncludesWarehouseFact()
    {
        var handler = new FakeHandler(_ => Ok());
        var channels = new NotificationChannels();
        channels.LowStockAlerts.Teams = true;
        var sut = Build(handler, channels: channels);

        var result = await sut.SendLowStockAlertAsync("Widget", 2, 10, "Main Warehouse");

        result.Should().BeTrue();
        handler.RequestBodies[0].Should().Contain("Widget").And.Contain("Main Warehouse");
    }

    [Fact]
    public async Task SendLowStockAlertAsync_WithoutWarehouseName_OmitsWarehouseFact()
    {
        var handler = new FakeHandler(_ => Ok());
        var channels = new NotificationChannels();
        channels.LowStockAlerts.Teams = true;
        var sut = Build(handler, channels: channels);

        var result = await sut.SendLowStockAlertAsync("Widget", 2, 10);

        result.Should().BeTrue();
        handler.RequestBodies[0].Should().Contain("Widget").And.NotContain("Lager");
    }

    // --- SendExpiryAlertAsync ---

    [Theory]
    [InlineData(-1, "ABGELAUFEN")]
    [InlineData(3, "Kritisch")]
    [InlineData(30, "Warnung")]
    public async Task SendExpiryAlertAsync_UrgencyDependsOnDaysUntilExpiry(int daysFromNow, string expectedUrgency)
    {
        var handler = new FakeHandler(_ => Ok());
        var channels = new NotificationChannels();
        channels.ExpiryAlerts.Teams = true;
        var sut = Build(handler, channels: channels);

        var result = await sut.SendExpiryAlertAsync("Milk", DateTime.UtcNow.AddDays(daysFromNow), 5, "Fridge");

        result.Should().BeTrue();
        handler.RequestBodies[0].Should().Contain(expectedUrgency).And.Contain("Fridge");
    }

    [Fact]
    public async Task SendExpiryAlertAsync_DisabledForType_ReturnsFalse()
    {
        var handler = new FakeHandler(_ => Ok());
        var channels = new NotificationChannels();
        channels.ExpiryAlerts.Teams = false;
        var sut = Build(handler, channels: channels);

        var result = await sut.SendExpiryAlertAsync("Milk", DateTime.UtcNow.AddDays(1), 5);

        result.Should().BeFalse();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SendExpiryAlertAsync_WithoutLocation_OmitsLocationFact()
    {
        var handler = new FakeHandler(_ => Ok());
        var channels = new NotificationChannels();
        channels.ExpiryAlerts.Teams = true;
        var sut = Build(handler, channels: channels);

        var result = await sut.SendExpiryAlertAsync("Milk", DateTime.UtcNow.AddDays(1), 5);

        result.Should().BeTrue();
        handler.RequestBodies[0].Should().Contain("Milk").And.NotContain("Lagerort");
    }

    // --- SendAnomalyAlertAsync ---

    [Theory]
    [InlineData(0.9, "Kritisch")]
    [InlineData(0.7, "Hoch")]
    [InlineData(0.3, "Mittel")]
    public async Task SendAnomalyAlertAsync_SeverityDependsOnScore(double score, string expectedSeverity)
    {
        var handler = new FakeHandler(_ => Ok());
        var channels = new NotificationChannels();
        channels.SecurityAlerts.Teams = true;
        var teamsSettings = new TeamsSettings { EnableTeams = true, WebhookUrl = "https://x", EnableForAnomalies = true, MaxRetries = 1 };
        var sut = Build(handler, teamsSettings, channels);

        var result = await sut.SendAnomalyAlertAsync("Unusual", score, "Weird pattern detected", "Product #5");

        result.Should().BeTrue();
        handler.RequestBodies[0].Should().Contain(expectedSeverity).And.Contain("Product #5");
    }

    [Fact]
    public async Task SendAnomalyAlertAsync_WithoutAffectedEntity_OmitsFact()
    {
        var handler = new FakeHandler(_ => Ok());
        var channels = new NotificationChannels();
        channels.SecurityAlerts.Teams = true;
        var teamsSettings = new TeamsSettings { EnableTeams = true, WebhookUrl = "https://x", EnableForAnomalies = true, MaxRetries = 1 };
        var sut = Build(handler, teamsSettings, channels);

        var result = await sut.SendAnomalyAlertAsync("Unusual", 0.5, "Weird pattern detected");

        result.Should().BeTrue();
        handler.RequestBodies[0].Should().NotContain("Betroffenes Objekt");
    }

    [Fact]
    public async Task SendAnomalyAlertAsync_DisabledForType_ReturnsFalse()
    {
        var handler = new FakeHandler(_ => Ok());
        var channels = new NotificationChannels();
        channels.SecurityAlerts.Teams = false;
        var sut = Build(handler, channels: channels);

        var result = await sut.SendAnomalyAlertAsync("Unusual", 0.9, "desc");

        result.Should().BeFalse();
    }

    // --- SendSecurityRiskAlertAsync ---

    [Theory]
    [InlineData("critical", "KRITISCH")]
    [InlineData("high", "Hoch")]
    [InlineData("low", "Mittel")]
    public async Task SendSecurityRiskAlertAsync_SeverityDependsOnRiskLevel(string riskLevel, string expectedSeverity)
    {
        var handler = new FakeHandler(_ => Ok());
        var channels = new NotificationChannels();
        channels.SecurityAlerts.Teams = true;
        var teamsSettings = new TeamsSettings { EnableTeams = true, WebhookUrl = "https://x", EnableForSecurityRisks = true, MaxRetries = 1 };
        var sut = Build(handler, teamsSettings, channels);

        var result = await sut.SendSecurityRiskAlertAsync(
            "jdoe", riskLevel, 7.5, new List<string> { "f1", "f2", "f3", "f4" });

        result.Should().BeTrue();
        handler.RequestBodies[0].Should().Contain(expectedSeverity).And.Contain("jdoe");
        // Only the first 3 risk factors should be included (Take(3)).
        handler.RequestBodies[0].Should().Contain("f1").And.Contain("f2").And.Contain("f3").And.NotContain("f4");
    }

    [Fact]
    public async Task SendSecurityRiskAlertAsync_DisabledForType_ReturnsFalse()
    {
        var handler = new FakeHandler(_ => Ok());
        var channels = new NotificationChannels();
        channels.SecurityAlerts.Teams = false;
        var sut = Build(handler, channels: channels);

        var result = await sut.SendSecurityRiskAlertAsync("jdoe", "high", 5, new List<string>());

        result.Should().BeFalse();
    }

    // --- SendSystemAlertAsync ---

    [Theory]
    [InlineData("error")]
    [InlineData("warning")]
    [InlineData("info")]
    [InlineData("other")]
    public async Task SendSystemAlertAsync_MapsSeverityToColor(string severity)
    {
        var handler = new FakeHandler(_ => Ok());
        var channels = new NotificationChannels();
        channels.SystemAlerts.Teams = true;
        var teamsSettings = new TeamsSettings { EnableTeams = true, WebhookUrl = "https://x", EnableForSystemAlerts = true, MaxRetries = 1 };
        var sut = Build(handler, teamsSettings, channels);

        var result = await sut.SendSystemAlertAsync("System Down", "The system is down", severity);

        result.Should().BeTrue();
        handler.RequestBodies[0].Should().Contain("System Down").And.Contain(severity);
    }

    [Fact]
    public async Task SendSystemAlertAsync_DisabledForType_ReturnsFalse()
    {
        var handler = new FakeHandler(_ => Ok());
        var channels = new NotificationChannels();
        channels.SystemAlerts.Teams = false;
        var sut = Build(handler, channels: channels);

        var result = await sut.SendSystemAlertAsync("Title", "Msg", "info");

        result.Should().BeFalse();
    }

    // --- SendAdaptiveCardAsync ---

    [Fact]
    public async Task SendAdaptiveCardAsync_Disabled_ReturnsFalseWithoutHttpCall()
    {
        var handler = new FakeHandler(_ => Ok());
        var sut = Build(handler, new TeamsSettings { EnableTeams = false, WebhookUrl = "" });

        var result = await sut.SendAdaptiveCardAsync(new { type = "AdaptiveCard" });

        result.Should().BeFalse();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SendAdaptiveCardAsync_Enabled_PostsCardAndReturnsTrue()
    {
        var handler = new FakeHandler(_ => Ok());
        var sut = Build(handler);

        var customCard = new { type = "AdaptiveCard", body = new object[] { new { type = "TextBlock", text = "custom" } } };
        var result = await sut.SendAdaptiveCardAsync(customCard);

        result.Should().BeTrue();
        handler.RequestBodies[0].Should().Contain("custom");
    }

    // --- SendToTeamsAsync retry / failure behaviour ---

    [Fact]
    public async Task SendMessageAsync_SingleAttemptWebhookFailure_ReturnsFalseWithoutDelay()
    {
        // MaxRetries = 1 means the loop exits immediately after the first failure
        // (retries(1) < MaxRetries(1) is false), so no Task.Delay is invoked - keeps the test fast.
        var handler = new FakeHandler(_ => ServerError());
        var sut = Build(handler, new TeamsSettings { EnableTeams = true, WebhookUrl = "https://x", MaxRetries = 1 });

        var result = await sut.SendMessageAsync("Title", "Message");

        result.Should().BeFalse();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SendMessageAsync_FailsThenSucceeds_RetriesAndReturnsTrue()
    {
        // First call returns a server error (exercises the retry-with-delay branch), second call succeeds.
        var callNumber = 0;
        var handler = new FakeHandler(_ =>
        {
            callNumber++;
            return callNumber == 1 ? ServerError() : Ok();
        });
        var sut = Build(handler, new TeamsSettings { EnableTeams = true, WebhookUrl = "https://x", MaxRetries = 2 });

        var result = await sut.SendMessageAsync("Title", "Message");

        result.Should().BeTrue();
        handler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task SendMessageAsync_ThrowsThenSucceeds_RetriesAndReturnsTrue()
    {
        // First call throws (exercises the catch-block retry-with-delay branch), second call succeeds.
        var callNumber = 0;
        var handler = new FakeHandler(_ =>
        {
            callNumber++;
            if (callNumber == 1)
            {
                throw new HttpRequestException("network error");
            }
            return Ok();
        });
        var sut = Build(handler, new TeamsSettings { EnableTeams = true, WebhookUrl = "https://x", MaxRetries = 2 });

        var result = await sut.SendMessageAsync("Title", "Message");

        result.Should().BeTrue();
        handler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task SendMessageAsync_AllAttemptsThrow_ReturnsFalseAfterExhaustingRetries()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("network error"));
        var sut = Build(handler, new TeamsSettings { EnableTeams = true, WebhookUrl = "https://x", MaxRetries = 1 });

        var result = await sut.SendMessageAsync("Title", "Message");

        result.Should().BeFalse();
        handler.CallCount.Should().Be(1);
    }
}
