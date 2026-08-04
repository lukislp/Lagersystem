using LagersystemLVHome.Application.Services;

namespace LagersystemLVHome.Middleware;

/// <summary>
/// Session-cookie-based connection mapping.
/// Handles connection ID changes on mobile while keeping the session ID stable.
/// </summary>
public class CircuitConnectionMappingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CircuitConnectionMappingMiddleware> _logger;
    private const string SESSION_COOKIE_NAME = "LagerSystem.SessionId";

    public CircuitConnectionMappingMiddleware(
        RequestDelegate next,
        ILogger<CircuitConnectionMappingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, CircuitUserStore circuitUserStore)
    {
        try
        {
            var connectionId = context.Connection.Id;

            // Try circuit ID from HttpContext.Items (set by circuit handler)
            if (context.Items.TryGetValue("CircuitId", out var circuitIdObj) &&
                circuitIdObj is string circuitId &&
                !string.IsNullOrEmpty(circuitId))
            {
                circuitUserStore.SetCurrentCircuitId(circuitId);

                var sessionId = circuitUserStore.GetSessionId();

                if (!string.IsNullOrEmpty(sessionId))
                {
                    context.Response.Cookies.Append(SESSION_COOKIE_NAME, sessionId, new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = true,
                        SameSite = SameSiteMode.Lax,
                        IsEssential = true
                    });

                    _logger.LogDebug("Session cookie set: {SessionId} for circuit: {CircuitId}, connection: {ConnectionId}",
                        sessionId, circuitId, connectionId);
                }

                _logger.LogDebug("Circuit ID from HttpContext.Items - Connection: {ConnectionId} -> Circuit: {CircuitId}",
                    connectionId, circuitId);
            }
            // Fallback: session cookie-based restoration for connection ID changes
            else if (context.Request.Cookies.TryGetValue(SESSION_COOKIE_NAME, out var cookieSessionId) &&
                !string.IsNullOrEmpty(cookieSessionId))
            {
                _logger.LogWarning("Fallback: searching circuit via session cookie: {SessionId}, connection: {ConnectionId}",
                    cookieSessionId, connectionId);

                var foundCircuitId = circuitUserStore.FindCircuitBySessionId(cookieSessionId);

                if (!string.IsNullOrEmpty(foundCircuitId))
                {
                    _logger.LogWarning("Circuit found via cookie: {CircuitId} for session: {SessionId}",
                        foundCircuitId, cookieSessionId);

                    context.Items["CircuitId"] = foundCircuitId;
                    circuitUserStore.SetCurrentCircuitId(foundCircuitId);

                    _logger.LogWarning("Connection mapping restored via cookie: Connection {ConnectionId} -> Circuit {CircuitId}",
                        connectionId, foundCircuitId);
                }
                else
                {
                    _logger.LogWarning("No circuit found for session {SessionId} (possibly expired)",
                        cookieSessionId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating connection mapping in middleware");
        }

        await _next(context);
    }
}

public static class CircuitConnectionMappingMiddlewareExtensions
{
    public static IApplicationBuilder UseCircuitConnectionMapping(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<CircuitConnectionMappingMiddleware>();
    }
}
