using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;

namespace LagersystemLVHome.UnitTests.Services.Session;

public class SessionStorageServiceTests
{
    [Fact]
    public async Task SetItemAsync_SerializesAndCallsJs()
    {
        var js = Substitute.For<IJSRuntime>();
        var sut = new SessionStorageService(js);

        await sut.SetItemAsync("k", new { a = 1, b = "x" });

        await js.Received(1).InvokeAsync<IJSVoidResult>(
            "sessionStorage.setItem",
            Arg.Is<object?[]>(args => args.Length == 2
                && (string)args[0]! == "k"
                && ((string)args[1]!).Contains("\"a\":1")));
    }

    [Fact]
    public async Task GetItemAsync_RoundtripsValueFromJs()
    {
        var js = Substitute.For<IJSRuntime>();
        js.InvokeAsync<string>("sessionStorage.getItem", Arg.Any<object?[]>())
            .Returns(new ValueTask<string>("\"hello\""));
        var sut = new SessionStorageService(js);

        var value = await sut.GetItemAsync<string>("k");

        value.Should().Be("hello");
    }

    [Fact]
    public async Task GetItemAsync_EmptyResponse_ReturnsDefault()
    {
        var js = Substitute.For<IJSRuntime>();
        js.InvokeAsync<string>("sessionStorage.getItem", Arg.Any<object?[]>())
            .Returns(new ValueTask<string>(""));
        var sut = new SessionStorageService(js);

        (await sut.GetItemAsync<string>("k")).Should().BeNull();
    }

    [Fact]
    public async Task GetItemAsync_JsThrows_ReturnsDefault()
    {
        var js = Substitute.For<IJSRuntime>();
        js.InvokeAsync<string>(Arg.Any<string>(), Arg.Any<object?[]>())
            .Returns<ValueTask<string>>(_ => throw new InvalidOperationException("js down"));
        var sut = new SessionStorageService(js);

        (await sut.GetItemAsync<string>("k")).Should().BeNull();
    }

    [Fact]
    public async Task RemoveItemAsync_CallsJsRemove()
    {
        var js = Substitute.For<IJSRuntime>();
        var sut = new SessionStorageService(js);

        await sut.RemoveItemAsync("k");

        await js.Received(1).InvokeAsync<IJSVoidResult>(
            "sessionStorage.removeItem",
            Arg.Is<object?[]>(args => args.Length == 1 && (string)args[0]! == "k"));
    }

    [Fact]
    public async Task SetItemAsync_JsThrows_DoesNotThrow()
    {
        var js = Substitute.For<IJSRuntime>();
        js.InvokeAsync<IJSVoidResult>(Arg.Any<string>(), Arg.Any<object?[]>())
            .Returns<ValueTask<IJSVoidResult>>(_ => throw new InvalidOperationException("js down"));
        var sut = new SessionStorageService(js);

        var act = () => sut.SetItemAsync("k", "v");

        await act.Should().NotThrowAsync();
    }
}
