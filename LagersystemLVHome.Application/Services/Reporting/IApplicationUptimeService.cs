using System.Diagnostics;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Tracks the actual application uptime across IIS recycles.
/// </summary>
public interface IApplicationUptimeService
{
    DateTime ApplicationStartTime { get; }
    TimeSpan ApplicationUptime { get; }
    TimeSpan ProcessUptime { get; }
    DateTime LastRecycleTime { get; }
    int RecycleCount { get; }
}
