using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services;

public interface ITrustedDeviceService
{
    Task<bool> IsDeviceTrustedAsync(int userId, string deviceFingerprint, CancellationToken cancellationToken = default);
    Task<bool> TrustDeviceAsync(int userId, string deviceFingerprint, string? deviceName = null, string? ipAddress = null, int trustDays = 30, CancellationToken cancellationToken = default);
    Task<bool> UntrustDeviceAsync(int userId, string deviceFingerprint, CancellationToken cancellationToken = default);
    Task<bool> UntrustDeviceByIdAsync(int userId, int trustedDeviceId, CancellationToken cancellationToken = default);
    Task<List<TrustedDevice>> GetTrustedDevicesAsync(int userId, CancellationToken cancellationToken = default);
    Task CleanupExpiredAsync(CancellationToken cancellationToken = default);
}
