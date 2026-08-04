using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;

namespace LagersystemLVHome.UnitTests.Services.UI;

public class KeyboardShortcutServiceTests
{
    private sealed class Target { }

    private static KeyboardShortcutService Build(IJSRuntime js)
        => new(js, NullLogger<KeyboardShortcutService>.Instance);

    [Fact]
    public async Task RegisterShortcutAsync_InvokesJsRegister()
    {
        var js = Substitute.For<IJSRuntime>();
        var sut = Build(js);
        using var dotNetRef = DotNetObjectReference.Create(new Target());

        await sut.RegisterShortcutAsync("ctrl+s", dotNetRef, "Save", "save action");

        await js.Received(1).InvokeAsync<IJSVoidResult>(
            "registerKeyboardShortcut",
            Arg.Is<object?[]>(args => args.Length == 4 && (string)args[0]! == "ctrl+s"));
    }

    [Fact]
    public async Task UnregisterShortcutAsync_InvokesJsUnregister()
    {
        var js = Substitute.For<IJSRuntime>();
        var sut = Build(js);

        await sut.UnregisterShortcutAsync("ctrl+s");

        await js.Received(1).InvokeAsync<IJSVoidResult>(
            "unregisterKeyboardShortcut",
            Arg.Is<object?[]>(args => args.Length == 1 && (string)args[0]! == "ctrl+s"));
    }

    [Fact]
    public async Task EnableAsync_InvokesEnable()
    {
        var js = Substitute.For<IJSRuntime>();
        var sut = Build(js);

        await sut.EnableAsync();

        await js.Received(1).InvokeAsync<IJSVoidResult>("enableKeyboardShortcuts", Arg.Any<object?[]>());
    }

    [Fact]
    public async Task DisableAsync_InvokesDisable()
    {
        var js = Substitute.For<IJSRuntime>();
        var sut = Build(js);

        await sut.DisableAsync();

        await js.Received(1).InvokeAsync<IJSVoidResult>("disableKeyboardShortcuts", Arg.Any<object?[]>());
    }

    [Fact]
    public async Task RegisterShortcutAsync_JsThrows_DoesNotThrow()
    {
        var js = Substitute.For<IJSRuntime>();
        js.InvokeAsync<IJSVoidResult>(Arg.Any<string>(), Arg.Any<object?[]>())
            .Returns<ValueTask<IJSVoidResult>>(_ => throw new InvalidOperationException("js down"));
        var sut = Build(js);
        using var dotNetRef = DotNetObjectReference.Create(new Target());

        var act = () => sut.RegisterShortcutAsync("ctrl+s", dotNetRef, "Save");

        await act.Should().NotThrowAsync();
    }
}
