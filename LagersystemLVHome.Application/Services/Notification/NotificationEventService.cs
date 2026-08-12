namespace LagersystemLVHome.Application.Services;

public sealed class NotificationEventService : INotificationEventService
{
    public event Action? OnNotificationsChanged;

    public void NotifyChanged()
    {
        OnNotificationsChanged?.Invoke();
    }
}
