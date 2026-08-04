using System.Collections.Concurrent;
using System.Text;
using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Application.Services;
using Microsoft.Extensions.Options;

namespace LagersystemLVHome.Infrastructure.HostedServices;

/// <summary>
/// Background service for continuous security monitoring.
/// Checks every 10 seconds for DDoS, burst attacks, brute-force, etc.
/// </summary>
public class SecurityMonitoringHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SecurityMonitoringHostedService> _logger;
    private readonly SecurityAlertsSettings _settings;

    private readonly ConcurrentDictionary<string, DateTime> _lastAlertSent = new();
    private readonly TimeSpan _alertCooldown = TimeSpan.FromMinutes(15);

    public SecurityMonitoringHostedService(
        IServiceProvider serviceProvider,
        ILogger<SecurityMonitoringHostedService> logger,
        IOptions<SecurityAlertsSettings> settings)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Security Monitoring Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var rateLimitService = scope.ServiceProvider.GetRequiredService<IRateLimitService>();
                var securityAlertService = scope.ServiceProvider.GetRequiredService<ISecurityAlertService>();

                var burst = rateLimitService.DetectBurstAttack("global-check");
                var bruteForce = rateLimitService.DetectBruteForce("global-check");
                var ddos = rateLimitService.DetectDDoS(TimeSpan.FromMinutes(5));
                var slowRate = rateLimitService.DetectSlowRateAttack();

                var activeThreats = new List<string>();

                if (burst.IsBurstAttack)
                {
                    _logger.LogWarning("Burst attack detected: {Requests} requests in {Duration}s",
                        burst.RequestsInBurst, burst.BurstDuration.TotalSeconds);

                    if (ShouldSendAlert("BurstAttack", burst.Identifier))
                    {
                        await securityAlertService.SendBurstAttackAlertAsync(burst);
                        MarkAlertSent("BurstAttack", burst.Identifier);
                        _logger.LogInformation("Burst alert email sent (cooldown: 15 min)");
                    }
                    else
                    {
                        _logger.LogDebug("Burst alert skipped (cooldown active)");
                    }

                    activeThreats.Add($"Burst ({burst.RequestsInBurst} req in {burst.BurstDuration.TotalSeconds:F1}s)");
                }

                if (bruteForce.IsBruteForce)
                {
                    _logger.LogWarning("Brute-force attack detected: {Attempts} failed attempts",
                        bruteForce.FailedAttempts);

                    if (ShouldSendAlert("BruteForce", bruteForce.Identifier))
                    {
                        await securityAlertService.SendBruteForceAlertAsync(bruteForce);
                        MarkAlertSent("BruteForce", bruteForce.Identifier);
                        _logger.LogInformation("Brute-force alert email sent (cooldown: 15 min)");
                    }
                    else
                    {
                        _logger.LogDebug("Brute-force alert skipped (cooldown active)");
                    }

                    activeThreats.Add($"BruteForce ({bruteForce.FailedAttempts} attempts)");
                }

                if (ddos.IsDDoSPattern)
                {
                    _logger.LogWarning("DDoS attack detected: {IPs} IPs, {Requests} requests, {Avg} avg/IP",
                        ddos.UniqueIPsInvolved, ddos.TotalRequests, ddos.AverageRequestsPerIP);

                    if (ShouldSendAlert("DDoS", "global"))
                    {
                        await securityAlertService.SendDDoSAlertAsync(ddos);
                        MarkAlertSent("DDoS", "global");
                        _logger.LogInformation("DDoS alert email sent (cooldown: 15 min)");
                    }
                    else
                    {
                        _logger.LogDebug("DDoS alert skipped (cooldown active)");
                    }

                    activeThreats.Add($"DDoS ({ddos.UniqueIPsInvolved} IPs, {ddos.TotalRequests} req)");
                }

                if (slowRate.IsSlowRateAttack)
                {
                    _logger.LogWarning("Slow-rate attack detected: {Count} suspicious IPs",
                        slowRate.SuspiciousPatternCount);

                    if (ShouldSendAlert("SlowRate", "global"))
                    {
                        await securityAlertService.SendSlowRateAlertAsync(slowRate);
                        MarkAlertSent("SlowRate", "global");
                        _logger.LogInformation("Slow-rate alert email sent (cooldown: 15 min)");
                    }
                    else
                    {
                        _logger.LogDebug("Slow-rate alert skipped (cooldown active)");
                    }

                    activeThreats.Add($"SlowRate ({slowRate.SuspiciousPatternCount} IPs)");
                }

                CleanupOldCooldowns();

                if (activeThreats.Any())
                {
                    _logger.LogWarning("Active security threats: {Threats}", string.Join(", ", activeThreats));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Security Monitoring");
            }

            // Check every 10 seconds
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }

        _logger.LogInformation("Security Monitoring Service stopped");
    }

    /// <summary>
    /// Checks whether an alert should be sent (cooldown check).
    /// </summary>
    private bool ShouldSendAlert(string threatType, string identifier)
    {
        var key = $"{threatType}:{identifier}";

        if (_lastAlertSent.TryGetValue(key, out var lastSent))
        {
            var timeSinceLastAlert = DateTime.UtcNow - lastSent;

            if (timeSinceLastAlert < _alertCooldown)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Marks an alert as sent with the current timestamp.
    /// </summary>
    private void MarkAlertSent(string threatType, string identifier)
    {
        var key = $"{threatType}:{identifier}";
        _lastAlertSent[key] = DateTime.UtcNow;

        _logger.LogDebug("Alert cooldown set: {Key} for {Cooldown} min", key, _alertCooldown.TotalMinutes);
    }

    private void CleanupOldCooldowns()
    {
        var cutoff = DateTime.UtcNow.AddHours(-1);
        var keysToRemove = _lastAlertSent
            .Where(kvp => kvp.Value < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _lastAlertSent.TryRemove(key, out _);
        }

        if (keysToRemove.Any())
        {
            _logger.LogDebug("Cleanup: {Count} old cooldowns removed", keysToRemove.Count);
        }
    }
}
