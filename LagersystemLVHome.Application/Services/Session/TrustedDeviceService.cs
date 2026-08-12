using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services;

public sealed class TrustedDeviceService : ITrustedDeviceService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly ILogger<TrustedDeviceService> _logger;
    private readonly IAuditService? _auditService;

    public TrustedDeviceService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        ILogger<TrustedDeviceService> logger,
        IAuditService? auditService = null)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _auditService = auditService;
    }

    public async Task<bool> IsDeviceTrustedAsync(int userId, string deviceFingerprint, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(deviceFingerprint))
            return false;

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Check primary fingerprint
        var trusted = await context.TrustedDevices
            .AnyAsync(t =>
                t.UserId == userId &&
                t.DeviceFingerprint == deviceFingerprint &&
                t.IsActive &&
                t.ExpiresAt > DateTime.UtcNow, cancellationToken);

        // Check linked fingerprints: is this FP a LinkedFingerprint
        // whose PrimaryFingerprint exists as a TrustedDevice?
        if (!trusted)
        {
            var primaryFp = await context.LinkedDeviceFingerprints
                .Where(l => l.UserId == userId && l.LinkedFingerprint == deviceFingerprint)
                .Select(l => l.PrimaryFingerprint)
                .FirstOrDefaultAsync(cancellationToken);

            if (primaryFp != null)
            {
                trusted = await context.TrustedDevices
                    .AnyAsync(t =>
                        t.UserId == userId &&
                        t.DeviceFingerprint == primaryFp &&
                        t.IsActive &&
                        t.ExpiresAt > DateTime.UtcNow, cancellationToken);
            }
        }

        // Check reverse: is this FP a Primary whose LinkedFingerprint is trusted?
        if (!trusted)
        {
            var linkedFps = await context.LinkedDeviceFingerprints
                .Where(l => l.UserId == userId && l.PrimaryFingerprint == deviceFingerprint)
                .Select(l => l.LinkedFingerprint)
                .ToListAsync(cancellationToken);

            if (linkedFps.Count > 0)
            {
                trusted = await context.TrustedDevices
                    .AnyAsync(t =>
                        t.UserId == userId &&
                        linkedFps.Contains(t.DeviceFingerprint) &&
                        t.IsActive &&
                        t.ExpiresAt > DateTime.UtcNow, cancellationToken);
            }
        }

        if (trusted)
        {
            _logger.LogInformation("Trusted device found for user {UserId}, FP: {FP}",
                userId, deviceFingerprint[..Math.Min(16, deviceFingerprint.Length)] + "...");
        }

        return trusted;
    }

    public async Task<bool> TrustDeviceAsync(int userId, string deviceFingerprint, string? deviceName = null, string? ipAddress = null, int trustDays = 30, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(deviceFingerprint))
            return false;

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await context.TrustedDevices
            .FirstOrDefaultAsync(t =>
                t.UserId == userId &&
                t.DeviceFingerprint == deviceFingerprint, cancellationToken);

        if (existing != null)
        {
            // Renew trust
            existing.IsActive = true;
            existing.TrustedAt = DateTime.UtcNow;
            existing.ExpiresAt = DateTime.UtcNow.AddDays(trustDays);
            existing.IpAddress = ipAddress;
            if (!string.IsNullOrEmpty(deviceName))
                existing.DeviceName = deviceName;
        }
        else
        {
            var trustedDevice = new TrustedDevice
            {
                UserId = userId,
                DeviceFingerprint = deviceFingerprint,
                DeviceName = deviceName,
                TrustedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(trustDays),
                IpAddress = ipAddress,
                IsActive = true
            };
            context.TrustedDevices.Add(trustedDevice);
        }

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Device trusted for user {UserId}: {DeviceName}, expires {Expires}",
            userId, deviceName ?? "Unknown", DateTime.UtcNow.AddDays(trustDays));

        if (_auditService != null)
        {
            await _auditService.LogAsync("DEVICE_TRUSTED", "TrustedDevice", userId,
                new { DeviceName = deviceName, TrustDays = trustDays }, AuditSeverity.Info);
        }

        return true;
    }

    public async Task<bool> UntrustDeviceAsync(int userId, string deviceFingerprint, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(deviceFingerprint))
            return false;

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var device = await context.TrustedDevices
            .FirstOrDefaultAsync(t =>
                t.UserId == userId &&
                t.DeviceFingerprint == deviceFingerprint, cancellationToken);

        if (device == null)
            return false;

        context.TrustedDevices.Remove(device);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Device untrusted for user {UserId}: {DeviceName}",
            userId, device.DeviceName ?? "Unknown");

        if (_auditService != null)
        {
            await _auditService.LogAsync("DEVICE_UNTRUSTED", "TrustedDevice", userId,
                new { DeviceName = device.DeviceName }, AuditSeverity.Info);
        }

        return true;
    }

    public async Task<bool> UntrustDeviceByIdAsync(int userId, int trustedDeviceId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var device = await context.TrustedDevices
            .FirstOrDefaultAsync(t => t.Id == trustedDeviceId && t.UserId == userId, cancellationToken);

        if (device == null)
            return false;

        context.TrustedDevices.Remove(device);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Device untrusted by ID for user {UserId}: {DeviceName}",
            userId, device.DeviceName ?? "Unknown");

        if (_auditService != null)
        {
            await _auditService.LogAsync("DEVICE_UNTRUSTED", "TrustedDevice", userId,
                new { DeviceName = device.DeviceName, TrustedDeviceId = trustedDeviceId }, AuditSeverity.Info);
        }

        return true;
    }

    public async Task<List<TrustedDevice>> GetTrustedDevicesAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.TrustedDevices
            .Where(t => t.UserId == userId && t.IsActive && t.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(t => t.TrustedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var expired = await context.TrustedDevices
            .Where(t => t.ExpiresAt < DateTime.UtcNow || !t.IsActive)
            .ToListAsync(cancellationToken);

        if (expired.Count > 0)
        {
            context.TrustedDevices.RemoveRange(expired);
            await context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Cleaned up {Count} expired trusted devices", expired.Count);
        }
    }
}
