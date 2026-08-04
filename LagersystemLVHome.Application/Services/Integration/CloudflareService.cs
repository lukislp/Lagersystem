using Microsoft.Extensions.Options;
using LagersystemLVHome.Application.Configuration;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LagersystemLVHome.Application.Services;

public sealed class CloudflareService : ICloudflareService
{
    private readonly ILogger<CloudflareService> _logger;
    private readonly CloudflareSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceProvider _serviceProvider;
    private static DateTime? _lastEscalation;
    private static CloudflareSecurityLevel _previousSecurityLevel = CloudflareSecurityLevel.Medium;

    public CloudflareService(
        ILogger<CloudflareService> logger,
        IOptions<CloudflareSettings> settings,
        IHttpClientFactory httpClientFactory,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
        _serviceProvider = serviceProvider;
    }

    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_settings.Enabled && !string.IsNullOrEmpty(_settings.ApiToken));
    }

    /// <summary>
    /// Retrieves Cloudflare analytics for the specified number of days.
    /// </summary>
    public async Task<CloudflareAnalytics?> GetAnalyticsAsync(int days = 1, CancellationToken cancellationToken = default)
    {
        if (!await IsEnabledAsync())
            return null;

        try
        {
            var client = CreateAuthenticatedClient();
            var since = DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-ddTHH:mm:ssZ");
            var until = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

            var url = $"https://api.cloudflare.com/client/v4/zones/{_settings.ZoneId}/analytics/dashboard?since={since}&until={until}";
            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Cloudflare API error: {StatusCode}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<CloudflareApiResponse<CloudflareAnalytics>>(json, GetJsonOptions());

            return result?.Result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Cloudflare analytics");
            return null;
        }
    }

    /// <summary>
    /// Retrieves comprehensive dashboard data from all Cloudflare endpoints.
    /// </summary>
    public async Task<CloudflareDashboardData?> GetDashboardDataAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsEnabledAsync())
            return null;

        try
        {
            var dashboardData = new CloudflareDashboardData
            {
                Analytics = await GetAnalyticsAsync(1),
                SecurityLevel = await GetCurrentSecurityLevelAsync(),
                CacheStats = await GetCacheStatsAsync(),
                SslInfo = await GetSslTlsInfoAsync(),
                ZoneInfo = await GetZoneInfoAsync(),
                EscalationStatus = await GetEscalationStatusAsync(),
                LastUpdated = DateTime.UtcNow
            };

            return dashboardData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting dashboard data");
            return null;
        }
    }

    public async Task<bool> UpdateSecurityLevelAsync(CloudflareSecurityLevel level, CancellationToken cancellationToken = default)
    {
        if (!await IsEnabledAsync())
            return false;

        try
        {
            var client = CreateAuthenticatedClient();
            var levelString = level switch
            {
                CloudflareSecurityLevel.Off => "off",
                CloudflareSecurityLevel.EssentiallyOff => "essentially_off",
                CloudflareSecurityLevel.Low => "low",
                CloudflareSecurityLevel.Medium => "medium",
                CloudflareSecurityLevel.High => "high",
                CloudflareSecurityLevel.UnderAttack => "under_attack",
                _ => "medium"
            };

            var url = $"https://api.cloudflare.com/client/v4/zones/{_settings.ZoneId}/settings/security_level";
            var content = new StringContent(
                JsonSerializer.Serialize(new { value = levelString }, GetJsonOptions()),
                System.Text.Encoding.UTF8,
                "application/json"
            );

            var response = await client.PatchAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Cloudflare security level changed to: {Level}", level);
                return true;
            }

            _logger.LogError("Cloudflare API error: {StatusCode}", response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating Cloudflare security level");
            return false;
        }
    }

    public async Task<SecurityLevelInfo?> GetCurrentSecurityLevelAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsEnabledAsync())
            return null;

        try
        {
            var client = CreateAuthenticatedClient();
            var url = $"https://api.cloudflare.com/client/v4/zones/{_settings.ZoneId}/settings/security_level";
            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<CloudflareApiResponse<SecurityLevelInfo>>(json, GetJsonOptions());

            return result?.Result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting security level");
            return null;
        }
    }

    /// <summary>
    /// Purges the Cloudflare cache (all or specific URLs).
    /// </summary>
    public async Task<bool> PurgeCacheAsync(List<string>? urls = null, CancellationToken cancellationToken = default)
    {
        if (!await IsEnabledAsync())
            return false;

        try
        {
            var client = CreateAuthenticatedClient();
            var url = $"https://api.cloudflare.com/client/v4/zones/{_settings.ZoneId}/purge_cache";

            var requestBody = urls == null || !urls.Any()
                ? new { purge_everything = true }
                : (object)new { files = urls };

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody, GetJsonOptions()),
                System.Text.Encoding.UTF8,
                "application/json"
            );

            var response = await client.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Cloudflare cache purged");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error purging cache");
            return false;
        }
    }

    /// <summary>
    /// Toggles Cloudflare development mode.
    /// </summary>
    public async Task<bool> SetDevelopmentModeAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (!await IsEnabledAsync())
            return false;

        try
        {
            var client = CreateAuthenticatedClient();
            var url = $"https://api.cloudflare.com/client/v4/zones/{_settings.ZoneId}/settings/development_mode";
            var content = new StringContent(
                JsonSerializer.Serialize(new { value = enabled ? "on" : "off" }, GetJsonOptions()),
                System.Text.Encoding.UTF8,
                "application/json"
            );

            var response = await client.PatchAsync(url, content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting development mode");
            return false;
        }
    }

    public async Task<CacheStats?> GetCacheStatsAsync(CancellationToken cancellationToken = default)
    {
        var analytics = await GetAnalyticsAsync(1);
        if (analytics == null)
            return null;

        return new CacheStats
        {
            TotalRequests = analytics.Requests.All,
            CachedRequests = analytics.Requests.Cached,
            UncachedRequests = analytics.Requests.Uncached,
            CacheHitRate = analytics.Requests.All > 0
                ? (double)analytics.Requests.Cached / analytics.Requests.All * 100
                : 0
        };
    }

    /// <summary>
    /// Escalates to "Under Attack" mode.
    /// </summary>
    public async Task<bool> EscalateToUnderAttackAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.AutoEscalation.Enabled)
            return false;

        _logger.LogWarning("Escalating to 'Under Attack' mode");

        // Save the current security level for later de-escalation
        var currentLevel = await GetCurrentSecurityLevelAsync();
        if (currentLevel != null)
        {
            _previousSecurityLevel = ParseSecurityLevel(currentLevel.Value);
        }

        var success = await UpdateSecurityLevelAsync(CloudflareSecurityLevel.UnderAttack);

        if (success)
        {
            _lastEscalation = DateTime.UtcNow;

            if (_settings.AutoEscalation.NotifyOnEscalation)
            {
                await SendEscalationNotificationAsync();
            }
        }

        return success;
    }

    /// <summary>
    /// De-escalates from "Under Attack" mode to the previous security level.
    /// </summary>
    public async Task<bool> DeEscalateFromUnderAttackAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("De-escalating from 'Under Attack' mode");

        var success = await UpdateSecurityLevelAsync(_previousSecurityLevel);

        if (success)
        {
            _lastEscalation = null;
        }

        return success;
    }

    /// <summary>
    /// Checks the current escalation status and auto-de-escalates if needed.
    /// </summary>
    public async Task<EscalationStatus> GetEscalationStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = new EscalationStatus
        {
            IsEscalated = false,
            EscalatedAt = _lastEscalation,
            AutoDeEscalateIn = null
        };

        if (_lastEscalation.HasValue)
        {
            status.IsEscalated = true;
            var elapsed = DateTime.UtcNow - _lastEscalation.Value;
            var remaining = TimeSpan.FromMinutes(_settings.AutoEscalation.AutoDeEscalateAfterMinutes) - elapsed;

            if (remaining > TimeSpan.Zero)
            {
                status.AutoDeEscalateIn = remaining;
            }
            else if (_settings.AutoEscalation.Enabled)
            {
                await DeEscalateFromUnderAttackAsync();
                status.IsEscalated = false;
            }
        }

        return status;
    }

    public async Task<SslTlsInfo?> GetSslTlsInfoAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsEnabledAsync())
            return null;

        try
        {
            var client = CreateAuthenticatedClient();
            var url = $"https://api.cloudflare.com/client/v4/zones/{_settings.ZoneId}/settings/ssl";
            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<CloudflareApiResponse<SslTlsInfo>>(json, GetJsonOptions());

            return result?.Result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting SSL/TLS info");
            return null;
        }
    }

    public async Task<bool> UpdateSslModeAsync(string mode, CancellationToken cancellationToken = default)
    {
        if (!await IsEnabledAsync())
            return false;

        try
        {
            var client = CreateAuthenticatedClient();
            var url = $"https://api.cloudflare.com/client/v4/zones/{_settings.ZoneId}/settings/ssl";
            var content = new StringContent(
                JsonSerializer.Serialize(new { value = mode }, GetJsonOptions()),
                System.Text.Encoding.UTF8,
                "application/json"
            );

            var response = await client.PatchAsync(url, content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating SSL mode");
            return false;
        }
    }

    public async Task<ZoneInfo?> GetZoneInfoAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsEnabledAsync())
            return null;

        try
        {
            var client = CreateAuthenticatedClient();
            var url = $"https://api.cloudflare.com/client/v4/zones/{_settings.ZoneId}";
            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<CloudflareApiResponse<ZoneInfo>>(json, GetJsonOptions());

            return result?.Result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting zone info");
            return null;
        }
    }

    /// <summary>
    /// Extracts all Cloudflare headers from the request.
    /// </summary>
    public Task<Dictionary<string, string>> GetRequestHeadersAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        var headers = new Dictionary<string, string>();

        if (!_settings.Enabled)
            return Task.FromResult(headers);

        foreach (var header in context.Request.Headers.Where(h => h.Key.StartsWith("CF-")))
        {
            headers[header.Key] = header.Value.ToString();
        }

        return Task.FromResult(headers);
    }

    // Private helpers

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_settings.ApiToken}");
        return client;
    }

    private JsonSerializerOptions GetJsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    private CloudflareSecurityLevel ParseSecurityLevel(string value)
    {
        return value switch
        {
            "off" => CloudflareSecurityLevel.Off,
            "essentially_off" => CloudflareSecurityLevel.EssentiallyOff,
            "low" => CloudflareSecurityLevel.Low,
            "medium" => CloudflareSecurityLevel.Medium,
            "high" => CloudflareSecurityLevel.High,
            "under_attack" => CloudflareSecurityLevel.UnderAttack,
            _ => CloudflareSecurityLevel.Medium
        };
    }

    private async Task SendEscalationNotificationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var notificationService = scope.ServiceProvider.GetService<INotificationService>();

            if (notificationService != null)
            {
                _logger.LogInformation("Escalation notification sent");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending escalation notification");
        }
    }
}

