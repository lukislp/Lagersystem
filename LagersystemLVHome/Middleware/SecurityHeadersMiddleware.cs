using Microsoft.AspNetCore.Http;

namespace LagersystemLVHome.Middleware;

public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // API endpoints return JSON - CSP is irrelevant and leaks security config
        var path = context.Request.Path.Value;
        if (path != null && path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
            await _next(context);
            return;
        }

        // Content Security Policy
        // 'unsafe-eval' required by Soenneker.Blazor.Thumbmarkjs (uses eval() via JS interop).
        // Remove once Thumbmarkjs is replaced with an eval-free alternative.
        context.Response.Headers.Append("Content-Security-Policy",
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://cdn.jsdelivr.net https://unpkg.com https://cdnjs.cloudflare.com; " +
            "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://fonts.googleapis.com; " +
            "font-src 'self' https://cdn.jsdelivr.net https://fonts.gstatic.com; " +
            "img-src 'self' data: blob:; " +
            "connect-src 'self' wss: ws: https://cdn.jsdelivr.net; " +
            "frame-ancestors 'self'; " +
            "base-uri 'self'; " +
            "form-action 'self'; " +
            "object-src 'none'; " +
            "worker-src 'self'");

        context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
        context.Response.Headers.Append("X-Frame-Options", "SAMEORIGIN");
        context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
        context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

        // camera=self: only this domain may use camera (no third-party)
        context.Response.Headers.Append("Permissions-Policy",
            "camera=(self), microphone=(), geolocation=(), payment=()");

        await _next(context);
    }
}
