using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services;

public interface INotificationService
{
    // Create notifications
    Task CreateNotificationAsync(int userId, NotificationType type, string title, string message, string? actionUrl = null, NotificationChannel channel = NotificationChannel.All, CancellationToken cancellationToken = default);
    Task CreateLowStockNotificationAsync(Product product, CancellationToken cancellationToken = default);
    Task CreateCriticalStockNotificationAsync(Product product, CancellationToken cancellationToken = default);
    Task CreateNewUserNotificationAsync(User newUser, CancellationToken cancellationToken = default);
    Task CreateSecurityAlertAsync(int userId, string message, CancellationToken cancellationToken = default);

    // Get notifications
    Task<List<Notification>> GetUserNotificationsAsync(int userId, bool unreadOnly = false, int limit = 50, CancellationToken cancellationToken = default);
    Task<int> GetUnreadCountAsync(int userId, CancellationToken cancellationToken = default);

    // Mark as read
    Task MarkAsReadAsync(int notificationId, CancellationToken cancellationToken = default);
    Task MarkAllAsReadAsync(int userId, CancellationToken cancellationToken = default);

    // Delete notifications
    Task DeleteNotificationAsync(int notificationId, CancellationToken cancellationToken = default);
    Task DeleteOldNotificationsAsync(int daysOld = 30, CancellationToken cancellationToken = default);

    // Settings
    Task<UserNotificationSettings> GetUserSettingsAsync(int userId, CancellationToken cancellationToken = default);
    Task UpdateUserSettingsAsync(UserNotificationSettings settings, CancellationToken cancellationToken = default);

    // Background tasks
    Task CheckLowStockAndNotifyAsync(CancellationToken cancellationToken = default);
    Task SendDailyDigestAsync(CancellationToken cancellationToken = default);

    // Push notifications
    Task<bool> SendPushNotificationAsync(int userId, string title, string body, string? url = null, CancellationToken cancellationToken = default);
    Task<bool> RequestPushPermissionAsync(int userId, string subscription, CancellationToken cancellationToken = default);
}