// Data models

public sealed class CloudflareAnalytics
{
    [JsonPropertyName("requests")]
    public RequestStats Requests { get; set; } = new();

    [JsonPropertyName("bandwidth")]
    public BandwidthStats Bandwidth { get; set; } = new();

    [JsonPropertyName("threats")]
    public ThreatStats Threats { get; set; } = new();

    [JsonPropertyName("pageviews")]
    public PageViewStats PageViews { get; set; } = new();
}

public sealed class RequestStats
{
    [JsonPropertyName("all")]
    public long All { get; set; }

    [JsonPropertyName("cached")]
    public long Cached { get; set; }

    [JsonPropertyName("uncached")]
    public long Uncached { get; set; }

    [JsonPropertyName("ssl")]
    public Dictionary<string, long> Ssl { get; set; } = new();

    [JsonPropertyName("http_status")]
    public Dictionary<string, long> HttpStatus { get; set; } = new();
}

public sealed class BandwidthStats
{
    [JsonPropertyName("all")]
    public long All { get; set; }

    [JsonPropertyName("cached")]
    public long Cached { get; set; }

    [JsonPropertyName("uncached")]
    public long Uncached { get; set; }
}

public sealed class ThreatStats
{
    [JsonPropertyName("all")]
    public long All { get; set; }

