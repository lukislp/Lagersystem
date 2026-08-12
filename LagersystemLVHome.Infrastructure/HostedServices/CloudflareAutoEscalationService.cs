using Microsoft.Extensions.Options;
using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Application.Services;
using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Infrastructure.HostedServices;

/// <summary>
/// Background service for automatic Cloudflare security escalation.
/// Monitors threat levels and escalates to "Under Attack" mode when thresholds are exceeded.
/// </summary>
public class CloudflareAutoEscalationService : BackgroundService
{
    private readonly ILogger<CloudflareAutoEscalationService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly CloudflareSettings _settings;
    private readonly Dictionary<DateTime, int> _threatHistory = new();
    private DateTime _lastCheck = DateTime.UtcNow;

    public CloudflareAutoEscalationService(
        ILogger<CloudflareAutoEscalationService> logger,
        IServiceProvider serviceProvider,
        IOptions<CloudflareSettings> settings)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait 1 minute after app start
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        _logger.LogInformation("Cloudflare Auto-Escalation Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_settings.Enabled && _settings.AutoEscalation.Enabled)
                {
                    await CheckAndEscalateAsync();
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Auto-Escalation Service");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }

    /// <summary>
    /// Checks the current threat level and escalates or de-escalates accordingly.
    /// </summary>
    private async Task CheckAndEscalateAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var cloudflareService = scope.ServiceProvider.GetRequiredService<ICloudflareService>();

        if (!await cloudflareService.IsEnabledAsync())
            return;

        try
        {
            var analytics = await cloudflareService.GetAnalyticsAsync(days: 1);
            if (analytics == null)
                return;

            var currentThreats = analytics.Threats.All;
            var now = DateTime.UtcNow;

            _threatHistory[now] = (int)currentThreats;

            // Remove entries older than the configured time window
            var cutoff = now.AddMinutes(-_settings.AutoEscalation.TimeWindowMinutes);
            var oldKeys = _threatHistory.Keys.Where(k => k < cutoff).ToList();
            foreach (var key in oldKeys)
            {
                _threatHistory.Remove(key);
            }

            var threatsInWindow = _threatHistory.Values.Sum();

            _logger.LogDebug("Threats in time window ({Minutes}min): {Count}",
                _settings.AutoEscalation.TimeWindowMinutes, threatsInWindow);

            var escalationStatus = await cloudflareService.GetEscalationStatusAsync();

            if (!escalationStatus.IsEscalated)
            {
                if (threatsInWindow >= _settings.AutoEscalation.ThreatsCountThreshold)
                {
                    _logger.LogWarning("Escalation triggered: {Threats} threats in {Minutes} minutes (threshold: {Threshold})",
                        threatsInWindow,
                        _settings.AutoEscalation.TimeWindowMinutes,
                        _settings.AutoEscalation.ThreatsCountThreshold);

                    var success = await cloudflareService.EscalateToUnderAttackAsync();

                    if (success)
                    {
                        _logger.LogWarning("Successfully escalated to 'Under Attack' mode");
                        await SendEscalationNotificationAsync(threatsInWindow);
                    }
                    else
                    {
                        _logger.LogError("Escalation failed");
                    }
                }
            }
            else
            {
                // Already escalated - check auto-de-escalation
                if (escalationStatus.AutoDeEscalateIn.HasValue &&
                    escalationStatus.AutoDeEscalateIn.Value <= TimeSpan.Zero)
                {
                    _logger.LogInformation("Auto-de-escalation: time window expired");

                    var success = await cloudflareService.DeEscalateFromUnderAttackAsync();

                    if (success)
                    {
                        _logger.LogInformation("Successfully de-escalated from 'Under Attack' mode");
                        await SendDeEscalationNotificationAsync();
                    }
                }
                else if (threatsInWindow < _settings.AutoEscalation.ThreatsCountThreshold / 2)
                {
                    // Early de-escalation when threats have significantly decreased
                    _logger.LogInformation("Early de-escalation: threats decreased ({Count})", threatsInWindow);

                    var success = await cloudflareService.DeEscalateFromUnderAttackAsync();

                    if (success)
                    {
                        _logger.LogInformation("Successfully de-escalated early");
                        await SendDeEscalationNotificationAsync();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during threat level check");
        }
    }

    /// <summary>
    /// Sends escalation notifications to all super admins.
    /// </summary>
    private async Task SendEscalationNotificationAsync(int threatCount, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var notificationService = scope.ServiceProvider.GetService<INotificationService>();
            var authService = scope.ServiceProvider.GetService<IAuthService>();

            if (notificationService == null || authService == null)
                return;

            var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<InventoryDbContext>>();
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            var superAdmins = await context.Users
                .Where(u => u.Role == UserRole.SuperAdmin && u.IsActive && !u.IsDeleted)
                .ToListAsync(cancellationToken);

            foreach (var admin in superAdmins)
            {
                await notificationService.CreateNotificationAsync(
                    admin.Id,
                    NotificationType.SecurityAlert,
                    "Cloudflare Security Escalation",
                    $"Automatic escalation to 'Under Attack' mode due to {threatCount} detected threats.",
                    "/security-center"
                );
            }

            _logger.LogInformation("Escalation notifications sent to {Count} super admins", superAdmins.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending escalation notification");
        }
    }

    /// <summary>
    /// Sends de-escalation notifications to all super admins.
    /// </summary>
    private async Task SendDeEscalationNotificationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var notificationService = scope.ServiceProvider.GetService<INotificationService>();

            if (notificationService == null)
                return;

            var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<InventoryDbContext>>();
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            var superAdmins = await context.Users
                .Where(u => u.Role == UserRole.SuperAdmin && u.IsActive && !u.IsDeleted)
                .ToListAsync(cancellationToken);

            foreach (var admin in superAdmins)
            {
                await notificationService.CreateNotificationAsync(
                    admin.Id,
                    NotificationType.Info,
                    "Cloudflare Security De-Escalation",
                    "Automatic return to normal security level. Threat situation has calmed down.",
                    "/security-center"
                );
            }

            _logger.LogInformation("De-escalation notifications sent to {Count} super admins", superAdmins.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending de-escalation notification");
        }
    }
}
