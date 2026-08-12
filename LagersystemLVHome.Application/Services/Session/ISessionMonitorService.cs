using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace LagersystemLVHome.Application.Services;

public interface ISessionMonitorService
{
    Task StartMonitoringAsync(int userId, string sessionId, string circuitId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Legacy method for compatibility (uses internal circuit tracker).
    /// </summary>
    Task StartMonitoringAsync(int userId, string sessionId, CancellationToken cancellationToken = default);

    Task StopMonitoringAsync(string circuitId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Legacy: stops the current monitor.
    /// </summary>
    Task StopMonitoringAsync(CancellationToken cancellationToken = default);

    Task StopAllMonitoringAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Force-terminates a session (for admin/cleanup).
    /// </summary>
    Task ForceTerminateSessionAsync(string sessionId, string reason, CancellationToken cancellationToken = default);

    bool IsMonitoring(string circuitId);

    int GetActiveMonitorCount();

    /// <summary>
    /// Event raised when a session should be terminated.
    /// </summary>
    event EventHandler<SessionTerminatedEventArgs>? SessionTerminated;
}
