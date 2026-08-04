using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace LagersystemLVHome.Application.Services;

public sealed class SessionTerminatedEventArgs : EventArgs
{
    public int UserId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string CircuitId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime TerminatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Circuit-isolated session monitor service.
///
/// This service is a SINGLETON but manages MULTIPLE monitors for different
/// circuits (browser tabs/devices).
///
/// Each circuit has its own:
/// - PeriodicTimer
/// - CancellationTokenSource
/// - Session info
///
/// This prevents the problem where a multi-user login causes one user
/// to overwrite another user's monitor.
/// </summary>
public sealed class SessionMonitorService : ISessionMonitorService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SessionMonitorService> _logger;

    // Circuit-isolated monitor instances
    private readonly ConcurrentDictionary<string, CircuitMonitorState> _monitors = new();

    // Synchronization for clean shutdown
    private readonly SemaphoreSlim _shutdownLock = new(1, 1);
    private bool _isDisposed = false;

    // For legacy compatibility
    private static readonly AsyncLocal<string?> _currentCircuitId = new();

    public event EventHandler<SessionTerminatedEventArgs>? SessionTerminated;

    public SessionMonitorService(
        IServiceProvider serviceProvider,
        ILogger<SessionMonitorService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Legacy method for compatibility.
    /// </summary>
    public Task StartMonitoringAsync(int userId, string sessionId, CancellationToken cancellationToken = default)
    {
        var circuitId = _currentCircuitId.Value ?? $"legacy-{Guid.NewGuid():N}";
        _currentCircuitId.Value = circuitId;
        return StartMonitoringAsync(userId, sessionId, circuitId);
    }

    public async Task StartMonitoringAsync(int userId, string sessionId, string circuitId, CancellationToken cancellationToken = default)
    {
        if (_isDisposed)
        {
            _logger.LogWarning("SessionMonitor: Cannot start - service is disposed");
            return;
        }

        if (string.IsNullOrEmpty(circuitId))
        {
            _logger.LogWarning("SessionMonitor: Cannot start - circuitId is null/empty");
            return;
        }

        if (string.IsNullOrEmpty(sessionId))
        {
            _logger.LogWarning("SessionMonitor: Cannot start - sessionId is null/empty");
            return;
        }

        // Stop existing monitor for this circuit (if any)
        if (_monitors.ContainsKey(circuitId))
        {
            _logger.LogInformation("SessionMonitor: Stopping existing monitor for circuit {CircuitId}", circuitId);
            await StopMonitoringAsync(circuitId);
        }

        // Create new monitor state
        var monitorState = new CircuitMonitorState
        {
            CircuitId = circuitId,
            UserId = userId,
            SessionId = sessionId,
            CancellationTokenSource = new CancellationTokenSource(),
            StartedAt = DateTime.UtcNow
        };

        // Add to dictionary
        if (!_monitors.TryAdd(circuitId, monitorState))
        {
            _logger.LogWarning("SessionMonitor: Failed to add monitor for circuit {CircuitId}", circuitId);
            monitorState.CancellationTokenSource.Dispose();
            return;
        }

        _logger.LogInformation(
            "SessionMonitor: Started monitoring - Circuit={CircuitId}, User={UserId}, Session={SessionId}, TotalMonitors={Count}",
            circuitId, userId, sessionId.Substring(0, Math.Min(8, sessionId.Length)) + "...", _monitors.Count);

        // Start background task for this circuit
        monitorState.MonitorTask = RunMonitorLoopAsync(monitorState);
    }

    /// <summary>
    /// Legacy: stops the current monitor.
    /// </summary>
    public Task StopMonitoringAsync(CancellationToken cancellationToken = default)
    {
        var circuitId = _currentCircuitId.Value;
        if (!string.IsNullOrEmpty(circuitId))
        {
            return StopMonitoringAsync(circuitId);
        }
        return Task.CompletedTask;
    }

    public async Task StopMonitoringAsync(string circuitId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(circuitId))
            return;

        if (_monitors.TryRemove(circuitId, out var monitorState))
        {
            _logger.LogInformation(
                "SessionMonitor: Stopping monitor for circuit {CircuitId}, RemainingMonitors={Count}",
                circuitId, _monitors.Count);

            try
            {
                // Signal cancellation
                monitorState.CancellationTokenSource.Cancel();

                // Wait briefly for task completion
                if (monitorState.MonitorTask != null && !monitorState.MonitorTask.IsCompleted)
                {
                    try
                    {
                        await monitorState.MonitorTask.WaitAsync(TimeSpan.FromSeconds(2));
                    }
                    catch (TimeoutException)
                    {
                        _logger.LogDebug("Monitor task did not complete in time for circuit {CircuitId}", circuitId);
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected
                    }
                }
            }
            finally
            {
                monitorState.CancellationTokenSource.Dispose();
            }

            _logger.LogInformation("SessionMonitor: Monitor stopped for circuit {CircuitId}", circuitId);
        }
    }

