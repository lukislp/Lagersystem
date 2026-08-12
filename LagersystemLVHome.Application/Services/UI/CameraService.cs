using Microsoft.JSInterop;
using System.Text.Json;

namespace LagersystemLVHome.Application.Services;

public sealed class CameraDevice
{
    public string DeviceId { get; set; } = "";
    public string Label { get; set; } = "";
    public string FacingMode { get; set; } = "unknown";
}

public sealed class CameraService : ICameraService
{
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<CameraService> _logger;

    public CameraService(IJSRuntime jsRuntime, ILogger<CameraService> logger)
    {
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    public async Task<List<CameraDevice>> GetAvailableCamerasAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _jsRuntime.InvokeAsync<JsonElement>("getAvailableCameras");

            var cameras = new List<CameraDevice>();
            if (result.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in result.EnumerateArray())
                {
                    cameras.Add(new CameraDevice
                    {
                        DeviceId = item.GetProperty("deviceId").GetString() ?? "",
                        Label = item.GetProperty("label").GetString() ?? "Camera",
                        FacingMode = item.GetProperty("facingMode").GetString() ?? "unknown"
                    });
                }
            }

            _logger.LogInformation("Found {Count} camera(s)", cameras.Count);
            return cameras;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get available cameras");
            return new List<CameraDevice>();
        }
    }

    public async Task<bool> StartCameraAsync(string? deviceId = null, string facingMode = "environment", CancellationToken cancellationToken = default)
    {
        try
        {
            if (deviceId != null)
            {
                await _jsRuntime.InvokeVoidAsync("startCamera", deviceId, facingMode);
            }
            else
            {
                await _jsRuntime.InvokeVoidAsync("startCamera", null, facingMode);
            }

            _logger.LogInformation("Camera started: {DeviceId}, {FacingMode}", deviceId ?? "default", facingMode);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start camera");
            return false;
        }
    }

    public async Task StopCameraAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("stopCamera");
            _logger.LogInformation("Camera stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop camera");
        }
    }

    public async Task<bool> ToggleTorchAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var enabled = await _jsRuntime.InvokeAsync<bool>("toggleCameraTorch");
            _logger.LogInformation("Torch toggled: {Enabled}", enabled);
            return enabled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle torch");
            return false;
        }
    }

    public async Task<bool> SetZoomAsync(double zoomLevel, CancellationToken cancellationToken = default)
    {
        try
        {
            var success = await _jsRuntime.InvokeAsync<bool>("setCameraZoom", zoomLevel);
            _logger.LogInformation("Zoom set to {Level}x: {Success}", zoomLevel, success);
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set zoom");
            return false;
        }
    }

    public async Task<bool> IsTorchSupportedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<bool>("isTorchSupported");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check torch support");
            return false;
        }
    }

    public async Task<bool> IsTorchEnabledAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<bool>("isTorchEnabled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check torch status");
            return false;
        }
    }
}
