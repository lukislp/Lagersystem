using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace LagersystemLVHome.Infrastructure;

/// <summary>
/// Global exception handler for the request pipeline. Returns RFC 7807
/// ProblemDetails for API calls and a generic 500 HTML response for other
/// requests. Logs the exception with its full stack trace.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger,
        IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Do not interfere with cancellation and abort propagation.
        if (exception is OperationCanceledException or TaskCanceledException)
        {
            return false;
        }

        _logger.LogError(
            exception,
            "Unhandled exception for {Method} {Path}: {Message}",
            httpContext.Request.Method,
            httpContext.Request.Path,
            exception.Message);

        // Only produce a ProblemDetails body for JSON/API callers. Razor and
        // SignalR already have their own error handling surfaces (the
        // <ErrorBoundary> component and the Blazor circuit).
        if (!IsApiRequest(httpContext))
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Type = "https://datatracker.ietf.org/doc/html/rfc9110#name-500-internal-server-error",
            Instance = httpContext.Request.Path
        };

        // Include the raw exception message only in development to avoid
        // information disclosure in production.
        if (_environment.IsDevelopment())
        {
            problem.Detail = exception.ToString();
        }

        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }

    private static bool IsApiRequest(HttpContext httpContext)
    {
        if (httpContext.Request.Path.StartsWithSegments("/api"))
        {
            return true;
        }

        var accept = httpContext.Request.Headers.Accept.ToString();
        return accept.Contains("application/json", StringComparison.OrdinalIgnoreCase);
    }
}
