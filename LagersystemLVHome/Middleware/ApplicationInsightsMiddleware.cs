using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.Application.Services;
using System.Security.Claims;
using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.Middleware;

public class ApplicationInsightsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApplicationInsightsMiddleware> _logger;

    // Paths excluded from session tracking
    private static readonly string[] _excludedFromSessionTracking =
    [
        "/api/session/check",
        "/api/health",
        "/_blazor",
        "/_framework"
    ];

    public ApplicationInsightsMiddleware(RequestDelegate next, ILogger<ApplicationInsightsMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IApplicationInsightsService insightsService,
        IGeoLocationService geoLocationService,
        IRateLimitService rateLimitService,
        ISessionManagementService sessionManagementService)
    {
        var startTime = DateTime.UtcNow;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var path = context.Request.Path.ToString().ToLowerInvariant();

        try
        {
            var isExcludedFromSessionTracking = _excludedFromSessionTracking
                .Any(excluded => path.StartsWith(excluded, StringComparison.OrdinalIgnoreCase));

            if (!isExcludedFromSessionTracking)
            {
                await DetectSessionHijackingAsync(context, sessionManagementService);
            }

            // Track page view for Blazor pages
            if (context.Request.Path.StartsWithSegments("/_framework") == false &&
                context.Request.Path.StartsWithSegments("/_blazor") == false &&
                context.Request.Path.StartsWithSegments("/api") == false)
            {
                await TrackPageViewAsync(context, insightsService, geoLocationService);
                await TrackPageViewForRateLimitAsync(context, rateLimitService);

                if (!isExcludedFromSessionTracking)
                {
                    await UpdateSessionActivityAsync(context, sessionManagementService);
                }
            }

            // Track API requests
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                await _next(context);
                stopwatch.Stop();

                if (!isExcludedFromSessionTracking)
                {
                    await TrackApiRequestAsync(context, insightsService, stopwatch.ElapsedMilliseconds, startTime);
                    await UpdateSessionActivityAsync(context, sessionManagementService);
                }
            }
            else
            {
                await _next(context);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            if (context.Request.Path.StartsWithSegments("/api"))
            {
                await TrackApiRequestAsync(context, insightsService, stopwatch.ElapsedMilliseconds, startTime, ex);
            }

            throw;
        }
    }

    /// <summary>
    /// Updates session activity on each request.
    /// API sessions are skipped (browser sessions only).
    /// Uses DeviceFingerprint for correct session assignment.
    /// </summary>
    private async Task UpdateSessionActivityAsync(HttpContext httpContext, ISessionManagementService sessionService)
    {
        try
        {
            if (!httpContext.User.Identity?.IsAuthenticated ?? true)
                return;

            var authType = httpContext.User.FindFirst("AuthenticationType")?.Value;
            if (authType == "ApiKey")
            {
                _logger.LogDebug("Skipping session activity update for API-Key authenticated request");
                return;
            }

            var userIdForConsent = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userIdForConsent) && int.TryParse(userIdForConsent, out var userIdConsent))
            {
                var dbContext = httpContext.RequestServices.GetRequiredService<InventoryDbContext>();
                var currentUser = await dbContext.Users.FindAsync(userIdConsent);

                if (currentUser == null || !currentUser.DeviceFingerprintConsent)
                {
                    _logger.LogDebug("Session activity tracking skipped for user {UserId} - no device fingerprint consent", userIdConsent);
                    return;
                }
            }

            var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            // Check X-Forwarded-For header (IIS/nginx proxy)
            var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwardedFor))
            {
                ipAddress = forwardedFor.Split(',')[0].Trim();
            }

            var userAgent = httpContext.Request.Headers["User-Agent"].ToString();

            var sessionId = httpContext.Items["SessionId"]?.ToString();

            var deviceFingerprint = httpContext.Request.Cookies["DeviceFingerprint"];

            _logger.LogDebug("UpdateSessionActivity: Cookie DeviceFingerprint = {Fingerprint}, HttpContext.Items = {Items}",
                deviceFingerprint?[..Math.Min(16, deviceFingerprint?.Length ?? 0)] ?? "NULL",
                httpContext.Items["DeviceFingerprint"]?.ToString() ?? "NULL");

            // Fallback: find active session by UserId and DeviceFingerprint
            if (string.IsNullOrEmpty(sessionId))
            {
                var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!string.IsNullOrEmpty(userIdClaim) && int.TryParse(userIdClaim, out var userId))
                {
                    var session = await sessionService.GetSessionByUserAndFingerprintAsync(userId, deviceFingerprint, onlyActive: true);

                    if (session != null)
                    {
                        sessionId = session.SessionId;
                        httpContext.Items["SessionId"] = sessionId;

                        _logger.LogDebug("Found session {SessionId} for user {UserId} with fingerprint match: {Match}",
                            sessionId, userId,
                            !string.IsNullOrEmpty(deviceFingerprint) && session.DeviceFingerprint == deviceFingerprint ? "EXACT" : "FALLBACK");
                    }
                    else
                    {
                        _logger.LogWarning("No session found for user {UserId} with fingerprint {Fingerprint}",
                            userId, deviceFingerprint?[..Math.Min(16, deviceFingerprint?.Length ?? 0)] ?? "NULL");
                    }
                }
            }

            // Skip API sessions
            if (!string.IsNullOrEmpty(sessionId) && sessionId.StartsWith("api-"))
            {
                _logger.LogDebug("Skipping session activity update for API session {SessionId}", sessionId);
                return;
            }

            if (!string.IsNullOrEmpty(sessionId))
            {
                var pageUrl = httpContext.Request.Path.ToString();
                await UpdateSessionWithClientInfoAsync(sessionService, sessionId, pageUrl, ipAddress, userAgent);
                await UpdateDeviceFingerprintIfNeededAsync(httpContext, sessionService, sessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update session activity");
        }
    }

    private async Task UpdateSessionWithClientInfoAsync(
        ISessionManagementService sessionService,
        string sessionId,
        string? pageUrl,
        string ipAddress,
        string userAgent)
    {
        try
        {
            var session = await sessionService.GetSessionAsync(sessionId);
            if (session == null)
                return;

            await sessionService.UpdateSessionActivityAsync(sessionId, pageUrl);

            var needsUpdate = false;

            if (session.IpAddress != ipAddress && ipAddress != "Unknown")
            {
                _logger.LogInformation("IP changed for session {SessionId}: {OldIp} -> {NewIp}",
                    sessionId, session.IpAddress, ipAddress);
                needsUpdate = true;
            }

            if (session.UserAgent != userAgent && !string.IsNullOrEmpty(userAgent) && userAgent != "Unknown")
            {
                _logger.LogInformation("User-Agent changed for session {SessionId}", sessionId);
                needsUpdate = true;
            }

            if (needsUpdate)
            {
                await sessionService.DetectSessionHijackingAsync(sessionId, ipAddress, userAgent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update session with client info");
        }
    }

    private async Task UpdateDeviceFingerprintIfNeededAsync(
        HttpContext httpContext,
        ISessionManagementService sessionService,
        string sessionId)
    {
        try
        {
            var session = await sessionService.GetSessionAsync(sessionId);
            if (session == null)
                return;

            if (string.IsNullOrEmpty(session.DeviceFingerprint))
            {
                _logger.LogInformation("Device fingerprint missing for session {SessionId}, generating now", sessionId);

                var fingerprintService = httpContext.RequestServices
                    .GetRequiredService<IDeviceFingerprintService>();

                var fingerprint = fingerprintService.GenerateFingerprint(httpContext);

                await fingerprintService.SaveDeviceFingerprintAsync(
                    session.Id,
                    fingerprint,
                    httpContext);

                _logger.LogInformation("Device fingerprint updated for session {SessionId}", sessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update device fingerprint for session {SessionId}", sessionId);
        }
    }

    private async Task DetectSessionHijackingAsync(HttpContext httpContext, ISessionManagementService sessionService)
    {
        try
        {
            if (!httpContext.User.Identity?.IsAuthenticated ?? true)
                return;

            var sessionId = httpContext.Items["SessionId"]?.ToString();
            if (string.IsNullOrEmpty(sessionId))
                return;

            var currentIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwardedFor))
            {
                currentIp = forwardedFor.Split(',')[0].Trim();
            }

            var currentUserAgent = httpContext.Request.Headers["User-Agent"].ToString();

            var isHijacked = await sessionService.DetectSessionHijackingAsync(sessionId, currentIp, currentUserAgent);

            if (isHijacked)
            {
                _logger.LogWarning("Possible session hijacking detected for session {SessionId}", sessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detect session hijacking");
        }
    }

    /// <summary>
    /// Logs Blazor page views in the RateLimitService for threat detection.
    /// </summary>
    private async Task TrackPageViewForRateLimitAsync(HttpContext httpContext, IRateLimitService rateLimitService)
    {
        try
        {
            var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();

            if (!string.IsNullOrEmpty(forwardedFor))
            {
                ipAddress = forwardedFor.Split(',')[0].Trim();
            }

            var identifier = $"ip:{ipAddress}";
            var endpoint = httpContext.Request.Path.ToString();
            var role = httpContext.User.FindFirst(ClaimTypes.Role)?.Value;

            await rateLimitService.CheckRateLimitAsync(identifier, endpoint, role, isWebRequest: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to track page view for rate limiting");
        }
    }

    private async Task TrackPageViewAsync(
        HttpContext httpContext,
        IApplicationInsightsService insightsService,
        IGeoLocationService geoLocationService)
    {
        try
        {
            var user = httpContext.User;
            if (!user.Identity?.IsAuthenticated ?? true)
                return;

            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId) || userId == 0)
                return;

            // Check analytics consent
            var dbContext = httpContext.RequestServices.GetRequiredService<InventoryDbContext>();
            var currentUser = await dbContext.Users.FindAsync(userId);

            if (currentUser == null || !currentUser.AnalyticsConsent)
            {
                _logger.LogDebug("Page view tracking skipped for user {UserId} - no analytics consent", userId);
                return;
            }

            if (!httpContext.Session.IsAvailable)
            {
                await httpContext.Session.LoadAsync();
            }

            var sessionId = GetOrCreatePersistentSessionId(httpContext);
            var userAgent = httpContext.Request.Headers["User-Agent"].ToString();
            var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            var geoLocation = await geoLocationService.GetLocationFromIpAsync(ipAddress);

            var pageView = new PageView
            {
                UserId = userId,
                Username = user.Identity.Name ?? "Unknown",
                UserRole = user.FindFirst(ClaimTypes.Role)?.Value ?? "User",
                PageUrl = httpContext.Request.Path,
                PageTitle = GetPageTitle(httpContext.Request.Path),
                Referrer = httpContext.Request.Headers["Referer"].ToString(),
                SessionId = sessionId,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                DeviceType = GetDeviceType(userAgent),
                Browser = GetBrowser(userAgent),
                OperatingSystem = GetOS(userAgent),
                Timestamp = DateTime.UtcNow,
                LoadTimeMs = Random.Shared.Next(50, 500),
                Country = geoLocation.Country,
                City = geoLocation.City,
                WarehouseId = int.TryParse(user.FindFirst("WarehouseId")?.Value, out var whId) ? whId : null,
                WarehouseName = user.FindFirst("WarehouseName")?.Value
            };

            await insightsService.TrackPageViewAsync(pageView);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error tracking page view");
        }
    }

    private string GetOrCreatePersistentSessionId(HttpContext httpContext)
    {
        const string cookieName = "InsightsSessionId";

        if (httpContext.Request.Cookies.TryGetValue(cookieName, out var existingSessionId) &&
            !string.IsNullOrEmpty(existingSessionId))
        {
            return existingSessionId;
        }

        var newSessionId = Guid.NewGuid().ToString("N");

        httpContext.Response.Cookies.Append(cookieName, newSessionId, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddMinutes(30),
            IsEssential = true
        });

        return newSessionId;
    }

    private async Task TrackApiRequestAsync(
        HttpContext httpContext,
        IApplicationInsightsService insightsService,
        long durationMs,
        DateTime timestamp,
        Exception? exception = null)
    {
        try
        {
            var apiRequest = new ApiRequest
            {
                Endpoint = httpContext.Request.Path,
                Method = httpContext.Request.Method,
                StatusCode = httpContext.Response.StatusCode,
                DurationMs = (int)durationMs,
                Timestamp = timestamp,
                IpAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
                UserAgent = httpContext.Request.Headers["User-Agent"].ToString(),
                IsAuthenticated = httpContext.User.Identity?.IsAuthenticated ?? false,
                IsError = exception != null || httpContext.Response.StatusCode >= 400,
                ErrorMessage = exception?.Message,
                StackTrace = exception?.StackTrace
            };

            if (httpContext.User.Identity?.IsAuthenticated ?? false)
            {
                apiRequest.TokenUserId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            }

            await insightsService.TrackApiRequestAsync(apiRequest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error tracking API request");
        }
    }

    private string GetPageTitle(string path)
    {
        return path switch
        {
            "/" => "Dashboard",
            "/products" => "Products",
            "/categories" => "Categories",
            "/scanner" => "Scanner",
            "/storage-locations" => "Storage Locations",
            "/movements" => "Movements",
            "/low-stock" => "Low Stock",
            "/expiry-monitoring" => "Expiry Monitoring",
            "/ml-test-dashboard" => "ML Dashboard",
            "/security-center" => "Security Center",
            "/admin" => "Admin",
            "/profile" => "Profile",
            "/ai-assistant" => "AI Assistant",
            "/admin/insights" => "Application Insights",
            _ => path
        };
    }

    private string GetDeviceType(string userAgent)
    {
        var ua = userAgent.ToLower();
        if (ua.Contains("mobile") || ua.Contains("android") || ua.Contains("iphone"))
            return "Mobile";
        if (ua.Contains("tablet") || ua.Contains("ipad"))
            return "Tablet";
        return "Desktop";
    }

    private string GetBrowser(string userAgent)
    {
        var ua = userAgent.ToLower();
        if (ua.Contains("edg")) return "Edge";
        if (ua.Contains("chrome")) return "Chrome";
        if (ua.Contains("firefox")) return "Firefox";
        if (ua.Contains("safari")) return "Safari";
        if (ua.Contains("opera")) return "Opera";
        return "Other";
    }

    private string GetOS(string userAgent)
    {
        var ua = userAgent.ToLower();
        if (ua.Contains("windows")) return "Windows";
        if (ua.Contains("mac")) return "macOS";
        if (ua.Contains("linux")) return "Linux";
        if (ua.Contains("android")) return "Android";
        if (ua.Contains("ios") || ua.Contains("iphone") || ua.Contains("ipad")) return "iOS";
        return "Other";
    }
}