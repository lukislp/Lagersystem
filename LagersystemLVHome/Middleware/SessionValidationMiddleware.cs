using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.Middleware;

/// <summary>
/// Server-side session enforcement middleware.
/// Validates the session on every HTTP request.
/// On invalid session: deletes cookie + returns 401.
/// Covers page navigation, SignalR reconnect and static resources.
/// For WebSocket-based Blazor interactions the SessionMonitorService is used.
/// </summary>
public class SessionValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SessionValidationMiddleware> _logger;

    private static readonly string[] _allowedPaths =
    [
        "/login",
        "/logout",
        "/register",
        "/setup",
        "/forgot-password",
        "/reset-password",
        "/privacy",
        "/api/auth/login",
        "/api/auth/register",
        "/api/session/check",
        "/_blazor",
        "/_framework",
        "/css",
        "/js",
        "/images",
        "/favicon",
        "/manifest",
        "/service-worker"
    ];

    public SessionValidationMiddleware(RequestDelegate next, ILogger<SessionValidationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IDbContextFactory<InventoryDbContext> contextFactory)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

        if (IsAllowedPath(path))
        {
            await _next(context);
            return;
        }

        // Skip for API requests with API key or bearer token (own auth)
        if (context.Request.Headers.ContainsKey("X-API-Key") ||
            context.Request.Headers.ContainsKey("Authorization"))
        {
            await _next(context);
            return;
        }

        var sessionCookie = context.Request.Cookies["LagerSystem.SessionId"];

        if (string.IsNullOrEmpty(sessionCookie))
        {
            // No session cookie = not logged in, let downstream middleware handle it
            await _next(context);
            return;
        }

        try
        {
            await using var dbContext = await contextFactory.CreateDbContextAsync();

            var session = await dbContext.UserSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.SessionId == sessionCookie);

            if (session == null)
            {
                _logger.LogWarning("SessionValidation: session not found in DB: {SessionId}",
                    sessionCookie.Substring(0, Math.Min(8, sessionCookie.Length)) + "...");

                await InvalidateSessionAsync(context, "Session not found");
                return;
            }

            if (!session.IsActive)
            {
                _logger.LogWarning("SessionValidation: session inactive: {SessionId}, Reason: {Reason}",
                    sessionCookie.Substring(0, Math.Min(8, sessionCookie.Length)) + "...",
                    session.EndReason);

                await InvalidateSessionAsync(context, $"Session ended: {session.EndReason}");
                return;
            }

            // Check inactivity timeout (30 minutes)
            var inactivityTimeout = TimeSpan.FromMinutes(30);
            if (DateTime.UtcNow - session.LastActivity > inactivityTimeout)
            {
                _logger.LogWarning("SessionValidation: session timeout due to inactivity: {SessionId}",
                    sessionCookie.Substring(0, Math.Min(8, sessionCookie.Length)) + "...");

                var sessionToUpdate = await dbContext.UserSessions
                    .FirstOrDefaultAsync(s => s.SessionId == sessionCookie);

                if (sessionToUpdate != null)
                {
                    sessionToUpdate.IsActive = false;
                    sessionToUpdate.EndTime = DateTime.UtcNow;
                    sessionToUpdate.EndReason = SessionEndReason.Timeout;
                    sessionToUpdate.EndReasonDetails = "Inactivity timeout (30 minutes)";
                    await dbContext.SaveChangesAsync();
                }

                await InvalidateSessionAsync(context, "Session timeout");
                return;
            }

            // Session valid - store info in HttpContext for downstream middleware
            context.Items["ValidatedSessionId"] = sessionCookie;
            context.Items["ValidatedUserId"] = session.UserId;
            context.Items["SessionLastActivity"] = session.LastActivity;

            _logger.LogDebug("SessionValidation: session valid for user {UserId}", session.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SessionValidation: error validating session");
            // Fail-open: let the request through, Blazor components will re-check
        }

        await _next(context);
    }

    private bool IsAllowedPath(string path)
    {
        return _allowedPaths.Any(allowed => path.StartsWith(allowed, StringComparison.OrdinalIgnoreCase));
    }

    private async Task InvalidateSessionAsync(HttpContext context, string reason)
    {
        context.Response.Cookies.Delete("LagerSystem.SessionId", new CookieOptions
        {
            Path = "/",
            Secure = true,
            HttpOnly = true,
            SameSite = SameSiteMode.Lax
        });

        context.Items["SessionInvalidReason"] = reason;

        var isBlazorRequest = context.Request.Path.StartsWithSegments("/_blazor");
        var isAjaxRequest = context.Request.Headers["X-Requested-With"] == "XMLHttpRequest";
        var acceptsHtml = context.Request.Headers["Accept"].ToString().Contains("text/html");

        if (isBlazorRequest || isAjaxRequest)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers["X-Session-Invalid"] = "true";
            context.Response.Headers["X-Session-Invalid-Reason"] = reason;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Session invalid",
                reason,
                redirectTo = "/login?reason=session-expired"
            });
        }
        else if (acceptsHtml)
        {
            context.Response.Redirect($"/login?reason=session-expired&message={Uri.EscapeDataString(reason)}");
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync($"Session invalid: {reason}");
        }
    }
}

public static class SessionValidationMiddlewareExtensions
{
    public static IApplicationBuilder UseSessionValidation(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<SessionValidationMiddleware>();
    }
}
