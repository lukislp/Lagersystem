namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Event service for cross-component notification updates.
/// Enables NotificationBell live-refresh when MarkAllAsRead is called.
/// </summary>
public interface INotificationEventService
{
    /// <summary>
    /// Raised when notifications have been marked as read.
    /// </summary>
    event Action? OnNotificationsChanged;

    /// <summary>
    /// Triggers the event (calls NotificationBell refresh).
    /// </summary>
    void NotifyChanged();
}
