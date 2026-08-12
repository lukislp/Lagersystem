using Microsoft.Extensions.Logging;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Stufe 10 — LoggerMessage source generator catalog for NotificationService.
/// EventId range 4000–4099.
/// </summary>
public sealed partial class NotificationService
{
    // --- Notification creation (4000-4019) ---

    [LoggerMessage(EventId = 4000, Level = LogLevel.Warning,
        Message = "User {UserId} not found for notification")]
    private static partial void LogUserNotFoundForNotification(ILogger logger, int userId);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Information,
        Message = "In-App notification saved to DB: {Title} (User: {UserId})")]
    private static partial void LogInAppNotificationSaved(ILogger logger, string? title, int userId);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Information,
        Message = "Email notification sent: {Title} (User: {UserId})")]
    private static partial void LogEmailNotificationSent(ILogger logger, string? title, int userId);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Warning,
        Message = "Email notification failed, but In-App notification was saved: {Title}")]
    private static partial void LogEmailNotificationFailed(ILogger logger, Exception ex, string? title);

    [LoggerMessage(EventId = 4004, Level = LogLevel.Information,
        Message = "Push notification sent: {Title} (User: {UserId})")]
    private static partial void LogPushNotificationSent(ILogger logger, string? title, int userId);

    [LoggerMessage(EventId = 4005, Level = LogLevel.Warning,
        Message = "Push notification failed, but In-App notification was saved: {Title}")]
    private static partial void LogPushNotificationFailed(ILogger logger, Exception ex, string? title);

    [LoggerMessage(EventId = 4006, Level = LogLevel.Error,
        Message = "CRITICAL: Failed to create In-App notification for user {UserId}")]
    private static partial void LogInAppNotificationCreateFailed(ILogger logger, Exception ex, int userId);

    // --- Specialized notifications (4020-4039) ---

    [LoggerMessage(EventId = 4020, Level = LogLevel.Error,
        Message = "Error creating low stock notification for product {ProductId}")]
    private static partial void LogLowStockNotificationError(ILogger logger, Exception ex, int productId);

    [LoggerMessage(EventId = 4021, Level = LogLevel.Error,
        Message = "Error creating critical stock notification for product {ProductId}")]
    private static partial void LogCriticalStockNotificationError(ILogger logger, Exception ex, int productId);

    [LoggerMessage(EventId = 4022, Level = LogLevel.Error,
        Message = "Error creating new user notification for user {UserId}")]
    private static partial void LogNewUserNotificationError(ILogger logger, Exception ex, int userId);

    [LoggerMessage(EventId = 4023, Level = LogLevel.Error,
        Message = "Error creating security alert for user {UserId}")]
    private static partial void LogSecurityAlertCreateError(ILogger logger, Exception ex, int userId);

    // --- Query (4040-4059) ---

    [LoggerMessage(EventId = 4040, Level = LogLevel.Warning,
        Message = "User {UserId} not found")]
    private static partial void LogUserNotFound(ILogger logger, int userId);

    [LoggerMessage(EventId = 4041, Level = LogLevel.Error,
        Message = "Error getting notifications for user {UserId}")]
    private static partial void LogGetNotificationsError(ILogger logger, Exception ex, int userId);

    [LoggerMessage(EventId = 4042, Level = LogLevel.Error,
        Message = "Error getting unread count for user {UserId}")]
    private static partial void LogGetUnreadCountError(ILogger logger, Exception ex, int userId);

    [LoggerMessage(EventId = 4043, Level = LogLevel.Error,
        Message = "Error marking notification {NotificationId} as read")]
    private static partial void LogMarkAsReadError(ILogger logger, Exception ex, int notificationId);

    [LoggerMessage(EventId = 4044, Level = LogLevel.Error,
        Message = "Error marking all notifications as read for user {UserId}")]
    private static partial void LogMarkAllAsReadError(ILogger logger, Exception ex, int userId);

    [LoggerMessage(EventId = 4045, Level = LogLevel.Error,
        Message = "Error deleting notification {NotificationId}")]
    private static partial void LogDeleteNotificationError(ILogger logger, Exception ex, int notificationId);

    [LoggerMessage(EventId = 4046, Level = LogLevel.Information,
        Message = "Deleted {Count} old notifications")]
    private static partial void LogOldNotificationsDeleted(ILogger logger, int count);

    [LoggerMessage(EventId = 4047, Level = LogLevel.Error,
        Message = "Error deleting old notifications")]
    private static partial void LogDeleteOldNotificationsError(ILogger logger, Exception ex);

    // --- Settings (4060-4069) ---

    [LoggerMessage(EventId = 4060, Level = LogLevel.Error,
        Message = "Error getting settings for user {UserId}")]
    private static partial void LogGetSettingsError(ILogger logger, Exception ex, int userId);

    [LoggerMessage(EventId = 4061, Level = LogLevel.Error,
        Message = "Error updating settings for user {UserId}")]
    private static partial void LogUpdateSettingsError(ILogger logger, Exception ex, int userId);

    // --- Background jobs (4070-4079) ---

    [LoggerMessage(EventId = 4070, Level = LogLevel.Information,
        Message = "Low stock check completed. Checked {Count} products.")]
    private static partial void LogLowStockCheckCompleted(ILogger logger, int count);

    [LoggerMessage(EventId = 4071, Level = LogLevel.Error,
        Message = "Error checking low stock")]
    private static partial void LogLowStockCheckError(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 4072, Level = LogLevel.Information,
        Message = "Daily digest sent to {Count} users")]
    private static partial void LogDailyDigestSent(ILogger logger, int count);

    [LoggerMessage(EventId = 4073, Level = LogLevel.Error,
        Message = "Error sending daily digest")]
    private static partial void LogDailyDigestError(ILogger logger, Exception ex);

    // --- Push helpers (4080-4089) ---

    [LoggerMessage(EventId = 4080, Level = LogLevel.Debug,
        Message = "Push notification skipped (not configured): {Title} for user {UserId}")]
    private static partial void LogPushSkippedNotConfigured(ILogger logger, string? title, int userId);

    [LoggerMessage(EventId = 4081, Level = LogLevel.Debug,
        Message = "Push subscription registration skipped (not configured) for user {UserId}")]
    private static partial void LogPushSubscriptionSkipped(ILogger logger, int userId);

    // --- Specific alerts (4090-4099) ---

    [LoggerMessage(EventId = 4090, Level = LogLevel.Error,
        Message = "Error sending low stock alert for {Product}")]
    private static partial void LogSendLowStockAlertError(ILogger logger, Exception ex, string? product);

    [LoggerMessage(EventId = 4091, Level = LogLevel.Error,
        Message = "Error sending expiry alert for {Product}")]
    private static partial void LogSendExpiryAlertError(ILogger logger, Exception ex, string? product);

    [LoggerMessage(EventId = 4092, Level = LogLevel.Error,
        Message = "Error sending security alert")]
    private static partial void LogSendSecurityAlertError(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 4093, Level = LogLevel.Error,
        Message = "Error sending system alert")]
    private static partial void LogSendSystemAlertError(ILogger logger, Exception ex);
}