    public async Task StopAllMonitoringAsync(CancellationToken cancellationToken = default)
    {
        await _shutdownLock.WaitAsync();
        try
        {
            _logger.LogInformation("SessionMonitor: Stopping all {Count} monitors", _monitors.Count);

            var circuits = _monitors.Keys.ToList();
            foreach (var circuitId in circuits)
            {
                await StopMonitoringAsync(circuitId);
            }
        }
        finally
        {
            _shutdownLock.Release();
        }
    }

    public async Task ForceTerminateSessionAsync(string sessionId, string reason, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("SessionMonitor: Force terminating session {SessionId}",
            sessionId.Substring(0, Math.Min(8, sessionId.Length)) + "...");

        // Find all circuits with this session
        var affectedMonitors = _monitors.Values
            .Where(m => m.SessionId == sessionId)
            .ToList();

        foreach (var monitor in affectedMonitors)
        {
            _logger.LogInformation("Triggering SessionTerminated event for circuit {CircuitId}", monitor.CircuitId);

            // Trigger event
            SessionTerminated?.Invoke(this, new SessionTerminatedEventArgs
            {
                UserId = monitor.UserId,
                SessionId = sessionId,
                CircuitId = monitor.CircuitId,
                Reason = reason
            });

            // Stop monitor
            await StopMonitoringAsync(monitor.CircuitId);
        }

        _logger.LogInformation(
            "Force terminate completed for session {SessionId}, affected {Count} circuits",
            sessionId.Substring(0, Math.Min(8, sessionId.Length)) + "...", affectedMonitors.Count);
    }

    public bool IsMonitoring(string circuitId)
    {
        return !string.IsNullOrEmpty(circuitId) && _monitors.ContainsKey(circuitId);
    }

    public int GetActiveMonitorCount()
    {
        return _monitors.Count;
    }

    private async Task RunMonitorLoopAsync(CircuitMonitorState state, CancellationToken cancellationToken = default)
    {
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        try
        {
            _logger.LogDebug("Monitor loop started for circuit {CircuitId}", state.CircuitId);

            while (!state.CancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    await timer.WaitForNextTickAsync(state.CancellationTokenSource.Token);
                    await CheckSessionStatusAsync(state);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug("Monitor loop cancelled for circuit {CircuitId}", state.CircuitId);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in monitor loop for circuit {CircuitId}", state.CircuitId);
                }
            }
        }
        finally
        {
            timer.Dispose();
            _logger.LogDebug("Monitor loop ended for circuit {CircuitId}", state.CircuitId);
        }
    }

    private async Task CheckSessionStatusAsync(CircuitMonitorState state, CancellationToken cancellationToken = default)
    {
        if (state.IsTerminated)
            return;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var sessionService = scope.ServiceProvider.GetRequiredService<ISessionManagementService>();

            var session = await sessionService.GetSessionAsync(state.SessionId);

            if (session == null)
            {
                _logger.LogWarning("Session not found: {SessionId} (Circuit: {CircuitId})",
                    state.SessionId.Substring(0, Math.Min(8, state.SessionId.Length)) + "...", state.CircuitId);
                await TerminateAsync(state, "Session not found in database");
                return;
            }

            if (!session.IsActive)
            {
                _logger.LogWarning("Session inactive: {SessionId}, Reason: {Reason} (Circuit: {CircuitId})",
                    state.SessionId.Substring(0, Math.Min(8, state.SessionId.Length)) + "...",
                    session.EndReason, state.CircuitId);
                await TerminateAsync(state, $"Session ended: {session.EndReason}");
                return;
            }

            _logger.LogDebug("Session active: User={UserId}, Circuit={CircuitId}",
                state.UserId, state.CircuitId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking session status for circuit {CircuitId}", state.CircuitId);
        }
    }

    private async Task TerminateAsync(CircuitMonitorState state, string reason, CancellationToken cancellationToken = default)
    {
        if (state.IsTerminated)
            return;

        state.IsTerminated = true;

        _logger.LogWarning("Terminating session for circuit {CircuitId}: {Reason}", state.CircuitId, reason);

        try
        {
            // Trigger event for UI blocking
            SessionTerminated?.Invoke(this, new SessionTerminatedEventArgs
            {
                UserId = state.UserId,
                SessionId = state.SessionId,
                CircuitId = state.CircuitId,
                Reason = reason
            });

            // Logout via AuthService
            using var scope = _serviceProvider.CreateScope();
            var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
            await authService.LogoutAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during session termination for circuit {CircuitId}", state.CircuitId);
        }
        finally
        {
            await StopMonitoringAsync(state.CircuitId);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        _logger.LogInformation("SessionMonitorService disposing...");

        try
        {
            StopAllMonitoringAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during SessionMonitorService disposal");
        }
        finally
        {
            _shutdownLock.Dispose();
        }
    }

    /// <summary>
    /// Internal state for each monitored circuit.
    /// </summary>
    private class CircuitMonitorState
    {
        public string CircuitId { get; set; } = string.Empty;
        public int UserId { get; set; }
        public string SessionId { get; set; } = string.Empty;
        public CancellationTokenSource CancellationTokenSource { get; set; } = null!;
        public Task? MonitorTask { get; set; }
        public DateTime StartedAt { get; set; }
        public bool IsTerminated { get; set; } = false;
    }
}
