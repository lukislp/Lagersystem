namespace LagersystemLVHome.UnitTests.Services.Notification;

public class NotificationEventServiceTests
{
    [Fact]
    public void NotifyChanged_FiresEvent_ForAllSubscribers()
    {
        var sut = new NotificationEventService();
        var hits = 0;
        Action handler1 = () => hits++;
        Action handler2 = () => hits++;
        sut.OnNotificationsChanged += handler1;
        sut.OnNotificationsChanged += handler2;

        sut.NotifyChanged();

        hits.Should().Be(2);
    }

    [Fact]
    public void NotifyChanged_NoSubscribers_DoesNotThrow()
    {
        var sut = new NotificationEventService();
        var act = () => sut.NotifyChanged();
        act.Should().NotThrow();
    }

    [Fact]
    public void Unsubscribe_StopsReceivingEvents()
    {
        var sut = new NotificationEventService();
        var hits = 0;
        Action handler = () => hits++;
        sut.OnNotificationsChanged += handler;

        sut.NotifyChanged();
        sut.OnNotificationsChanged -= handler;
        sut.NotifyChanged();

        hits.Should().Be(1);
    }
}
