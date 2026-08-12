using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Application.Services;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Json;

namespace LagersystemLVHome.Middleware;

/// <summary>
/// Rate limiting middleware for API endpoints.
/// </summary>
public class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RateLimitSettings _settings;
    private readonly ILogger<RateLimitMiddleware> _logger;

    public RateLimitMiddleware(
        RequestDelegate next,
        IOptions<RateLimitSettings> settings,
        ILogger<RateLimitMiddleware> logger)
    {
        _next = next;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IRateLimitService rateLimitService)
    {
        var isWebRequest = IsWebRequest(context);

        // Skip rate limiting for non-API requests
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        var identifier = GetIdentifier(context);
        var endpoint = context.Request.Path.Value ?? "/";
        var role = context.User.FindFirst(ClaimTypes.Role)?.Value;

        var result = await rateLimitService.CheckRateLimitAsync(identifier, endpoint, role, isWebRequest);

        if (!result.IsSuccess)
        {
            await HandleRateLimitExceeded(context, result);
            return;
        }

        AddRateLimitHeaders(context.Response, result);

        await _next(context);
    }

    private bool IsWebRequest(HttpContext context)
    {
        var acceptHeader = context.Request.Headers["Accept"].ToString();
        if (acceptHeader.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            return true;

        var contentType = context.Request.ContentType;
        if (contentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        if (context.Request.Path.StartsWithSegments("/_blazor"))
            return true;

        if (!context.Request.Path.StartsWithSegments("/api"))
            return true;

        return false;
    }

    private string GetIdentifier(HttpContext context)
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            return $"user:{userId}";
        }

        if (context.Request.Headers.TryGetValue("X-Api-Key", out var apiKey))
        {
            return $"apikey:{apiKey}";
        }

        if (context.Request.Headers.TryGetValue("X-Access-Token", out var token))
        {
            return $"token:{token}";
        }

        var ipAddress = GetIpAddress(context);

        var displayIp = ipAddress switch
        {
            "::1" => "localhost-ipv6",
            "127.0.0.1" => "localhost-ipv4",
            _ => ipAddress
        };

        return $"ip:{displayIp}";
    }

    private string GetIpAddress(HttpContext context)
    {
        // Priority: Forwarded-For > Real-IP > RemoteIpAddress
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            var ip = forwardedFor.Split(',')[0].Trim();
            if (!string.IsNullOrEmpty(ip) && ip != "::1" && ip != "127.0.0.1")
            {
                return ip;
            }
        }

        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp) && realIp != "::1" && realIp != "127.0.0.1")
        {
            return realIp;
        }

        var remoteIp = context.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrEmpty(remoteIp))
        {
            if (remoteIp == "::1")
                return "::1";

            if (remoteIp == "127.0.0.1")
                return "127.0.0.1";

            // Map ::ffff:x.x.x.x to x.x.x.x
            if (remoteIp.StartsWith("::ffff:"))
            {
                return remoteIp.Substring(7);
            }

            return remoteIp;
        }

        return "unknown";
    }

    private async Task HandleRateLimitExceeded(HttpContext context, RateLimitResult result)
    {
        context.Response.StatusCode = _settings.StatusCode;
        context.Response.ContentType = "application/json";

        if (result.RetryAfter.HasValue)
        {
            context.Response.Headers.Append("Retry-After", ((int)result.RetryAfter.Value.TotalSeconds).ToString());
            context.Response.Headers.Append("X-RateLimit-Remaining", "0");
            context.Response.Headers.Append("X-RateLimit-Reset", DateTimeOffset.UtcNow.Add(result.RetryAfter.Value).ToUnixTimeSeconds().ToString());
        }

        var response = new
        {
            error = "Rate limit exceeded",
            message = result.Message ?? "Too many requests. Please try again later.",
            retryAfter = result.RetryAfter?.TotalSeconds,
            remainingRequests = result.RemainingRequests
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response));

        _logger.LogWarning("Rate limit exceeded: {Path} | IP: {IP} | User: {User}",
            context.Request.Path,
            GetIpAddress(context),
            context.User.Identity?.Name ?? "Anonymous");
    }

    private void AddRateLimitHeaders(HttpResponse response, RateLimitResult result)
    {
        response.Headers.Append("X-RateLimit-Remaining", result.RemainingRequests.ToString());

        if (result.RetryAfter.HasValue)
        {
            var resetTime = DateTimeOffset.UtcNow.Add(result.RetryAfter.Value).ToUnixTimeSeconds();
            response.Headers.Append("X-RateLimit-Reset", resetTime.ToString());
        }
    }
}

public static class RateLimitMiddlewareExtensions
{
    public static IApplicationBuilder UseRateLimit(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RateLimitMiddleware>();
    }
}
