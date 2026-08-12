using Microsoft.AspNetCore.Components.Server.Circuits;
using LagersystemLVHome.Application.Services;

namespace LagersystemLVHome.Middleware;

/// <summary>
/// Restores the circuit ID on each request so connection mappings
/// stay up to date even when the connection ID changes.
/// </summary>
public class CircuitIdRestorationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CircuitIdRestorationMiddleware> _logger;

    public CircuitIdRestorationMiddleware(
        RequestDelegate next,
        ILogger<CircuitIdRestorationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, CircuitUserStore circuitUserStore)
    {
        if (context.Items.TryGetValue("CircuitId", out var circuitIdObj) && circuitIdObj is string circuitId)
        {
            circuitUserStore.SetCurrentCircuitId(circuitId);

            _logger.LogDebug("Circuit ID restored in middleware: {CircuitId} for Connection: {ConnectionId}",
                circuitId, context.Connection.Id);
        }

        await _next(context);
    }
}

public static class CircuitIdRestorationMiddlewareExtensions
{
    public static IApplicationBuilder UseCircuitIdRestoration(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<CircuitIdRestorationMiddleware>();
    }
}
