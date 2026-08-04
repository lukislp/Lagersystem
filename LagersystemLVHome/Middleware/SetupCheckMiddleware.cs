using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.Middleware;

/// <summary>
/// Middleware to check if initial setup is required (no users exist).
/// </summary>
public class SetupCheckMiddleware
{
    private readonly RequestDelegate _next;

    public SetupCheckMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext httpContext,
        IDbContextFactory<InventoryDbContext> contextFactory)
    {
        var path = httpContext.Request.Path.Value?.ToLower() ?? string.Empty;

        // Skip static files and specific paths. Health/readiness probes must always respond
        // regardless of setup state - otherwise a fresh, unseeded instance (e.g. right after
        // `docker compose up` before the setup wizard has run) never reports healthy.
        if (path.StartsWith("/_framework") ||
            path.StartsWith("/_content") ||
            path.StartsWith("/css") ||
            path.StartsWith("/js") ||
            path.StartsWith("/lib") ||
            path == "/setup" ||
            path == "/healthz" ||
            path == "/readyz")
        {
            await _next(httpContext);
            return;
        }

        await using var dbContext = await contextFactory.CreateDbContextAsync();
        var hasUsers = await dbContext.Users.AnyAsync();

        // If no users exist and not already on setup page, redirect to setup
        if (!hasUsers && path != "/setup")
        {
            httpContext.Response.Redirect("/setup");
            return;
        }

        await _next(httpContext);
    }
}
