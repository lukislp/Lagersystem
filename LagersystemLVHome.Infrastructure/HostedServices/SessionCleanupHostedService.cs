using LagersystemLVHome.Data;
using LagersystemLVHome.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.Infrastructure.HostedServices;

/// <summary>
/// Automatically cleans up expired and inactive sessions.
/// Terminates inactive sessions (30+ min) and deletes old sessions (30+ days).
/// Triggers logout via SessionMonitorService.
/// </summary>
public class SessionCleanupHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SessionCleanupHostedService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5);
    private readonly TimeSpan _inactivityTimeout = TimeSpan.FromMinutes(30);
    private readonly TimeSpan _oldSessionThreshold = TimeSpan.FromDays(30);

    public SessionCleanupHostedService(
        IServiceProvider serviceProvider,
        ILogger<SessionCleanupHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Session Cleanup Service started - Check interval: {Interval} minutes",
            _checkInterval.TotalMinutes);

        // Wait 1 minute after start before first cleanup runs
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupSessionsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during session cleanup");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("Session Cleanup Service stopped");
    }

    private async Task CleanupSessionsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<InventoryDbContext>>();
        var sessionMonitor = scope.ServiceProvider.GetRequiredService<ISessionMonitorService>();

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var inactivityCutoff = now - _inactivityTimeout;
        var oldSessionCutoff = now - _oldSessionThreshold;

        // Terminate inactive active sessions (30+ min no activity)
        var inactiveSessions = await context.UserSessions
            .Where(s => s.IsActive && s.LastActivity < inactivityCutoff)
            .ToListAsync(cancellationToken);

        if (inactiveSessions.Any())
        {
            foreach (var session in inactiveSessions)
            {
                session.IsActive = false;
                session.EndTime = now;
                session.EndReason = Domain.Models.SessionEndReason.Timeout;
                session.EndReasonDetails = $"Automatic termination due to inactivity (>{_inactivityTimeout.TotalMinutes} minutes)";

                try
                {
                    await sessionMonitor.ForceTerminateSessionAsync(
                        session.SessionId,
                        "Automatic cleanup: Session timeout"
                    );

                    _logger.LogInformation("Triggered logout for session {SessionId} (User: {Username})",
                        session.SessionId, session.Username);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not trigger logout for session {SessionId}, but DB updated",
                        session.SessionId);
                }
            }

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Terminated {Count} inactive sessions (inactivity > {Minutes} min)",
                inactiveSessions.Count, _inactivityTimeout.TotalMinutes);
        }

        // Delete old terminated sessions (30+ days)
        var oldSessions = await context.UserSessions
            .Where(s => !s.IsActive && s.StartTime < oldSessionCutoff)
            .ToListAsync(cancellationToken);

        if (oldSessions.Any())
        {
            // Delete associated SessionActivities first (foreign key constraint)
            var sessionIds = oldSessions.Select(s => s.Id).ToList();
            var activitiesToDelete = await context.SessionActivities
                .Where(a => sessionIds.Contains(a.SessionId))
                .ToListAsync(cancellationToken);

            if (activitiesToDelete.Any())
            {
                context.SessionActivities.RemoveRange(activitiesToDelete);
                _logger.LogInformation("Deleting {Count} session activities for old sessions",
                    activitiesToDelete.Count);
            }

            context.UserSessions.RemoveRange(oldSessions);
            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Deleted {Count} old sessions (age > {Days} days)",
                oldSessions.Count, _oldSessionThreshold.TotalDays);
        }

        // Log session statistics
        var activeSessions = await context.UserSessions
            .CountAsync(s => s.IsActive, cancellationToken);
        var totalSessions = await context.UserSessions
            .CountAsync(cancellationToken);

        _logger.LogInformation("Session statistics: {Active} active, {Total} total in database",
            activeSessions, totalSessions);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Session Cleanup Service is stopping...");
        await base.StopAsync(cancellationToken);
    }
}
