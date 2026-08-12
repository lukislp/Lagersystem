using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LagersystemLVHome.Application.Services;

public sealed class SecurityAlertService : ISecurityAlertService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly INotificationService _notificationService;
    private readonly IEmailService _emailService;
    private readonly SecurityAlertsSettings _alertSettings;
    private readonly ILogger<SecurityAlertService> _logger;
    private readonly string _applicationUrl;

    public SecurityAlertService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        INotificationService notificationService,
        IEmailService emailService,
        IOptions<SecurityAlertsSettings> alertSettings,
        IConfiguration configuration,
        ILogger<SecurityAlertService> logger)
    {
        _contextFactory = contextFactory;
        _notificationService = notificationService;
        _emailService = emailService;
        _alertSettings = alertSettings.Value;
        _logger = logger;
        _applicationUrl = configuration["EmailSettings:ApplicationUrl"] ?? "https://localhost:5001";
    }

    public async Task SendBurstAttackAlertAsync(BurstAttackDetection detection, CancellationToken cancellationToken = default)
    {
        if (!_alertSettings.Enabled) return;

        var title = "Burst Attack erkannt";
        var message = $@"
=== KRITISCHE SICHERHEITSWARNUNG ===

Angreifer: {detection.Identifier}
Requests: {detection.RequestsInBurst} in {detection.BurstDuration.TotalSeconds:F1}s
Rate: {detection.RequestsPerSecond:F1} req/s

>>> Sofortige Überprüfung erforderlich! <<<

Dashboard: {_applicationUrl.TrimEnd('/')}/admin/rate-limits
";

        await CreateNotificationForSuperAdminAsync(title, message, NotificationPriority.Critical);

        if (_alertSettings.BurstAttack.EmailEnabled)
        {
            await SendEmailAlertToSuperAdminAsync(title, message);
        }

        _logger.LogCritical("Burst Attack detected from {Identifier}: {Requests} requests in {Duration}s",
            detection.Identifier, detection.RequestsInBurst, detection.BurstDuration.TotalSeconds);
    }

    public async Task SendBruteForceAlertAsync(BruteForceDetection detection, CancellationToken cancellationToken = default)
    {
        if (!_alertSettings.Enabled) return;

        var title = "Brute-Force Angriff";
        var message = $@"
=== SICHERHEITSWARNUNG ===

Angreifer: {detection.Identifier}
Fehlgeschlagene Versuche: {detection.FailedAttempts}
Zeitraum: {detection.AttackDuration.TotalMinutes:F1} Minuten
Ziel-Endpoints: {string.Join(", ", detection.TargetedEndpoints)}

>>> Account könnte kompromittiert sein! <<<

Dashboard: {_applicationUrl.TrimEnd('/')}/admin/rate-limits
";

        await CreateNotificationForSuperAdminAsync(title, message, NotificationPriority.High);

        if (_alertSettings.BruteForce.EmailEnabled)
        {
            await SendEmailAlertToSuperAdminAsync(title, message);
        }

        _logger.LogWarning("Brute-Force attack from {Identifier}: {Attempts} failed attempts",
            detection.Identifier, detection.FailedAttempts);
    }

    public async Task SendDDoSAlertAsync(DDoSDetection detection, CancellationToken cancellationToken = default)
    {
        if (!_alertSettings.Enabled) return;

        var title = "DDoS Pattern erkannt";
        var message = $@"
=== KRITISCHE NETZWERKWARNUNG ===

Unique IPs: {detection.UniqueIPsInvolved}
Total Requests: {detection.TotalRequests}
Durchschnitt Requests/IP: {detection.AverageRequestsPerIP:F1}
Top Offenders: {string.Join(", ", detection.SuspiciousIPs.Take(10))}

>>> Netzwerk-Verteidigung aktivieren! <<<

Dashboard: {_applicationUrl.TrimEnd('/')}/admin/rate-limits
";

        await CreateNotificationForSuperAdminAsync(title, message, NotificationPriority.Critical);

        if (_alertSettings.DDoS.EmailEnabled)
        {
            await SendEmailAlertToSuperAdminAsync(title, message);
        }

        _logger.LogCritical("DDoS pattern detected: {IPs} IPs, {Requests} requests",
            detection.UniqueIPsInvolved, detection.TotalRequests);
    }

    public async Task SendSlowRateAlertAsync(SlowRateAttackDetection detection, CancellationToken cancellationToken = default)
    {
        if (!_alertSettings.Enabled) return;

        var title = "Slow-Rate Attack Pattern";
        var message = $@"
=== SICHERHEITSHINWEIS ===

Verdächtige IPs: {detection.SuspiciousPatternCount}
Pattern: Konstante Aktivität über 24h
Offenders: {string.Join(", ", detection.ConsistentOffenders.Take(5))}

>>> Möglicher koordinierter Angriff <<<

Dashboard: {_applicationUrl.TrimEnd('/')}/admin/rate-limits
";

        await CreateNotificationForSuperAdminAsync(title, message, NotificationPriority.Medium);

        if (_alertSettings.SlowRate.EmailEnabled)
        {
            await SendEmailAlertToSuperAdminAsync(title, message);
        }

        _logger.LogInformation("Slow-rate attack pattern detected: {Count} suspicious IPs",
            detection.SuspiciousPatternCount);
    }

    /// <summary>
    /// Creates a notification in the notification center for a security alert.
    /// </summary>
    private async Task CreateNotificationForSuperAdminAsync(string title, string message, NotificationPriority priority, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var superAdmin = await context.Users
                .FirstOrDefaultAsync(u => u.Role == UserRole.SuperAdmin && u.IsActive && !u.IsDeleted, cancellationToken);

            if (superAdmin == null)
            {
                _logger.LogWarning("No SuperAdmin found in database - cannot send notification");
                return;
            }

            _logger.LogDebug("Creating security notification for SuperAdmin: {Username} (ID: {UserId}), Title: {Title}, Priority: {Priority}",
                superAdmin.Username, superAdmin.Id, title, priority);

            await _notificationService.CreateNotificationAsync(
                superAdmin.Id,
                NotificationType.SecurityAlert,
                title,
                message,
                "/admin/security-threats"
            );

            // Set priority manually since CreateNotificationAsync has no priority parameter
            var notification = await context.Notifications
                .OrderByDescending(n => n.Id)
                .FirstOrDefaultAsync(n => n.UserId == superAdmin.Id && n.Title == title, cancellationToken);

            if (notification != null)
            {
                notification.Priority = priority;
                await context.SaveChangesAsync(cancellationToken);
                _logger.LogDebug("Notification {NotificationId} priority updated to {Priority}", notification.Id, priority);
            }
            else
            {
                _logger.LogError("Notification not found in DB after creation for title: {Title}", title);
            }

            _logger.LogInformation("Security alert notification created for SuperAdmin: {Username} (Priority: {Priority})",
                superAdmin.Username, priority);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create notification for SuperAdmin");
        }
    }

    /// <summary>
    /// Sends an email alert to the SuperAdmin.
    /// </summary>
    private async Task SendEmailAlertToSuperAdminAsync(string subject, string body, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var superAdmin = await context.Users
                .FirstOrDefaultAsync(u => u.Role == UserRole.SuperAdmin && u.IsActive && !u.IsDeleted, cancellationToken);

            if (superAdmin == null)
            {
                _logger.LogWarning("No SuperAdmin found in database - cannot send email alert");
                return;
            }

            if (string.IsNullOrEmpty(superAdmin.Email))
            {
                _logger.LogWarning("SuperAdmin has no email address configured");
                return;
            }

            await _emailService.SendEmailAsync(
                to: superAdmin.Email,
                subject: $"[SECURITY ALERT] {subject}",
                body: body
            );

            _logger.LogInformation("Security alert email sent to SuperAdmin: {Email}", superAdmin.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send security alert email to SuperAdmin");
        }
    }
}
