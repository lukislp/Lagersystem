using Microsoft.JSInterop;
using System.Text.Json;

namespace LagersystemLVHome.Application.Services;

public interface ICameraService
{
    Task<List<CameraDevice>> GetAvailableCamerasAsync(CancellationToken cancellationToken = default);
    Task<bool> StartCameraAsync(string? deviceId = null, string facingMode = "environment", CancellationToken cancellationToken = default);
    Task StopCameraAsync(CancellationToken cancellationToken = default);
    Task<bool> ToggleTorchAsync(CancellationToken cancellationToken = default);
    Task<bool> SetZoomAsync(double zoomLevel, CancellationToken cancellationToken = default);
    Task<bool> IsTorchSupportedAsync(CancellationToken cancellationToken = default);
    Task<bool> IsTorchEnabledAsync(CancellationToken cancellationToken = default);
}
