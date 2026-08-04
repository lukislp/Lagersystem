using Microsoft.Extensions.Options;
using LagersystemLVHome.Application.Configuration;

namespace LagersystemLVHome.Middleware;

/// <summary>
/// Cloudflare security middleware.
/// Uses Cloudflare headers for bot protection, DDoS mitigation and geo-blocking.
/// Fully optional - only active when Cloudflare is enabled in settings.
/// </summary>
public class CloudflareSecurityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CloudflareSecurityMiddleware> _logger;
    private readonly CloudflareSettings _settings;

    private const string CF_BOT_SCORE = "CF-Bot-Management-Score";
    private const string CF_THREAT_SCORE = "CF-Threat-Score";
    private const string CF_COUNTRY = "CF-IPCountry";
    private const string CF_CONNECTING_IP = "CF-Connecting-IP";
    private const string CF_RAY_ID = "CF-Ray";
    private const string CF_VISITOR = "CF-Visitor";

    public CloudflareSecurityMiddleware(
        RequestDelegate next,
        ILogger<CloudflareSecurityMiddleware> logger,
        IOptions<CloudflareSettings> settings)
    {
        _next = next;
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_settings.Enabled)
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.ContainsKey(CF_RAY_ID))
        {
            _logger.LogWarning("Cloudflare enabled but request has no CF-Ray header (direct access)");
        }

        try
        {
            if (_settings.BotProtection.Enabled)
            {
                var botCheckResult = await CheckBotProtection(context);
                if (!botCheckResult.Allowed)
                {
                    await ForbidRequest(context, botCheckResult.Reason);
                    return;
                }
            }

            if (_settings.DDoSProtection.Enabled)
            {
                var ddosCheckResult = await CheckDDoSProtection(context);
                if (!ddosCheckResult.Allowed)
                {
                    await ForbidRequest(context, ddosCheckResult.Reason);
                    return;
                }
            }

            if (_settings.GeoLocation.Enabled)
            {
                var geoCheckResult = await CheckGeoLocation(context);
                if (!geoCheckResult.Allowed)
                {
                    await ForbidRequest(context, geoCheckResult.Reason);
                    return;
                }
            }

            if (_settings.Challenge.Enabled && _settings.Challenge.ValidateChallengePassage)
            {
                var challengeResult = await ValidateChallengePassage(context);
                if (!challengeResult.Passed)
                {
                    _logger.LogWarning("Challenge not passed for {IP} - Ray: {Ray}",
                        GetClientIP(context), GetRayId(context));
                }
            }

            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Cloudflare security middleware");
            // Fail-open strategy
            await _next(context);
        }
    }

    private Task<SecurityCheckResult> CheckBotProtection(HttpContext context)
    {
        var botScoreHeader = context.Request.Headers[CF_BOT_SCORE].FirstOrDefault();

        if (string.IsNullOrEmpty(botScoreHeader) && _settings.BotProtection.RequireBotScoreHeader)
        {
            _logger.LogWarning("Bot score header missing for {IP}", GetClientIP(context));
            return Task.FromResult(new SecurityCheckResult
            {
                Allowed = false,
                Reason = "Bot-Score Header required but missing"
            });
        }

        if (int.TryParse(botScoreHeader, out var botScore))
        {
            _logger.LogDebug("Bot score for {IP}: {Score}", GetClientIP(context), botScore);

            if (botScore < _settings.BotProtection.MinimumBotScore)
            {
                _logger.LogWarning("Bot blocked: {IP} (Score: {Score}, Ray: {Ray})",
                    GetClientIP(context), botScore, GetRayId(context));

                return Task.FromResult(new SecurityCheckResult
                {
                    Allowed = false,
                    Reason = $"Bot detected (Score: {botScore})"
                });
            }

            if (botScore < _settings.BotProtection.SuspiciousBotScoreThreshold)
            {
                _logger.LogInformation("Suspicious bot score: {IP} (Score: {Score})",
                    GetClientIP(context), botScore);
            }
        }

        return Task.FromResult(new SecurityCheckResult { Allowed = true });
    }

    private Task<SecurityCheckResult> CheckDDoSProtection(HttpContext context)
    {
        var threatScoreHeader = context.Request.Headers[CF_THREAT_SCORE].FirstOrDefault();

        if (int.TryParse(threatScoreHeader, out var threatScore))
        {
            _logger.LogDebug("Threat score for {IP}: {Score}", GetClientIP(context), threatScore);

            if (threatScore > _settings.DDoSProtection.ThreatScoreThreshold)
            {
                _logger.LogWarning("High threat score blocked: {IP} (Score: {Score}, Ray: {Ray})",
                    GetClientIP(context), threatScore, GetRayId(context));

                return Task.FromResult(new SecurityCheckResult
                {
                    Allowed = !_settings.DDoSProtection.BlockHighThreatScore,
                    Reason = $"High threat score detected ({threatScore})"
                });
            }
        }

        return Task.FromResult(new SecurityCheckResult { Allowed = true });
    }

    private Task<SecurityCheckResult> CheckGeoLocation(HttpContext context)
    {
        var country = context.Request.Headers[CF_COUNTRY].FirstOrDefault();

        if (string.IsNullOrEmpty(country))
        {
            return Task.FromResult(new SecurityCheckResult { Allowed = true });
        }

        if (_settings.GeoLocation.BlockedCountries.Any() &&
            _settings.GeoLocation.BlockedCountries.Contains(country, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Access from blocked country: {Country} - {IP} (Ray: {Ray})",
                country, GetClientIP(context), GetRayId(context));

            return Task.FromResult(new SecurityCheckResult
            {
                Allowed = false,
                Reason = $"Access from blocked country: {country}"
            });
        }

        if (_settings.GeoLocation.AllowedCountries.Any() &&
            !_settings.GeoLocation.AllowedCountries.Contains(country, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Access from non-allowed country: {Country} - {IP} (Ray: {Ray})",
                country, GetClientIP(context), GetRayId(context));

            return Task.FromResult(new SecurityCheckResult
            {
                Allowed = false,
                Reason = "Access only allowed from specific countries"
            });
        }

        _logger.LogDebug("Access from {Country}: {IP}", country, GetClientIP(context));

        return Task.FromResult(new SecurityCheckResult { Allowed = true });
    }

    private Task<ChallengeResult> ValidateChallengePassage(HttpContext context)
    {
        var challengePassed = context.Request.Cookies.ContainsKey("cf_clearance");

        return Task.FromResult(new ChallengeResult
        {
            Passed = challengePassed
        });
    }

    private async Task ForbidRequest(HttpContext context, string reason)
    {
        context.Response.StatusCode = 403;
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync($"Access Denied: {reason}");

        _logger.LogWarning("Request blocked: {IP} - {Reason} (Ray: {Ray})",
            GetClientIP(context), reason, GetRayId(context));
    }

    private string GetClientIP(HttpContext context)
    {
        return context.Request.Headers[CF_CONNECTING_IP].FirstOrDefault()
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "Unknown";
    }

    private string GetRayId(HttpContext context)
    {
        return context.Request.Headers[CF_RAY_ID].FirstOrDefault() ?? "N/A";
    }
}

internal class SecurityCheckResult
{
    public bool Allowed { get; set; }
    public string Reason { get; set; } = string.Empty;
}

internal class ChallengeResult
{
    public bool Passed { get; set; }
}