    [JsonPropertyName("type")]
    public Dictionary<string, long> Type { get; set; } = new();
}

public sealed class PageViewStats
{
    [JsonPropertyName("all")]
    public long All { get; set; }

    [JsonPropertyName("search_engine")]
    public Dictionary<string, long> SearchEngine { get; set; } = new();
}

public sealed class CloudflareDashboardData
{
    public CloudflareAnalytics? Analytics { get; set; }
    public SecurityLevelInfo? SecurityLevel { get; set; }
    public CacheStats? CacheStats { get; set; }
    public SslTlsInfo? SslInfo { get; set; }
    public ZoneInfo? ZoneInfo { get; set; }
    public EscalationStatus EscalationStatus { get; set; } = new();
    public DateTime LastUpdated { get; set; }
}

public sealed class SecurityLevelInfo
{
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("editable")]
    public bool Editable { get; set; }
}

public sealed class CacheStats
{
    public long TotalRequests { get; set; }
    public long CachedRequests { get; set; }
    public long UncachedRequests { get; set; }
    public double CacheHitRate { get; set; }
}

public sealed class SslTlsInfo
{
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("editable")]
    public bool Editable { get; set; }

    [JsonPropertyName("certificate_status")]
    public string CertificateStatus { get; set; } = string.Empty;
}

public sealed class ZoneInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("plan")]
    public PlanInfo Plan { get; set; } = new();

    [JsonPropertyName("name_servers")]
    public List<string> NameServers { get; set; } = new();
}

public sealed class PlanInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public sealed class EscalationStatus
{
    public bool IsEscalated { get; set; }
    public DateTime? EscalatedAt { get; set; }
    public TimeSpan? AutoDeEscalateIn { get; set; }
}

internal class CloudflareApiResponse<T>
{
    [JsonPropertyName("result")]
    public T? Result { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = new();
}
