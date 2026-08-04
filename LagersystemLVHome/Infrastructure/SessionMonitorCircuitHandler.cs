using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Server.Circuits;
using LagersystemLVHome.Application.Services;

namespace LagersystemLVHome.Infrastructure;

/// <summary>
/// Circuit handler with circuit-isolated session monitoring.
/// Starts the session monitor on circuit open and stops it on circuit close.
/// Uses the circuit ID for isolated monitoring.
/// </summary>
public class SessionMonitorCircuitHandler : CircuitHandler
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SessionMonitorCircuitHandler> _logger;
    private readonly CircuitUserStore _circuitUserStore;
    private string? _currentCircuitId;

    public SessionMonitorCircuitHandler(
        IServiceProvider serviceProvider,
        ILogger<SessionMonitorCircuitHandler> logger,
        CircuitUserStore circuitUserStore)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _circuitUserStore = circuitUserStore;
    }

    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _currentCircuitId = circuit.Id;
        _logger.LogInformation("Circuit opened: {CircuitId}", circuit.Id);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();

            var currentUser = await authService.GetCurrentUserAsync();
            if (currentUser == null)
            {
                _logger.LogDebug("Circuit {CircuitId} opened without authenticated user (normal for login page)", circuit.Id);
                await base.OnCircuitOpenedAsync(circuit, cancellationToken);
                return;
            }

            var sessionId = await authService.GetCurrentSessionIdAsync();
            if (string.IsNullOrEmpty(sessionId))
            {
                _logger.LogWarning("Circuit {CircuitId} opened for user {UserId} but no session ID found",
                    circuit.Id, currentUser.Id);
                await base.OnCircuitOpenedAsync(circuit, cancellationToken);
                return;
            }

            _logger.LogInformation("Circuit {CircuitId}: User={Username} (UserId={UserId}), SessionId={SessionId}",
                circuit.Id, currentUser.Username, currentUser.Id,
                sessionId.Substring(0, Math.Min(8, sessionId.Length)) + "...");

            // Start session monitor with circuit ID (circuit-isolated)
            var sessionMonitor = _serviceProvider.GetRequiredService<ISessionMonitorService>();
            await sessionMonitor.StartMonitoringAsync(currentUser.Id, sessionId, circuit.Id);

            _logger.LogInformation("Session monitor started for circuit {CircuitId}, TotalMonitors={Count}",
                circuit.Id, sessionMonitor.GetActiveMonitorCount());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting session monitor for circuit {CircuitId}", circuit.Id);
        }

        await base.OnCircuitOpenedAsync(circuit, cancellationToken);
    }

    public override async Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Circuit closed: {CircuitId}", circuit.Id);

        try
        {
            var sessionMonitor = _serviceProvider.GetRequiredService<ISessionMonitorService>();
            await sessionMonitor.StopMonitoringAsync(circuit.Id);

            _circuitUserStore.RemoveCircuit(circuit.Id);

            _logger.LogInformation("Circuit {CircuitId} cleaned up, RemainingMonitors={Count}",
                circuit.Id, sessionMonitor.GetActiveMonitorCount());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up circuit {CircuitId}", circuit.Id);
        }

        await base.OnCircuitClosedAsync(circuit, cancellationToken);
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Connection down for circuit: {CircuitId}", circuit.Id);

        // Monitor keeps running - session is only ended on circuit close.
        // This allows reconnects without session loss.

        return base.OnConnectionDownAsync(circuit, cancellationToken);
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Connection restored for circuit: {CircuitId}", circuit.Id);

        // Monitor is already running - no action needed.

        return base.OnConnectionUpAsync(circuit, cancellationToken);
    }
}
