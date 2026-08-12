using Microsoft.AspNetCore.Components.Server.Circuits;
using LagersystemLVHome.Application.Services;

namespace LagersystemLVHome.Infrastructure;

/// <summary>
/// Circuit handler that tracks circuit IDs and connection mappings.
/// </summary>
public class CircuitIdTrackerHandler : CircuitHandler
{
    private readonly CircuitUserStore _circuitUserStore;
    private readonly ILogger<CircuitIdTrackerHandler> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    private string? _currentCircuitId;

    public CircuitIdTrackerHandler(
        CircuitUserStore circuitUserStore,
        ILogger<CircuitIdTrackerHandler> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _circuitUserStore = circuitUserStore;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _currentCircuitId = circuit.Id;

        _logger.LogWarning("OnCircuitOpenedAsync START - Circuit: {CircuitId}", circuit.Id);
        SetCircuitIdWithLogging(circuit.Id, "Circuit Opened");
        _logger.LogWarning("OnCircuitOpenedAsync END - Circuit: {CircuitId}", circuit.Id);

        return base.OnCircuitOpenedAsync(circuit, cancellationToken);
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _currentCircuitId = circuit.Id;

        _logger.LogWarning("OnConnectionUpAsync START - Circuit: {CircuitId}", circuit.Id);
        SetCircuitIdWithLogging(circuit.Id, "Connection Up (RECONNECT)");
        _logger.LogWarning("OnConnectionUpAsync END - Circuit: {CircuitId}", circuit.Id);

        return base.OnConnectionUpAsync(circuit, cancellationToken);
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _logger.LogWarning("OnConnectionDownAsync - Circuit: {CircuitId}", circuit.Id);
        return base.OnConnectionDownAsync(circuit, cancellationToken);
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _logger.LogWarning("OnCircuitClosedAsync - Circuit: {CircuitId}", circuit.Id);
        _currentCircuitId = null;
        return base.OnCircuitClosedAsync(circuit, cancellationToken);
    }

    private void SetCircuitIdWithLogging(string circuitId, string eventName)
    {
        _logger.LogWarning("  SetCircuitIdWithLogging BEFORE - CircuitId: {CircuitId}, Event: {Event}",
            circuitId, eventName);

        _circuitUserStore.SetCurrentCircuitId(circuitId);

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null)
        {
            var connectionId = httpContext.Connection.Id;
            var userAgent = httpContext.Request.Headers["User-Agent"].ToString();
            var isMobile = userAgent.Contains("Mobile", StringComparison.OrdinalIgnoreCase) ||
                userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase) ||
                userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase);

            _logger.LogWarning("  Circuit ID set: {CircuitId} | Connection: {ConnectionId} | Event: {Event} | Mobile: {IsMobile}",
                circuitId, connectionId, eventName, isMobile);

            var allCircuits = _circuitUserStore.GetAllCircuits();
            var allMappings = _circuitUserStore.GetAllConnectionMappings();

            _logger.LogWarning("  Current user circuits count: {Count}", allCircuits.Count);
            _logger.LogWarning("  Current connection mappings count: {Count}", allMappings.Count);

            foreach (var mapping in allMappings)
            {
                _logger.LogWarning("   - Connection: {ConnectionId} -> Circuit: {CircuitId}",
                    mapping.Key, mapping.Value);
            }

            foreach (var circuit in allCircuits)
            {
                _logger.LogWarning("   - Circuit: {CircuitId} -> User: {User}",
                    circuit.Key, circuit.Value);
            }
        }
        else
        {
            _logger.LogWarning("  Circuit ID set: {CircuitId} (no HttpContext) | Event: {Event}",
                circuitId, eventName);
        }

        _logger.LogWarning("  SetCircuitIdWithLogging AFTER - CircuitId: {CircuitId}", circuitId);
    }
}
