using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using System.Text.Json;

namespace LagersystemLVHome.UnitTests.Services.UI;

public class CameraServiceTests
{
    private static CameraService Build(IJSRuntime js) => new(js, NullLogger<CameraService>.Instance);

    private static JsonElement ParseArray(string json) => JsonDocument.Parse(json).RootElement;

    // --- GetAvailableCamerasAsync ---

    [Fact]
    public async Task GetAvailableCamerasAsync_MapsAllFieldsFromJsResult()
    {
        var js = Substitute.For<IJSRuntime>();
        js.InvokeAsync<JsonElement>("getAvailableCameras", Arg.Any<object?[]>())
            .Returns(new ValueTask<JsonElement>(ParseArray(
                """[{"deviceId":"cam1","label":"Front Camera","facingMode":"user"}]""")));
        var sut = Build(js);

        var cameras = await sut.GetAvailableCamerasAsync();

        cameras.Should().ContainSingle();
        cameras[0].DeviceId.Should().Be("cam1");
        cameras[0].Label.Should().Be("Front Camera");
        cameras[0].FacingMode.Should().Be("user");
    }

    [Fact]
    public async Task GetAvailableCamerasAsync_NullLabelAndFacingMode_FallsBackToDefaults()
    {
        var js = Substitute.For<IJSRuntime>();
        js.InvokeAsync<JsonElement>("getAvailableCameras", Arg.Any<object?[]>())
            .Returns(new ValueTask<JsonElement>(ParseArray(
                """[{"deviceId":"cam1","label":null,"facingMode":null}]""")));
        var sut = Build(js);

        var cameras = await sut.GetAvailableCamerasAsync();

        cameras[0].Label.Should().Be("Camera");
        cameras[0].FacingMode.Should().Be("unknown");
    }

    [Fact]
    public async Task GetAvailableCamerasAsync_MultipleEntries_ReturnsAllOfThem()
    {
        var js = Substitute.For<IJSRuntime>();
        js.InvokeAsync<JsonElement>("getAvailableCameras", Arg.Any<object?[]>())
            .Returns(new ValueTask<JsonElement>(ParseArray(
                """[{"deviceId":"cam1","label":"A","facingMode":"user"},{"deviceId":"cam2","label":"B","facingMode":"environment"}]""")));
        var sut = Build(js);

        var cameras = await sut.GetAvailableCamerasAsync();

        cameras.Should().HaveCount(2);
        cameras.Select(c => c.DeviceId).Should().Equal("cam1", "cam2");
    }

