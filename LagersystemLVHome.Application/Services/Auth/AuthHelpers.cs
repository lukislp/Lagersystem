using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Internal helpers shared across auth-related services to avoid copy-pasting the
/// same <c>GetClientIp</c> / <c>LogAuditAsync</c> boilerplate in every service.
/// </summary>
internal static class AuthHelpers
{
    /// <summary>
    /// Resolves the caller's client IP from <c>X-Forwarded-For</c> (first hop) or the
    /// raw <c>RemoteIpAddress</c>. Returns <c>null</c> when no <c>HttpContext</c> is available.
    /// </summary>
    public static string? GetClientIp(this IHttpContextAccessor? httpContextAccessor)
    {
        var context = httpContextAccessor?.HttpContext;
        if (context is null) return null;

        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString();
    }

    /// <summary>
    /// Best-effort audit logging: silently no-ops when <paramref name="auditService"/> is <c>null</c>
    /// and swallows exceptions (logging them via <paramref name="logger"/>) so that audit failures
    /// never take down the calling business operation.
    /// </summary>
    public static async Task SafeLogAsync(
        this IAuditService? auditService,
        ILogger logger,
        string action,
        string entity,
        int? entityId,
        object? details,
        AuditSeverity severity, CancellationToken cancellationToken = default)
    {
        if (auditService is null) return;
        try
        {
            await auditService.LogAsync(action, entity, entityId, details, severity);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error logging audit entry for action {Action}", action);
        }
    }
}
