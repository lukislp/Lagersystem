using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Soenneker.Blazor.Thumbmarkjs.Abstract;

namespace LagersystemLVHome.Application.Services;

public sealed class DeviceFingerprintService : IDeviceFingerprintService
{
    private readonly IDbContextFactory<InventoryDbContext> _dbContextFactory;
    private readonly ILogger<DeviceFingerprintService> _logger;
    private readonly IThumbmarkjsInterop? _thumbmarkJsInterop;

    public DeviceFingerprintService(
        IDbContextFactory<InventoryDbContext> dbContextFactory,
        ILogger<DeviceFingerprintService> logger,
        IThumbmarkjsInterop? thumbmarkJsInterop = null)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
        _thumbmarkJsInterop = thumbmarkJsInterop;
    }

    /// <summary>
    /// Generates a precise browser fingerprint using Thumbmarkjs.
    /// Uses canvas, WebGL, audio, fonts and other browser features.
    /// </summary>
    public async Task<string> GenerateBrowserFingerprintAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        if (_thumbmarkJsInterop == null)
        {
            _logger.LogWarning("Thumbmarkjs not available - using fallback fingerprint");
            return "fallback-" + Guid.NewGuid().ToString("N")[..16];
        }

        try
        {
            var fingerprint = await _thumbmarkJsInterop.Get(instanceId, cancellationToken);

            if (string.IsNullOrEmpty(fingerprint))
            {
                _logger.LogWarning("Thumbmarkjs returned empty fingerprint - using fallback");
                return "fallback-" + Guid.NewGuid().ToString("N")[..16];
            }

            _logger.LogDebug("Thumbmarkjs fingerprint generated: {Fingerprint}",
                fingerprint[..Math.Min(16, fingerprint.Length)] + "...");

            return fingerprint;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating Thumbmarkjs fingerprint");
            return "error-" + Guid.NewGuid().ToString("N")[..16];
        }
    }

    /// <summary>
    /// Generates a device fingerprint based on HTTP context (server-side fallback).
    /// Does not include IP address for a more stable fingerprint.
    /// </summary>
    public string GenerateFingerprint(HttpContext context)
    {
        try
        {
            var userAgent = context.Request.Headers["User-Agent"].ToString();
            var acceptLanguage = context.Request.Headers["Accept-Language"].ToString();
            var acceptEncoding = context.Request.Headers["Accept-Encoding"].ToString();

            // Additional stable properties
            var screenResolution = context.Request.Headers["Sec-CH-UA-Platform"].ToString();
            var platform = context.Request.Headers["Sec-CH-UA"].ToString();

            // Combine only stable values (without IP)
            var fingerprintData = $"{userAgent}|{acceptLanguage}|{acceptEncoding}|{screenResolution}|{platform}";

            // SHA256 hash
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintData));
            return Convert.ToBase64String(hash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating device fingerprint");
            return "unknown";
        }
    }

    /// <summary>
    /// Checks whether the device is already known (including linked fingerprints).
    /// </summary>
    public async Task<bool> IsKnownDeviceAsync(int userId, string fingerprint, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            // Check direct session with this fingerprint
            var directMatch = await dbContext.UserSessions
                .AnyAsync(s =>
                    s.UserId == userId &&
                    s.DeviceFingerprint == fingerprint &&
                    s.IsActive, cancellationToken);

            if (directMatch) return true;

            // Check linked fingerprints (user-based)
            var linked = await dbContext.LinkedDeviceFingerprints
                .AnyAsync(l =>
                    l.UserId == userId &&
                    (l.LinkedFingerprint == fingerprint || l.PrimaryFingerprint == fingerprint), cancellationToken);

            if (linked)
            {
                // One of the linked fingerprints has an active session.
                // The SelectMany(l => new[] { ... }) flatten has to happen client-side (after
                // materializing the rows) - EF Core cannot translate a per-row array literal
                // into SQL and throws at execution time if it's chained directly onto the query.
                var relatedRows = await dbContext.LinkedDeviceFingerprints
                    .Where(l => l.UserId == userId &&
                        (l.LinkedFingerprint == fingerprint || l.PrimaryFingerprint == fingerprint))
                    .ToListAsync(cancellationToken);

                var allRelated = relatedRows
                    .SelectMany(l => new[] { l.PrimaryFingerprint, l.LinkedFingerprint })
                    .Distinct()
                    .ToList();

                return await dbContext.UserSessions
                    .AnyAsync(s => s.UserId == userId && s.IsActive &&
                        s.DeviceFingerprint != null && allRelated.Contains(s.DeviceFingerprint), cancellationToken);
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking device fingerprint");
            return false;
        }
    }

    /// <summary>
    /// Saves device info to the session including browser, OS and device type.
    /// </summary>
    public async Task SaveDeviceFingerprintAsync(int sessionId, string fingerprint, HttpContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var session = await dbContext.UserSessions.FindAsync(sessionId);
            if (session != null)
            {
                session.DeviceFingerprint = fingerprint;

                // Parse complete device info from user agent
                var (browser, os, deviceType) = ParseUserAgent(context);

                session.Browser = browser;
                session.OperatingSystem = os;
                session.DeviceType = deviceType;
                session.DeviceInfo = deviceType;

                await dbContext.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Device fingerprint saved for session {SessionId}: Browser={Browser}, OS={OS}, Device={DeviceType}, FP={Fingerprint}",
                    sessionId, browser, os, deviceType, fingerprint[..Math.Min(16, fingerprint.Length)] + "...");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving device fingerprint");
        }
    }

    /// <summary>
    /// Parses user agent to extract browser, OS and device type from HTTP context.
    /// </summary>
    private (string browser, string os, string deviceType) ParseUserAgent(HttpContext context)
    {
        try
        {
            var userAgent = context.Request.Headers["User-Agent"].ToString().ToLower();

            // Fallback if user agent is empty (IIS/nginx forwarding issue)
            if (string.IsNullOrEmpty(userAgent))
            {
                _logger.LogWarning("User-Agent header is empty - possible IIS/nginx forwarding issue");

                // Check custom header
                userAgent = context.Request.Headers["X-Original-User-Agent"].ToString().ToLower();

                if (string.IsNullOrEmpty(userAgent))
                {
                    return ("Unknown", "Unknown", "Unknown");
                }
            }

            // Browser detection (most specific first)
            string browser = "Unknown";
            if (userAgent.Contains("edg/")) browser = "Edge";
            else if (userAgent.Contains("opr/") || userAgent.Contains("opera")) browser = "Opera";
            else if (userAgent.Contains("chrome") && !userAgent.Contains("edg")) browser = "Chrome";
            else if (userAgent.Contains("firefox")) browser = "Firefox";
            else if (userAgent.Contains("safari") && !userAgent.Contains("chrome")) browser = "Safari";
            else if (userAgent.Contains("trident") || userAgent.Contains("msie")) browser = "Internet Explorer";

            // OS detection
            string os = "Unknown";
            if (userAgent.Contains("windows nt 10.0")) os = "Windows 10/11";
            else if (userAgent.Contains("windows nt 6.3")) os = "Windows 8.1";
            else if (userAgent.Contains("windows nt 6.2")) os = "Windows 8";
            else if (userAgent.Contains("windows nt 6.1")) os = "Windows 7";
            else if (userAgent.Contains("windows")) os = "Windows";
            else if (userAgent.Contains("iphone")) os = "iOS";
            else if (userAgent.Contains("ipad")) os = "iPadOS";
            else if (userAgent.Contains("mac os x")) os = "macOS";
            else if (userAgent.Contains("android")) os = "Android";
            else if (userAgent.Contains("linux")) os = "Linux";
            else if (userAgent.Contains("cros")) os = "ChromeOS";

            // Device type detection
            string deviceType = "Desktop";
            if (userAgent.Contains("mobile") || userAgent.Contains("android")) deviceType = "Mobile";
            else if (userAgent.Contains("tablet") || userAgent.Contains("ipad")) deviceType = "Tablet";

            _logger.LogDebug("Parsed User-Agent: Browser={Browser}, OS={OS}, DeviceType={DeviceType}",
                browser, os, deviceType);

            return (browser, os, deviceType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing user agent");
            return ("Unknown", "Unknown", "Desktop");
        }
    }

    /// <summary>
    /// Lists all devices for a user.
    /// </summary>
    public async Task<List<DeviceInfo>> GetUserDevicesAsync(int userId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var devices = await dbContext.UserSessions
                .Where(s => s.UserId == userId && s.DeviceFingerprint != null)
                .GroupBy(s => s.DeviceFingerprint)
                .Select(g => new DeviceInfo
                {
                    Fingerprint = g.Key!,
                    DeviceType = g.First().DeviceInfo ?? "Unknown",
                    LastSeen = g.Max(s => s.LastActivity),
                    SessionCount = g.Count(),
                    IsActive = g.Any(s => s.IsActive)
                })
                .OrderByDescending(d => d.LastSeen)
                .ToListAsync(cancellationToken);

            return devices;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading user devices");
            return [];
        }
    }

    public async Task<bool> LinkFingerprintsAsync(int userId, string primaryFingerprint, string linkedFingerprint, string? source = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(primaryFingerprint) || string.IsNullOrEmpty(linkedFingerprint))
            return false;
        if (primaryFingerprint == linkedFingerprint)
            return false;

        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var exists = await dbContext.LinkedDeviceFingerprints
                .AnyAsync(l => l.UserId == userId &&
                    l.PrimaryFingerprint == primaryFingerprint &&
                    l.LinkedFingerprint == linkedFingerprint, cancellationToken);
            if (exists) return true;

            // If the linked fingerprint is itself a primary, take over its links
            var existingLinks = await dbContext.LinkedDeviceFingerprints
                .Where(l => l.UserId == userId && l.PrimaryFingerprint == linkedFingerprint)
                .ToListAsync(cancellationToken);

            foreach (var existingLink in existingLinks)
            {
                existingLink.PrimaryFingerprint = primaryFingerprint;
            }

            // If the linked fingerprint is a linked entry under another primary, remove it there
            var oldLink = await dbContext.LinkedDeviceFingerprints
                .FirstOrDefaultAsync(l => l.UserId == userId && l.LinkedFingerprint == linkedFingerprint, cancellationToken);
            if (oldLink != null)
            {
                dbContext.LinkedDeviceFingerprints.Remove(oldLink);
            }

            // Create new link
            dbContext.LinkedDeviceFingerprints.Add(new LinkedDeviceFingerprint
            {
                UserId = userId,
                PrimaryFingerprint = primaryFingerprint,
                LinkedFingerprint = linkedFingerprint,
                Source = source,
                LinkedAt = DateTime.UtcNow
            });

            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Linked fingerprints for user {UserId}: {Primary} <- {Linked} ({Source})",
                userId,
                primaryFingerprint[..Math.Min(12, primaryFingerprint.Length)],
                linkedFingerprint[..Math.Min(12, linkedFingerprint.Length)],
                source ?? "unknown");

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error linking fingerprints for user {UserId}", userId);
            return false;
        }
    }

    public async Task<bool> UnlinkFingerprintAsync(int userId, int linkedId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var link = await dbContext.LinkedDeviceFingerprints
                .FirstOrDefaultAsync(l => l.Id == linkedId && l.UserId == userId, cancellationToken);

            if (link == null) return false;

            dbContext.LinkedDeviceFingerprints.Remove(link);
            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Unlinked fingerprint {Id} for user {UserId}", linkedId, userId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unlinking fingerprint {Id}", linkedId);
            return false;
        }
    }

    public async Task<List<LinkedDeviceFingerprint>> GetLinkedFingerprintsAsync(int userId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            return await dbContext.LinkedDeviceFingerprints
                .Where(l => l.UserId == userId)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading linked fingerprints for user {UserId}", userId);
            return [];
        }
    }

    public async Task<string> ResolvePrimaryFingerprintAsync(int userId, string fingerprint, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(fingerprint)) return fingerprint;

        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            // Check if this fingerprint is registered as a linked fingerprint
            var link = await dbContext.LinkedDeviceFingerprints
                .FirstOrDefaultAsync(l => l.UserId == userId && l.LinkedFingerprint == fingerprint, cancellationToken);

            return link?.PrimaryFingerprint ?? fingerprint;
        }
        catch
        {
            return fingerprint;
        }
    }
}

public sealed class DeviceInfo
{
    public string Fingerprint { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public DateTime LastSeen { get; set; }
    public int SessionCount { get; set; }
    public bool IsActive { get; set; }
}