    [Fact]
    public async Task GetAvailableCamerasAsync_NonArrayResult_ReturnsEmptyList()
    {
        var js = Substitute.For<IJSRuntime>();
        js.InvokeAsync<JsonElement>("getAvailableCameras", Arg.Any<object?[]>())
            .Returns(new ValueTask<JsonElement>(ParseArray("""{}""")));
        var sut = Build(js);

        (await sut.GetAvailableCamerasAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task GetAvailableCamerasAsync_JsThrows_ReturnsEmptyList()
    {
        var js = Substitute.For<IJSRuntime>();
        js.InvokeAsync<JsonElement>(Arg.Any<string>(), Arg.Any<object?[]>())
            .Returns<ValueTask<JsonElement>>(_ => throw new InvalidOperationException("js down"));
        var sut = Build(js);

        (await sut.GetAvailableCamerasAsync()).Should().BeEmpty();
    }

    // --- StartCameraAsync ---

    [Fact]
    public async Task StartCameraAsync_WithDeviceId_PassesDeviceIdAndFacingMode_ReturnsTrue()
    {
        var js = Substitute.For<IJSRuntime>();
        var sut = Build(js);

        var result = await sut.StartCameraAsync("cam1", "user");

        result.Should().BeTrue();
        await js.Received(1).InvokeAsync<IJSVoidResult>(
            "startCamera", Arg.Is<object?[]>(a => a.Length == 2 && (string)a[0]! == "cam1" && (string)a[1]! == "user"));
    }

    [Fact]
    public async Task StartCameraAsync_NoDeviceId_PassesNullDeviceId_ReturnsTrue()
    {
        var js = Substitute.For<IJSRuntime>();
        var sut = Build(js);

        var result = await sut.StartCameraAsync(facingMode: "environment");

        result.Should().BeTrue();
        await js.Received(1).InvokeAsync<IJSVoidResult>(
            "startCamera", Arg.Is<object?[]>(a => a.Length == 2 && a[0] == null && (string)a[1]! == "environment"));
    }

    [Fact]
    public async Task StartCameraAsync_JsThrows_ReturnsFalse()
    {
        var js = Substitute.For<IJSRuntime>();
        js.InvokeAsync<IJSVoidResult>(Arg.Any<string>(), Arg.Any<object?[]>())
            .Returns<ValueTask<IJSVoidResult>>(_ => throw new InvalidOperationException("js down"));
        var sut = Build(js);

        (await sut.StartCameraAsync("cam1")).Should().BeFalse();
    }

    // --- StopCameraAsync ---

    [Fact]
    public async Task StopCameraAsync_InvokesJsStopCamera()
    {
        var js = Substitute.For<IJSRuntime>();
        var sut = Build(js);

        await sut.StopCameraAsync();

        await js.Received(1).InvokeAsync<IJSVoidResult>("stopCamera", Arg.Any<object?[]>());
    }

    [Fact]
    public async Task StopCameraAsync_JsThrows_DoesNotThrow()
    {
        var js = Substitute.For<IJSRuntime>();
        js.InvokeAsync<IJSVoidResult>(Arg.Any<string>(), Arg.Any<object?[]>())
            .Returns<ValueTask<IJSVoidResult>>(_ => throw new InvalidOperationException("js down"));
        var sut = Build(js);

        var act = () => sut.StopCameraAsync();

        await act.Should().NotThrowAsync();
    }

    // --- ToggleTorchAsync ---

    [Fact]
    public async Task ToggleTorchAsync_ReturnsJsResult()
    {
        var js = Substitute.For<IJSRuntime>();
        js.InvokeAsync<bool>("toggleCameraTorch", Arg.Any<object?[]>()).Returns(new ValueTask<bool>(true));
        var sut = Build(js);

        (await sut.ToggleTorchAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task ToggleTorchAsync_JsThrows_ReturnsFalse()
    {
        var js = Substitute.For<IJSRuntime>();
        js.InvokeAsync<bool>(Arg.Any<string>(), Arg.Any<object?[]>())
            .Returns<ValueTask<bool>>(_ => throw new InvalidOperationException("js down"));
        var sut = Build(js);

        (await sut.ToggleTorchAsync()).Should().BeFalse();
    }

    // --- SetZoomAsync ---

    [Fact]
    public async Task SetZoomAsync_PassesZoomLevel_ReturnsJsResult()
    {
        var js = Substitute.For<IJSRuntime>();
        js.InvokeAsync<bool>("setCameraZoom", Arg.Any<object?[]>()).Returns(new ValueTask<bool>(true));
        var sut = Build(js);

        var result = await sut.SetZoomAsync(2.5);

        result.Should().BeTrue();
        await js.Received(1).InvokeAsync<bool>(
            "setCameraZoom", Arg.Is<object?[]>(a => a.Length == 1 && (double)a[0]! == 2.5));
    }

    [Fact]
    public async Task SetZoomAsync_JsThrows_ReturnsFalse()
    {
        var js = Substitute.For<IJSRuntime>();
        js.InvokeAsync<bool>(Arg.Any<string>(), Arg.Any<object?[]>())
            .Returns<ValueTask<bool>>(_ => throw new InvalidOperationException("js down"));
        var sut = Build(js);

        (await sut.SetZoomAsync(1.0)).Should().BeFalse();
    }

    // --- IsTorchSupportedAsync ---

    [Fact]
    public async Task IsTorchSupportedAsync_ReturnsJsResult()
    {
        var js = Substitute.For<IJSRuntime>();
        js.InvokeAsync<bool>("isTorchSupported", Arg.Any<object?[]>()).Returns(new ValueTask<bool>(true));
        var sut = Build(js);

        (await sut.IsTorchSupportedAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task IsTorchSupportedAsync_JsThrows_ReturnsFalse()
    {
        var js = Substitute.For<IJSRuntime>();
        js.InvokeAsync<bool>(Arg.Any<string>(), Arg.Any<object?[]>())
            .Returns<ValueTask<bool>>(_ => throw new InvalidOperationException("js down"));
        var sut = Build(js);

        (await sut.IsTorchSupportedAsync()).Should().BeFalse();
    }

    // --- IsTorchEnabledAsync ---

    [Fact]
    public async Task IsTorchEnabledAsync_ReturnsJsResult()
    {
        var js = Substitute.For<IJSRuntime>();
        js.InvokeAsync<bool>("isTorchEnabled", Arg.Any<object?[]>()).Returns(new ValueTask<bool>(true));
        var sut = Build(js);

        (await sut.IsTorchEnabledAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task IsTorchEnabledAsync_JsThrows_ReturnsFalse()
    {
        var js = Substitute.For<IJSRuntime>();
        js.InvokeAsync<bool>(Arg.Any<string>(), Arg.Any<object?[]>())
            .Returns<ValueTask<bool>>(_ => throw new InvalidOperationException("js down"));
        var sut = Build(js);

        (await sut.IsTorchEnabledAsync()).Should().BeFalse();
    }
}
