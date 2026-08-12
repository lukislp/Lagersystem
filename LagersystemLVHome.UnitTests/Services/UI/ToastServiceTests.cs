namespace LagersystemLVHome.UnitTests.Services.UI;

public class ToastServiceTests
{
    private sealed record Captured(string Message, string Type, string? Title, int Duration, string? Extra);

    private static (ToastService sut, List<Captured> events) BuildWithSubscriber()
    {
        var sut = new ToastService();
        var events = new List<Captured>();
        sut.OnShow += (msg, type, title, dur, extra) => events.Add(new Captured(msg, type, title, dur, extra));
        return (sut, events);
    }

    [Fact]
    public void ShowSuccess_FiresOnShowWithSuccessType()
    {
        var (sut, events) = BuildWithSubscriber();

        sut.ShowSuccess("done", "OK", duration: 1234);

        events.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new Captured("done", "success", "OK", 1234, null));
    }

    [Fact]
    public void ShowError_FiresOnShowWithErrorType()
    {
        var (sut, events) = BuildWithSubscriber();

        sut.ShowError("boom");

        events.Should().ContainSingle().Which.Type.Should().Be("error");
    }

    [Fact]
    public void ShowWarning_FiresOnShowWithWarningType()
    {
        var (sut, events) = BuildWithSubscriber();

        sut.ShowWarning("careful");

        events.Should().ContainSingle().Which.Type.Should().Be("warning");
    }

    [Fact]
    public void ShowInfo_FiresOnShowWithInfoType()
    {
        var (sut, events) = BuildWithSubscriber();

        sut.ShowInfo("fyi");

        events.Should().ContainSingle().Which.Type.Should().Be("info");
    }

    [Fact]
    public void Show_WithAdditionalClass_PropagatesExtra()
    {
        var (sut, events) = BuildWithSubscriber();

        sut.Show("m", "info", "t", 500, "highlight");

        events.Should().ContainSingle().Which.Extra.Should().Be("highlight");
    }

    [Fact]
    public void Show_NoSubscribers_DoesNotThrow()
    {
        var sut = new ToastService();

        var act = () => sut.ShowInfo("x");

        act.Should().NotThrow();
    }
}
