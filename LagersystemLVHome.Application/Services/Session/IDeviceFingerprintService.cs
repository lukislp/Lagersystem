using LagersystemLVHome.Domain.Models;
using Microsoft.AspNetCore.Http;

namespace LagersystemLVHome.Application.Services;

public interface IDeviceFingerprintService
{
    Task<string> GenerateBrowserFingerprintAsync(string instanceId, CancellationToken cancellationToken = default);
    string GenerateFingerprint(HttpContext context);
    Task<bool> IsKnownDeviceAsync(int userId, string fingerprint, CancellationToken cancellationToken = default);
    Task SaveDeviceFingerprintAsync(int sessionId, string fingerprint, HttpContext context, CancellationToken cancellationToken = default);
    Task<List<DeviceInfo>> GetUserDevicesAsync(int userId, CancellationToken cancellationToken = default);
    Task<bool> LinkFingerprintsAsync(int userId, string primaryFingerprint, string linkedFingerprint, string? source = null, CancellationToken cancellationToken = default);
    Task<bool> UnlinkFingerprintAsync(int userId, int linkedId, CancellationToken cancellationToken = default);
    Task<List<LinkedDeviceFingerprint>> GetLinkedFingerprintsAsync(int userId, CancellationToken cancellationToken = default);
    Task<string> ResolvePrimaryFingerprintAsync(int userId, string fingerprint, CancellationToken cancellationToken = default);
}
