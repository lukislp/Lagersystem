using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using LagersystemLVHome.Application.Configuration;

namespace LagersystemLVHome.Application.Services;

public sealed partial class NotificationService : INotificationService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly IEmailService _emailService;
    private readonly ITeamsService _teamsService;
    private readonly NotificationChannels _notificationChannels;
    private readonly ILogger<NotificationService> _logger;
    private readonly INotificationEventService _eventService;

    public NotificationService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        IEmailService emailService,
        ITeamsService teamsService,
        IOptions<NotificationChannels> notificationChannels,
        ILogger<NotificationService> logger,
        INotificationEventService eventService)
    {
        _contextFactory = contextFactory;
        _emailService = emailService;
        _teamsService = teamsService;
        _notificationChannels = notificationChannels.Value;
        _logger = logger;
        _eventService = eventService;
    }

    public async Task CreateNotificationAsync(
        int userId,
        NotificationType type,
        string title,
        string message,
        string? actionUrl = null,
        NotificationChannel channel = NotificationChannel.All, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var user = await context.Users
                .Include(u => u.Warehouse)
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

            if (user == null)
            {
                LogUserNotFoundForNotification(_logger, userId);
                return;
            }

            var settings = await GetUserSettingsAsync(userId);

            if (channel == NotificationChannel.All || channel == NotificationChannel.InApp)
            {
                if (ShouldSendInAppNotification(type, settings))
                {
                    var notification = new Notification
                    {
                        UserId = userId,
                        Type = type,
                        Title = title,
                        Message = message,
                        ActionUrl = actionUrl,
                        WarehouseId = user.WarehouseId,
                        IsRead = false,
                        CreatedAt = DateTime.UtcNow
                    };

                    context.Notifications.Add(notification);
                    await context.SaveChangesAsync(cancellationToken);

                    LogInAppNotificationSaved(_logger, title, userId);
                }
            }

            // Email notification
            if (channel == NotificationChannel.All || channel == NotificationChannel.Email)
            {
                if (ShouldSendEmailNotification(type, settings))
                {
                    try
                    {
                        await _emailService.SendEmailAsync(
                            user.Email,
                            title,
                            message);
                        LogEmailNotificationSent(_logger, title, userId);
                    }
                    catch (Exception emailEx)
                    {
                        LogEmailNotificationFailed(_logger, emailEx, title);
                        // Exception swallowed - in-app notification was already saved
                    }
                }
            }

            // Push notification
            if (channel == NotificationChannel.All || channel == NotificationChannel.Push)
            {
                if (ShouldSendPushNotification(type, settings))
                {
                    try
                    {
                        await SendPushNotificationAsync(userId, title, message, actionUrl);
                        LogPushNotificationSent(_logger, title, userId);
                    }
                    catch (Exception pushEx)
                    {
                        LogPushNotificationFailed(_logger, pushEx, title);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogInAppNotificationCreateFailed(_logger, ex, userId);
        }
    }

    public async Task CreateLowStockNotificationAsync(Product product, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var settings = await context.UserNotificationSettings
                .Include(s => s.User)
                .Where(s => s.User.WarehouseId == product.WarehouseId
                    && s.User.IsActive
                    && (s.User.Role == UserRole.Admin
                        || s.User.Role == UserRole.SuperAdmin
                        || s.User.Role == UserRole.Manager))
                .ToListAsync(cancellationToken);

            foreach (var userSettings in settings)
            {
                if (product.Quantity <= userSettings.LowStockThreshold && product.Quantity > userSettings.CriticalStockThreshold)
                {
                    await CreateNotificationAsync(
                        userSettings.UserId,
                        NotificationType.LowStock,
                        "Niedriger Bestand",
                        $"Das Produkt '{product.Name}' hat einen niedrigen Bestand: {product.Quantity} St\u00fcck",
                        $"/products?search={product.Name}");
                }
            }
        }
        catch (Exception ex)
        {
            LogLowStockNotificationError(_logger, ex, product.Id);
        }
    }

    public async Task CreateCriticalStockNotificationAsync(Product product, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var settings = await context.UserNotificationSettings
                .Include(s => s.User)
                .Where(s => s.User.WarehouseId == product.WarehouseId
                    && s.User.IsActive
                    && (s.User.Role == UserRole.Admin
                        || s.User.Role == UserRole.SuperAdmin
                        || s.User.Role == UserRole.Manager))
                .ToListAsync(cancellationToken);

            foreach (var userSettings in settings)
            {
                if (product.Quantity <= userSettings.CriticalStockThreshold)
                {
                    await CreateNotificationAsync(
                        userSettings.UserId,
                        NotificationType.CriticalStock,
                        "KRITISCHER BESTAND!",
                        $"Das Produkt '{product.Name}' hat einen kritischen Bestand: {product.Quantity} St\u00fcck. Bitte umgehend nachbestellen!",
                        $"/products?search={product.Name}",
                        NotificationChannel.All);
                }
            }
        }
        catch (Exception ex)
        {
            LogCriticalStockNotificationError(_logger, ex, product.Id);
        }
    }

    public async Task CreateNewUserNotificationAsync(User newUser, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var admins = await context.Users
                .Where(u => u.WarehouseId == newUser.WarehouseId
                    && u.IsActive
                    && (u.Role == UserRole.Admin || u.Role == UserRole.SuperAdmin)
                    && u.Id != newUser.Id)
                .ToListAsync(cancellationToken);

            foreach (var admin in admins)
            {
                await CreateNotificationAsync(
                    admin.Id,
                    NotificationType.NewUser,
                    "Neue Benutzer-Registrierung",
                    $"Ein neuer Benutzer '{newUser.Username}' ({newUser.Email}) hat sich registriert und wartet auf Freigabe.",
                    "/admin/users");
            }
        }
        catch (Exception ex)
        {
            LogNewUserNotificationError(_logger, ex, newUser.Id);
        }
    }

    public async Task CreateSecurityAlertAsync(int userId, string message, CancellationToken cancellationToken = default)
    {
        try
        {
            await CreateNotificationAsync(
                userId,
                NotificationType.SecurityAlert,
                "Sicherheitswarnung",
                message,
                "/profile",
                NotificationChannel.All);
        }
        catch (Exception ex)
        {
            LogSecurityAlertCreateError(_logger, ex, userId);
        }
    }

    public async Task<List<Notification>> GetUserNotificationsAsync(int userId, bool unreadOnly = false, int limit = 50, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var user = await context.Users
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

            if (user == null)
            {
                LogUserNotFound(_logger, userId);
                return [];
            }

            var query = context.Notifications
                .Where(n => n.UserId == userId);

            // Filter by permissions
            if (user.Role != UserRole.SuperAdmin)
            {
                // Regular users: no security alerts
                query = query.Where(n => n.Type != NotificationType.SecurityAlert);

                // Regular users: only notifications from their warehouse
                query = query.Where(n => n.WarehouseId == user.WarehouseId || n.WarehouseId == null);
            }
            // SuperAdmin: sees everything (no filter)

            if (unreadOnly)
            {
                query = query.Where(n => !n.IsRead);
            }

            return await query
                .OrderByDescending(n => n.CreatedAt)
                .Take(limit)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            LogGetNotificationsError(_logger, ex, userId);
            return [];
        }
    }

    public async Task<int> GetUnreadCountAsync(int userId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var user = await context.Users
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

            if (user == null)
            {
                LogUserNotFound(_logger, userId);
                return 0;
            }

            var query = context.Notifications
                .Where(n => n.UserId == userId && !n.IsRead);

            // Filter by permissions
            if (user.Role != UserRole.SuperAdmin)
            {
                query = query.Where(n => n.Type != NotificationType.SecurityAlert);
                query = query.Where(n => n.WarehouseId == user.WarehouseId || n.WarehouseId == null);
            }

            return await query.CountAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            LogGetUnreadCountError(_logger, ex, userId);
            return 0;
        }
    }

    public async Task MarkAsReadAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var notification = await context.Notifications.FindAsync(notificationId);
            if (notification != null && !notification.IsRead)
            {
                notification.IsRead = true;
                notification.ReadAt = DateTime.UtcNow;
                await context.SaveChangesAsync(cancellationToken);

                _eventService.NotifyChanged();
            }
        }
        catch (Exception ex)
        {
            LogMarkAsReadError(_logger, ex, notificationId);
        }
    }

    public async Task MarkAllAsReadAsync(int userId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var notifications = await context.Notifications
                .Where(n => n.UserId == userId && !n.IsRead)
                .ToListAsync(cancellationToken);

            foreach (var notification in notifications)
            {
                notification.IsRead = true;
                notification.ReadAt = DateTime.UtcNow;
            }

            await context.SaveChangesAsync(cancellationToken);

            _eventService.NotifyChanged();
        }
        catch (Exception ex)
        {
            LogMarkAllAsReadError(_logger, ex, userId);
        }
    }

    public async Task DeleteNotificationAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var notification = await context.Notifications.FindAsync(notificationId);
            if (notification != null)
            {
                context.Notifications.Remove(notification);
                await context.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            LogDeleteNotificationError(_logger, ex, notificationId);
        }
    }

    public async Task DeleteOldNotificationsAsync(int daysOld = 30, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var cutoffDate = DateTime.UtcNow.AddDays(-daysOld);
            var oldNotifications = await context.Notifications
                .Where(n => n.CreatedAt < cutoffDate && n.IsRead)
                .ToListAsync(cancellationToken);

            context.Notifications.RemoveRange(oldNotifications);
            await context.SaveChangesAsync(cancellationToken);

            LogOldNotificationsDeleted(_logger, oldNotifications.Count);
        }
        catch (Exception ex)
        {
            LogDeleteOldNotificationsError(_logger, ex);
        }
    }

    public async Task<UserNotificationSettings> GetUserSettingsAsync(int userId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var settings = await context.UserNotificationSettings
                .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

            if (settings == null)
            {
                settings = new UserNotificationSettings
                {
                    UserId = userId,
                    EmailLowStock = true,
                    EmailCriticalStock = true,
                    EmailNewUser = true,
                    EmailSecurityAlert = true,
                    PushLowStock = true,
                    PushCriticalStock = true,
                    PushSecurityAlert = true,
                    InAppLowStock = true,
                    InAppCriticalStock = true,
                    InAppNewUser = true,
                    InAppSecurityAlert = true,
                    LowStockThreshold = 10,
                    CriticalStockThreshold = 5,
                    DailyDigest = true,
                    DigestTime = new TimeSpan(9, 0, 0)
                };

                context.UserNotificationSettings.Add(settings);
                await context.SaveChangesAsync(cancellationToken);
            }

            return settings;
        }
        catch (Exception ex)
        {
            LogGetSettingsError(_logger, ex, userId);
            return new UserNotificationSettings { UserId = userId };
        }
    }

    public async Task UpdateUserSettingsAsync(UserNotificationSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            settings.UpdatedAt = DateTime.UtcNow;
            context.UserNotificationSettings.Update(settings);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            LogUpdateSettingsError(_logger, ex, settings.UserId);
        }
    }

    public async Task CheckLowStockAndNotifyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var products = await context.Products
                .Include(p => p.Warehouse)
                .ToListAsync(cancellationToken);

            foreach (var product in products)
            {
                var recentNotification = await context.Notifications
                    .AnyAsync(n => n.ActionUrl != null
                        && n.ActionUrl.Contains(product.Name)
                        && n.CreatedAt > DateTime.UtcNow.AddHours(-24)
                        && (n.Type == NotificationType.LowStock || n.Type == NotificationType.CriticalStock), cancellationToken);

                if (!recentNotification)
                {
                    if (product.Quantity <= 5)
                    {
                        await CreateCriticalStockNotificationAsync(product);
                    }
                    else if (product.Quantity <= 10)
                    {
                        await CreateLowStockNotificationAsync(product);
                    }
                }
            }

            LogLowStockCheckCompleted(_logger, products.Count);
        }
        catch (Exception ex)
        {
            LogLowStockCheckError(_logger, ex);
        }
    }

    public async Task SendDailyDigestAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var now = DateTime.Now;
            var usersWithDigest = await context.UserNotificationSettings
                .Include(s => s.User)
                .Where(s => s.DailyDigest
                    && s.User.IsActive
                    && s.DigestTime.Hours == now.Hour)
                .ToListAsync(cancellationToken);

            foreach (var userSettings in usersWithDigest)
            {
                var unreadNotifications = await context.Notifications
                    .Where(n => n.UserId == userSettings.UserId
                        && !n.IsRead
                        && n.CreatedAt >= DateTime.UtcNow.AddHours(-24))
                    .ToListAsync(cancellationToken);

                if (unreadNotifications.Any())
                {
                    var digestMessage = $"Sie haben {unreadNotifications.Count} ungelesene Benachrichtigungen:\n\n";
                    foreach (var notification in unreadNotifications.Take(10))
                    {
                        digestMessage += $"\u2022 {notification.Title}: {notification.Message}\n";
                    }

                    await _emailService.SendEmailAsync(
                        userSettings.User.Email,
                        "T\u00e4gliche Benachrichtungs-Zusammenfassung",
                        digestMessage);
                }
            }

            LogDailyDigestSent(_logger, usersWithDigest.Count);
        }
        catch (Exception ex)
        {
            LogDailyDigestError(_logger, ex);
        }
    }

    public Task<bool> SendPushNotificationAsync(int userId, string title, string body, string? url = null, CancellationToken cancellationToken = default)
    {
        LogPushSkippedNotConfigured(_logger, title, userId);
        return Task.FromResult(false);
    }

    public Task<bool> RequestPushPermissionAsync(int userId, string subscription, CancellationToken cancellationToken = default)
    {
        LogPushSubscriptionSkipped(_logger, userId);
        return Task.FromResult(true);
    }

    private bool ShouldSendInAppNotification(NotificationType type, UserNotificationSettings settings)
    {
        return type switch
        {
            NotificationType.LowStock => settings.InAppLowStock,
            NotificationType.CriticalStock => settings.InAppCriticalStock,
            NotificationType.NewUser => settings.InAppNewUser,
            NotificationType.SecurityAlert => settings.InAppSecurityAlert,
            _ => true
        };
    }

    private bool ShouldSendEmailNotification(NotificationType type, UserNotificationSettings settings)
    {
        return type switch
        {
            NotificationType.LowStock => settings.EmailLowStock,
            NotificationType.CriticalStock => settings.EmailCriticalStock,
            NotificationType.NewUser => settings.EmailNewUser,
            NotificationType.SecurityAlert => settings.EmailSecurityAlert,
            NotificationType.SystemUpdate => settings.EmailSystemUpdate,
            _ => false
        };
    }

    private bool ShouldSendPushNotification(NotificationType type, UserNotificationSettings settings)
    {
        return type switch
        {
            NotificationType.LowStock => settings.PushLowStock,
            NotificationType.CriticalStock => settings.PushCriticalStock,
            NotificationType.NewUser => settings.PushNewUser,
            NotificationType.SecurityAlert => settings.PushSecurityAlert,
            _ => false
        };
    }

    // Multi-channel notification methods

    public async Task SendLowStockAlertAsync(string productName, int currentStock, int minStock, string? warehouseName = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var channelConfig = _notificationChannels.LowStockAlerts;

            if (channelConfig.InApp)
            {
                await CreateNotificationAsync(
                    "Niedriger Bestand",
                    $"Der Bestand von {productName} ({currentStock} St\u00fcck) ist unter dem Mindestbestand ({minStock} St\u00fcck).",
                    NotificationType.LowStock,
                    null
                );
            }

            if (channelConfig.Email)
            {
                var admins = await context.Users
                    .Where(u => u.Role == UserRole.SuperAdmin || u.Role == UserRole.Admin)
                    .ToListAsync(cancellationToken);

                foreach (var admin in admins)
                {
                    await _emailService.SendEmailAsync(
                        admin.Email,
                        "Niedriger Bestand - LagerSystem",
                        $"<p>Hallo {admin.DisplayName},</p>" +
                        $"<p>Der Bestand von <strong>{productName}</strong> ist unter dem Mindestbestand:</p>" +
                        $"<ul>" +
                        $"<li>Aktueller Bestand: {currentStock} St\u00fcck</li>" +
                        $"<li>Mindestbestand: {minStock} St\u00fcck</li>" +
                        $"<li>Fehlmenge: {minStock - currentStock} St\u00fcck</li>" +
                        (!string.IsNullOrEmpty(warehouseName) ? $"<li>Lager: {warehouseName}</li>" : "") +
                        $"</ul>",
                        isHtml: true
                    );
                }
            }

            if (channelConfig.Teams)
            {
                await _teamsService.SendLowStockAlertAsync(productName, currentStock, minStock, warehouseName);
            }
        }
        catch (Exception ex)
        {
            LogSendLowStockAlertError(_logger, ex, productName);
        }
    }

    public async Task SendExpiryAlertAsync(string productName, DateTime expiryDate, int quantity, string? location = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var channelConfig = _notificationChannels.ExpiryAlerts;
            var daysUntilExpiry = (expiryDate - DateTime.UtcNow).Days;

            if (channelConfig.InApp)
            {
                await CreateNotificationAsync(
                    "MHD-Warnung",
                    $"{productName} l\u00e4uft am {expiryDate:dd.MM.yyyy} ab ({daysUntilExpiry} Tage).",
                    NotificationType.Info,
                    null
                );
            }

            if (channelConfig.Email)
            {
                var admins = await context.Users
                    .Where(u => u.Role == UserRole.SuperAdmin || u.Role == UserRole.Admin)
                    .ToListAsync(cancellationToken);

                foreach (var admin in admins)
                {
                    await _emailService.SendEmailAsync(
                        admin.Email,
                        "MHD-Warnung - LagerSystem",
                        $"<p>Hallo {admin.DisplayName},</p>" +
                        $"<p><strong>{productName}</strong> l\u00e4uft bald ab:</p>" +
                        $"<ul>" +
                        $"<li>MHD: {expiryDate:dd.MM.yyyy}</li>" +
                        $"<li>Tage bis Ablauf: {daysUntilExpiry}</li>" +
                        $"<li>Menge: {quantity} St\u00fcck</li>" +
                        (!string.IsNullOrEmpty(location) ? $"<li>Lagerort: {location}</li>" : "") +
                        $"</ul>",
                        isHtml: true
                    );
                }
            }

            if (channelConfig.Teams)
            {
                await _teamsService.SendExpiryAlertAsync(productName, expiryDate, quantity, location);
            }
        }
        catch (Exception ex)
        {
            LogSendExpiryAlertError(_logger, ex, productName);
        }
    }

    public async Task SendSecurityAlertAsync(string title, string message, string severity, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var channelConfig = _notificationChannels.SecurityAlerts;

            if (channelConfig.InApp)
            {
                await CreateNotificationAsync(
                    title,
                    message,
                    NotificationType.SecurityAlert,
                    null
                );
            }

            if (channelConfig.Email)
            {
                var superAdmins = await context.Users
                    .Where(u => u.Role == UserRole.SuperAdmin)
                    .ToListAsync(cancellationToken);

                foreach (var admin in superAdmins)
                {
                    await _emailService.SendEmailAsync(
                        admin.Email,
                        $"{title} - LagerSystem",
                        $"<p>Hallo {admin.DisplayName},</p>" +
                        $"<p>{message}</p>" +
                        $"<p><strong>Schweregrad:</strong> {severity}</p>",
                        isHtml: true
                    );
                }
            }

            if (channelConfig.Teams)
            {
                await _teamsService.SendSystemAlertAsync(title, message, severity);
            }
        }
        catch (Exception ex)
        {
            LogSendSecurityAlertError(_logger, ex);
        }
    }

    public async Task SendSystemAlertAsync(string title, string message, string severity, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var channelConfig = _notificationChannels.SystemAlerts;

            if (channelConfig.InApp)
            {
                await CreateNotificationAsync(
                    title,
                    message,
                    NotificationType.Info,
                    null
                );
            }

            if (channelConfig.Teams)
            {
                await _teamsService.SendSystemAlertAsync(title, message, severity);
            }
        }
        catch (Exception ex)
        {
            LogSendSystemAlertError(_logger, ex);
        }
    }

    // Send notification to all admins
    private async Task CreateNotificationAsync(string title, string message, NotificationType type, string? actionUrl, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var admins = await context.Users
            .Where(u => u.Role == UserRole.SuperAdmin || u.Role == UserRole.Admin)
            .ToListAsync(cancellationToken);

        foreach (var admin in admins)
        {
            await CreateNotificationAsync(admin.Id, type, title, message, actionUrl);
        }
    }
}
