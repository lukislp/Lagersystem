using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Application.Utilities;
using Microsoft.Extensions.Options;

namespace LagersystemLVHome.Application.Services;

public sealed partial class SessionManagementService : ISessionManagementService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<SessionManagementService> _logger;
    private readonly VpnDetectionConfig _vpnConfig;

    public SessionManagementService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        IHttpContextAccessor httpContextAccessor,
        ILogger<SessionManagementService> logger,
        IOptions<VpnDetectionConfig> vpnConfig)
    {
        _contextFactory = contextFactory;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _vpnConfig = vpnConfig.Value;
    }

    public async Task<Domain.Models.UserSession> CreateSessionAsync(int userId, int warehouseId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context.Users.FindAsync(userId);
        if (user == null)
            throw new ArgumentException("User not found");

        // IP detection with multiple fallbacks
        var clientIp = ipAddress;
        var debugInfo = new List<string> { $"Param:{ipAddress ?? "NULL"}" };

        if (string.IsNullOrEmpty(clientIp) || clientIp == "::1" || clientIp == "127.0.0.1")
        {
            // Fallback 1: X-Forwarded-For header
            var forwardedFor = _httpContextAccessor?.HttpContext?.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            debugInfo.Add($"XFF:{forwardedFor ?? "NULL"}");
            if (!string.IsNullOrEmpty(forwardedFor))
            {
                clientIp = forwardedFor.Split(',')[0].Trim();
            }
            else
            {
                // Fallback 2: X-Real-IP header
                var realIp = _httpContextAccessor?.HttpContext?.Request.Headers["X-Real-IP"].FirstOrDefault();
                debugInfo.Add($"XRI:{realIp ?? "NULL"}");
                if (!string.IsNullOrEmpty(realIp))
                {
                    clientIp = realIp;
                }
                else
                {
                    // Fallback 3: X-Original-For (nginx)
                    var originalFor = _httpContextAccessor?.HttpContext?.Request.Headers["X-Original-For"].FirstOrDefault();
                    debugInfo.Add($"XOF:{originalFor ?? "NULL"}");
                    if (!string.IsNullOrEmpty(originalFor))
                    {
                        clientIp = originalFor;
                    }
                    else
                    {
                        // Fallback 4: RemoteIpAddress
                        var remoteIp = _httpContextAccessor?.HttpContext?.Connection.RemoteIpAddress?.ToString();
                        debugInfo.Add($"RIP:{remoteIp ?? "NULL"}");
                        clientIp = remoteIp ?? "Unknown";
                    }
                }
            }
        }

        debugInfo.Add($"Final:{clientIp}");

        // Parse user agent
        var deviceType = GetDeviceType(userAgent);
        var browser = GetBrowser(userAgent);
        var os = GetOperatingSystem(userAgent);

        // VPN detection
        var vpnResult = await DetectVpnAsync(clientIp);

        // Risk assessment
        var riskLevel = CalculateRiskLevel(vpnResult, clientIp);

        var session = new Domain.Models.UserSession
        {
            SessionId = Guid.NewGuid().ToString(),
            UserId = userId,
            Username = user.Username,
            WarehouseId = warehouseId,
            StartTime = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow,
            IsActive = true,

            // Device info
            IpAddress = clientIp,
            UserAgent = userAgent,
            DeviceType = deviceType,
            Browser = browser,
            OperatingSystem = os,
            DeviceInfo = deviceType,

            // Geolocation
            Country = vpnResult.Country,
            City = vpnResult.City,
            Region = vpnResult.Region,
            Latitude = vpnResult.Latitude,
            Longitude = vpnResult.Longitude,

            // VPN detection
            IsVpn = vpnResult.IsVpn,
            IsProxy = vpnResult.IsProxy,
            IsTor = vpnResult.IsTor,
            VpnProvider = vpnResult.VpnProvider,
            VpnConfidenceScore = vpnResult.ConfidenceScore,
            HostingProvider = vpnResult.HostingProvider,

            // Security
            RiskLevel = riskLevel,
            RiskFactors = JsonSerializer.Serialize(vpnResult.RiskFactors)
        };

        // Check concurrent logins
        var concurrentCount = await context.UserSessions
            .CountAsync(s => s.UserId == userId && s.IsActive && s.SessionId != session.SessionId, cancellationToken);

        session.IsConcurrent = concurrentCount > 0;
        session.ConcurrentSessionCount = concurrentCount + 1;

        context.UserSessions.Add(session);

        await context.SaveChangesAsync(cancellationToken);

        // Log security event if VPN detected (after session is saved)
        if (vpnResult.IsVpn)
        {
            context.SecurityEvents.Add(new SecurityEvent
            {
                SessionId = session.Id,
                UserId = userId,
                EventType = "VPN_DETECTED",
                Severity = riskLevel >= SessionRiskLevel.High ? SecurityEventSeverity.High : SecurityEventSeverity.Medium,
                Description = $"VPN detected from {vpnResult.Country}",
                IpAddress = clientIp,
                Country = vpnResult.Country,
                IsVpn = true,
                Details = JsonSerializer.Serialize(vpnResult)
            });

            await context.SaveChangesAsync(cancellationToken);
        }

        LogSessionCreated(_logger, userId, clientIp, vpnResult.IsVpn);

        return session;
    }

    public async Task<Domain.Models.UserSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.UserSessions
            .Include(s => s.User)
            .Include(s => s.Warehouse)
            .Include(s => s.Activities)
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);
    }

    public async Task<List<Domain.Models.UserSession>> GetActiveSessionsAsync(int? warehouseId = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.UserSessions
            .AsNoTracking()
            .Include(s => s.User)
            .Include(s => s.Warehouse)
            .Where(s => s.IsActive && s.LastActivity >= DateTime.UtcNow.AddMinutes(-30))
            .AsQueryable();

        if (warehouseId.HasValue)
            query = query.Where(s => s.WarehouseId == warehouseId.Value);

        // Explicit projection to avoid PostgreSQL alias issues
        var sessions = await query
            .Select(s => new Domain.Models.UserSession
            {
                Id = s.Id,
                SessionId = s.SessionId,
                UserId = s.UserId,
                Username = s.Username,
                WarehouseId = s.WarehouseId,
                StartTime = s.StartTime,
                LastActivity = s.LastActivity,
                EndTime = s.EndTime,
                IsActive = s.IsActive,
                IpAddress = s.IpAddress,
                UserAgent = s.UserAgent,
                DeviceType = s.DeviceType,
                Browser = s.Browser,
                OperatingSystem = s.OperatingSystem,
                Country = s.Country,
                City = s.City,
                Region = s.Region,
                Latitude = s.Latitude,
                Longitude = s.Longitude,
                IsVpn = s.IsVpn,
                IsProxy = s.IsProxy,
                IsTor = s.IsTor,
                VpnProvider = s.VpnProvider,
                VpnConfidenceScore = s.VpnConfidenceScore,
                HostingProvider = s.HostingProvider,
                IsSuspicious = s.IsSuspicious,
                SuspiciousActivityCount = s.SuspiciousActivityCount,
                LastSuspiciousActivity = s.LastSuspiciousActivity,
                SuspiciousReason = s.SuspiciousReason,
                IsConcurrent = s.IsConcurrent,
                ConcurrentSessionCount = s.ConcurrentSessionCount,
                RiskLevel = s.RiskLevel,
                RiskFactors = s.RiskFactors,
                PageViewsCount = s.PageViewsCount,
                ApiRequestsCount = s.ApiRequestsCount,
                LastPageUrl = s.LastPageUrl,
                WasForcedLogout = s.WasForcedLogout,
                TerminatedByUserId = s.TerminatedByUserId,
                EndReason = s.EndReason,
                EndReasonDetails = s.EndReasonDetails,
                DeviceFingerprint = s.DeviceFingerprint,
                DeviceInfo = s.DeviceInfo,
                // Navigation properties - load minimal
                User = s.User != null ? new User
                {
                    Id = s.User.Id,
                    Username = s.User.Username,
                    Email = s.User.Email,
                    DisplayName = s.User.DisplayName,
                    Role = s.User.Role
                } : null,
                Warehouse = s.Warehouse != null ? new Warehouse
                {
                    Id = s.Warehouse.Id,
                    Name = s.Warehouse.Name
                } : null
            })
            .OrderByDescending(s => s.LastActivity)
            .ToListAsync(cancellationToken);

        return sessions;
    }

    // Overload for DeviceManagement with onlyActive parameter
    public async Task<List<Domain.Models.UserSession>> GetActiveSessionsAsync(int? warehouseId, bool onlyActive, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.UserSessions
            .AsNoTracking()
            .Include(s => s.User)
            .Include(s => s.Warehouse)
            .AsQueryable();

        if (onlyActive)
        {
            query = query.Where(s => s.IsActive && s.LastActivity >= DateTime.UtcNow.AddMinutes(-30));
        }

        if (warehouseId.HasValue)
            query = query.Where(s => s.WarehouseId == warehouseId.Value);

        // Explicit projection to avoid PostgreSQL alias issues
        var sessions = await query
            .Select(s => new Domain.Models.UserSession
            {
                Id = s.Id,
                SessionId = s.SessionId,
                UserId = s.UserId,
                Username = s.Username,
                WarehouseId = s.WarehouseId,
                StartTime = s.StartTime,
                LastActivity = s.LastActivity,
                EndTime = s.EndTime,
                IsActive = s.IsActive,
                IpAddress = s.IpAddress,
                UserAgent = s.UserAgent,
                DeviceType = s.DeviceType,
                Browser = s.Browser,
                OperatingSystem = s.OperatingSystem,
                Country = s.Country,
                City = s.City,
                Region = s.Region,
                Latitude = s.Latitude,
                Longitude = s.Longitude,
                IsVpn = s.IsVpn,
                IsProxy = s.IsProxy,
                IsTor = s.IsTor,
                VpnProvider = s.VpnProvider,
                VpnConfidenceScore = s.VpnConfidenceScore,
                HostingProvider = s.HostingProvider,
                IsSuspicious = s.IsSuspicious,
                SuspiciousActivityCount = s.SuspiciousActivityCount,
                LastSuspiciousActivity = s.LastSuspiciousActivity,
                SuspiciousReason = s.SuspiciousReason,
                IsConcurrent = s.IsConcurrent,
                ConcurrentSessionCount = s.ConcurrentSessionCount,
                RiskLevel = s.RiskLevel,
                RiskFactors = s.RiskFactors,
                PageViewsCount = s.PageViewsCount,
                ApiRequestsCount = s.ApiRequestsCount,
                LastPageUrl = s.LastPageUrl,
                WasForcedLogout = s.WasForcedLogout,
                TerminatedByUserId = s.TerminatedByUserId,
                EndReason = s.EndReason,
                EndReasonDetails = s.EndReasonDetails,
                DeviceFingerprint = s.DeviceFingerprint,
                DeviceInfo = s.DeviceInfo,
                // Navigation properties - load minimal
                User = s.User != null ? new User
                {
                    Id = s.User.Id,
                    Username = s.User.Username,
                    Email = s.User.Email,
                    DisplayName = s.User.DisplayName,
                    Role = s.User.Role
                } : null,
                Warehouse = s.Warehouse != null ? new Warehouse
                {
                    Id = s.Warehouse.Id,
                    Name = s.Warehouse.Name
                } : null
            })
            .OrderByDescending(s => s.LastActivity)
            .ToListAsync(cancellationToken);

        return sessions;
    }

    public async Task<List<Domain.Models.UserSession>> GetUserSessionsAsync(int userId, bool onlyActive = true, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.UserSessions
            .AsNoTracking()
            .Include(s => s.Warehouse)
            .Where(s => s.UserId == userId);

        if (onlyActive)
            query = query.Where(s => s.IsActive);

        // Explicit projection without u.CreatedAt
        var sessions = await query
            .Select(s => new Domain.Models.UserSession
            {
                Id = s.Id,
                SessionId = s.SessionId,
                UserId = s.UserId,
                Username = s.Username,
                WarehouseId = s.WarehouseId,
                StartTime = s.StartTime,
                LastActivity = s.LastActivity,
                EndTime = s.EndTime,
                IsActive = s.IsActive,
                IpAddress = s.IpAddress,
                UserAgent = s.UserAgent,
                DeviceType = s.DeviceType,
                Browser = s.Browser,
                OperatingSystem = s.OperatingSystem,
                Country = s.Country,
                City = s.City,
                Region = s.Region,
                Latitude = s.Latitude,
                Longitude = s.Longitude,
                IsVpn = s.IsVpn,
                IsProxy = s.IsProxy,
                IsTor = s.IsTor,
                VpnProvider = s.VpnProvider,
                VpnConfidenceScore = s.VpnConfidenceScore,
                HostingProvider = s.HostingProvider,
                IsSuspicious = s.IsSuspicious,
                SuspiciousActivityCount = s.SuspiciousActivityCount,
                LastSuspiciousActivity = s.LastSuspiciousActivity,
                SuspiciousReason = s.SuspiciousReason,
                IsConcurrent = s.IsConcurrent,
                ConcurrentSessionCount = s.ConcurrentSessionCount,
                RiskLevel = s.RiskLevel,
                RiskFactors = s.RiskFactors,
                PageViewsCount = s.PageViewsCount,
                ApiRequestsCount = s.ApiRequestsCount,
                LastPageUrl = s.LastPageUrl,
                WasForcedLogout = s.WasForcedLogout,
                TerminatedByUserId = s.TerminatedByUserId,
                EndReason = s.EndReason,
                EndReasonDetails = s.EndReasonDetails,
                DeviceFingerprint = s.DeviceFingerprint,
                DeviceInfo = s.DeviceInfo,
                // Navigation properties
                Warehouse = s.Warehouse != null ? new Warehouse
                {
                    Id = s.Warehouse.Id,
                    Name = s.Warehouse.Name
                } : null
            })
            .OrderByDescending(s => s.StartTime)
            .ToListAsync(cancellationToken);

        return sessions;
    }

    /// <summary>
    /// Gets an active session by UserId AND DeviceFingerprint.
    /// This prevents updating the wrong session when a user has multiple active sessions.
    /// Uses User-Agent as a secondary matching criterion.
    /// </summary>
    public async Task<Domain.Models.UserSession?> GetSessionByUserAndFingerprintAsync(
        int userId, string? deviceFingerprint, bool onlyActive = true, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.UserSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.DeviceType != "API");

        if (onlyActive)
            query = query.Where(s => s.IsActive);

        // Strategy 1: Exact fingerprint match (if cookie is present)
        if (!string.IsNullOrEmpty(deviceFingerprint))
        {
            var sessionByFingerprint = await query
                .Where(s => s.DeviceFingerprint == deviceFingerprint)
                .OrderByDescending(s => s.LastActivity)
                .Select(s => new Domain.Models.UserSession
                {
                    Id = s.Id,
                    SessionId = s.SessionId,
                    UserId = s.UserId,
                    Username = s.Username,
                    WarehouseId = s.WarehouseId,
                    StartTime = s.StartTime,
                    LastActivity = s.LastActivity,
                    EndTime = s.EndTime,
                    IsActive = s.IsActive,
                    IpAddress = s.IpAddress,
                    UserAgent = s.UserAgent,
                    DeviceType = s.DeviceType,
                    Browser = s.Browser,
                    OperatingSystem = s.OperatingSystem,
                    Country = s.Country,
                    City = s.City,
                    DeviceFingerprint = s.DeviceFingerprint,
                    DeviceInfo = s.DeviceInfo,
                    PageViewsCount = s.PageViewsCount,
                    ApiRequestsCount = s.ApiRequestsCount,
                    LastPageUrl = s.LastPageUrl,
                    RiskLevel = s.RiskLevel
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (sessionByFingerprint != null)
            {
                LogSessionFoundByFingerprint(_logger, sessionByFingerprint.SessionId, userId);
                return sessionByFingerprint;
            }

            LogSessionNotFoundByFingerprint(_logger, userId, deviceFingerprint[..Math.Min(12, deviceFingerprint.Length)]);
        }

        // Strategy 2: User-Agent based match (when no fingerprint cookie is available)
        // This helps on the first request after login, before the fingerprint cookie is set
        var httpContext = _httpContextAccessor?.HttpContext;
        var currentUserAgent = httpContext?.Request.Headers["User-Agent"].ToString();

        if (!string.IsNullOrEmpty(currentUserAgent))
        {
            var sessionByUserAgent = await query
                .Where(s => s.UserAgent == currentUserAgent)
                .OrderByDescending(s => s.LastActivity)
                .Select(s => new Domain.Models.UserSession
                {
                    Id = s.Id,
                    SessionId = s.SessionId,
                    UserId = s.UserId,
                    Username = s.Username,
                    WarehouseId = s.WarehouseId,
                    StartTime = s.StartTime,
                    LastActivity = s.LastActivity,
                    EndTime = s.EndTime,
                    IsActive = s.IsActive,
                    IpAddress = s.IpAddress,
                    UserAgent = s.UserAgent,
                    DeviceType = s.DeviceType,
                    Browser = s.Browser,
                    OperatingSystem = s.OperatingSystem,
                    Country = s.Country,
                    City = s.City,
                    DeviceFingerprint = s.DeviceFingerprint,
                    DeviceInfo = s.DeviceInfo,
                    PageViewsCount = s.PageViewsCount,
                    ApiRequestsCount = s.ApiRequestsCount,
                    LastPageUrl = s.LastPageUrl,
                    RiskLevel = s.RiskLevel
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (sessionByUserAgent != null)
            {
                LogSessionFoundByUserAgent(_logger, sessionByUserAgent.SessionId, userId);
                return sessionByUserAgent;
            }
        }

        // Strategy 3: Most recent active session (fallback - only when no better method available)
        var fallbackSession = await query
            .OrderByDescending(s => s.LastActivity)
            .Select(s => new Domain.Models.UserSession
            {
                Id = s.Id,
                SessionId = s.SessionId,
                UserId = s.UserId,
                Username = s.Username,
                WarehouseId = s.WarehouseId,
                StartTime = s.StartTime,
                LastActivity = s.LastActivity,
                EndTime = s.EndTime,
                IsActive = s.IsActive,
                IpAddress = s.IpAddress,
                UserAgent = s.UserAgent,
                DeviceType = s.DeviceType,
                Browser = s.Browser,
                OperatingSystem = s.OperatingSystem,
                Country = s.Country,
                City = s.City,
                DeviceFingerprint = s.DeviceFingerprint,
                DeviceInfo = s.DeviceInfo,
                PageViewsCount = s.PageViewsCount,
                ApiRequestsCount = s.ApiRequestsCount,
                LastPageUrl = s.LastPageUrl,
                RiskLevel = s.RiskLevel
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (fallbackSession != null)
        {
            LogFallbackSession(_logger,
                fallbackSession.SessionId, userId,
                fallbackSession.DeviceFingerprint?[..Math.Min(12, fallbackSession.DeviceFingerprint?.Length ?? 0)] ?? "NULL",
                fallbackSession.UserAgent?[..Math.Min(50, fallbackSession.UserAgent?.Length ?? 0)] ?? "NULL");
        }

        return fallbackSession;
    }

    public async Task UpdateSessionActivityAsync(string sessionId, string? pageUrl = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var session = await context.UserSessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.IsActive, cancellationToken);

        if (session == null)
            return;

        session.LastActivity = DateTime.UtcNow;
        session.PageViewsCount++;
        if (!string.IsNullOrEmpty(pageUrl))
            session.LastPageUrl = pageUrl;

        // Update IP and User-Agent if available in HttpContext
        var httpContext = _httpContextAccessor?.HttpContext;
        if (httpContext != null)
        {
            // Get current IP (with X-Forwarded-For support)
            var currentIp = httpContext.Connection.RemoteIpAddress?.ToString();
            var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwardedFor))
            {
                currentIp = forwardedFor.Split(',')[0].Trim();
            }

            // Get current user agent
            var currentUserAgent = httpContext.Request.Headers["User-Agent"].ToString();

            // Update only if values are valid and different
            if (!string.IsNullOrEmpty(currentIp) && currentIp != "Unknown" && currentIp != "::1")
            {
                if (session.IpAddress != currentIp)
                {
                    LogSessionIpUpdate(_logger, sessionId, session.IpAddress, currentIp);
                    session.IpAddress = currentIp;

                    // Update geolocation on IP change
                    try
                    {
                        var geoLocationService = httpContext.RequestServices.GetService<IGeoLocationService>();
                        if (geoLocationService != null && geoLocationService.IsAvailable)
                        {
                            var vpnResult = await DetectVpnAsync(currentIp);

                            if (!string.IsNullOrEmpty(vpnResult.Country))
                            {
                                LogSessionGeoUpdate(_logger, sessionId, session.Country, vpnResult.Country);

                                session.Country = vpnResult.Country;
                                session.City = vpnResult.City;
                                session.Region = vpnResult.Region;
                                session.Latitude = vpnResult.Latitude;
                                session.Longitude = vpnResult.Longitude;

                                session.IsVpn = vpnResult.IsVpn;
                                session.VpnProvider = vpnResult.VpnProvider;
                                session.VpnConfidenceScore = vpnResult.ConfidenceScore;
                                session.IsProxy = vpnResult.IsProxy;
                                session.IsTor = vpnResult.IsTor;
                                session.HostingProvider = vpnResult.HostingProvider;

                                // Recalculate risk level
                                session.RiskLevel = CalculateRiskLevel(vpnResult, currentIp);
                                session.RiskFactors = JsonSerializer.Serialize(vpnResult.RiskFactors);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogSessionGeoUpdateFailed(_logger, ex, sessionId);
                    }
                }
            }

            if (!string.IsNullOrEmpty(currentUserAgent) && currentUserAgent.Length > 10)
            {
                if (session.UserAgent != currentUserAgent)
                {
                    LogSessionUserAgentUpdate(_logger, sessionId);
                    session.UserAgent = currentUserAgent;
                }
            }
        }

        // Add activity record
        context.SessionActivities.Add(new SessionActivity
        {
            SessionId = session.Id,
            ActivityType = "PageView",
            PageUrl = pageUrl,
            IpAddress = session.IpAddress
        });

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateSessionFingerprintAsync(
        string sessionId,
        string fingerprint,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(fingerprint))
        {
            return false;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var session = await context.UserSessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.IsActive, cancellationToken);

        if (session == null)
        {
            LogFingerprintUpdateSessionMissing(_logger, sessionId);
            return false;
        }

        session.DeviceFingerprint = fingerprint;
        session.LastActivity = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task EndSessionAsync(string sessionId, SessionEndReason reason, string? details = null, int? terminatedByUserId = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var session = await context.UserSessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.IsActive, cancellationToken);

        if (session == null)
            return;

        session.IsActive = false;
        session.EndTime = DateTime.UtcNow;
        session.EndReason = reason;
        session.EndReasonDetails = details;
        session.WasForcedLogout = terminatedByUserId.HasValue;
        session.TerminatedByUserId = terminatedByUserId;

        await context.SaveChangesAsync(cancellationToken);

        LogSessionEnded(_logger, sessionId, reason);
    }

    public async Task ForceLogoutAsync(string sessionId, int adminUserId, string reason, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var session = await context.UserSessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);

        if (session == null)
            return;

        await EndSessionAsync(sessionId, SessionEndReason.AdminForceLogout, reason, adminUserId);

        context.SecurityEvents.Add(new SecurityEvent
        {
            SessionId = session.Id,
            UserId = session.UserId,
            EventType = "ADMIN_FORCE_LOGOUT",
            Severity = SecurityEventSeverity.High,
            Description = $"Session forcefully terminated by admin",
            Details = JsonSerializer.Serialize(new { AdminUserId = adminUserId, Reason = reason })
        });

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task ForceLogoutUserAsync(int userId, int adminUserId, string reason, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var activeSessions = await context.UserSessions
            .Where(s => s.UserId == userId && s.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var session in activeSessions)
        {
            await ForceLogoutAsync(session.SessionId, adminUserId, reason);
        }
    }

    public async Task<bool> DetectSessionHijackingAsync(string sessionId, string currentIp, string currentUserAgent, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var session = await context.UserSessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.IsActive, cancellationToken);

        if (session == null)
            return false;

        // Check for IP address change
        var ipChanged = session.IpAddress != currentIp;

        // Check for user agent change
        var uaChanged = session.UserAgent != currentUserAgent;

        // If both changed, highly suspicious
        if (ipChanged && uaChanged)
        {
            await MarkSessionSuspiciousAsync(sessionId, "IP and User-Agent changed");
            return true;
        }

        // Check for geolocation anomaly
        if (ipChanged)
        {
            var newVpnResult = await DetectVpnAsync(currentIp);

            // Check whether both countries are local (Localhost, Local Network, Private IPs)
            var oldCountryIsLocal = IsLocalCountry(session.Country);
            var newCountryIsLocal = IsLocalCountry(newVpnResult.Country);

            // If both are local, skip impossible travel detection
            if (oldCountryIsLocal && newCountryIsLocal)
            {
                LogSkipImpossibleTravelLocal(_logger, session.Country, newVpnResult.Country);
                return false;
            }

            // Different country = suspicious (only when not both local)
            if (newVpnResult.Country != session.Country && !oldCountryIsLocal)
            {
                var timeDiff = (DateTime.UtcNow - session.LastActivity).TotalMinutes;

                // Travel time impossible?
                if (timeDiff < 60)
                {
                    await MarkSessionSuspiciousAsync(sessionId,
                        $"Impossible travel: {session.Country} -> {newVpnResult.Country} in {timeDiff:F2} minutes");
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether a country is considered "local" (Localhost, Local Network, Private IP).
    /// </summary>
    private bool IsLocalCountry(string? country)
    {
        if (string.IsNullOrEmpty(country))
            return false;

        var localKeywords = new[] { "localhost", "local network", "private", "lan", "127.0.0.1", "::1" };

        return localKeywords.Any(keyword =>
            country.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    public async Task MarkSessionSuspiciousAsync(string sessionId, string reason, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var session = await context.UserSessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);

        if (session == null)
            return;

        session.IsSuspicious = true;
        session.SuspiciousActivityCount++;
        session.LastSuspiciousActivity = DateTime.UtcNow;
        session.SuspiciousReason = reason;
        session.RiskLevel = SessionRiskLevel.High;

        context.SecurityEvents.Add(new SecurityEvent
        {
            SessionId = session.Id,
            UserId = session.UserId,
            EventType = "SUSPICIOUS_ACTIVITY",
            Severity = SecurityEventSeverity.High,
            Description = reason,
            IpAddress = session.IpAddress
        });

        await context.SaveChangesAsync(cancellationToken);

        LogSessionMarkedSuspicious(_logger, sessionId, reason);
    }

    public async Task<bool> CheckConcurrentLoginAsync(int userId, string newSessionId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var activeSessions = await context.UserSessions
            .Where(s => s.UserId == userId && s.IsActive && s.SessionId != newSessionId)
            .ToListAsync(cancellationToken);

        return activeSessions.Any();
    }

    public async Task TerminatePreviousSessionsAsync(int userId, string currentSessionId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var oldSessions = await context.UserSessions
            .Where(s => s.UserId == userId && s.IsActive && s.SessionId != currentSessionId)
            .ToListAsync(cancellationToken);

        foreach (var session in oldSessions)
        {
            await EndSessionAsync(session.SessionId, SessionEndReason.ConcurrentLogin,
                "Terminated due to new login");
        }
    }

    public async Task<VpnDetectionResult> DetectVpnAsync(string ipAddress, CancellationToken cancellationToken = default)
    {
        var result = new VpnDetectionResult { IpAddress = ipAddress };

        try
        {
            // Configuration-based VPN detection (no external APIs)
            result = DetectVpnFromConfig(ipAddress);
        }
        catch (Exception ex)
        {
            LogVpnDetectionError(_logger, ex, ipAddress);
        }

        return result;
    }

    /// <summary>
    /// VPN detection based on appsettings.json configuration.
    /// </summary>
    private VpnDetectionResult DetectVpnFromConfig(string ipAddress)
    {
        var result = new VpnDetectionResult { IpAddress = ipAddress };

        // Localhost
        if (ipAddress == "127.0.0.1" || ipAddress == "::1" || ipAddress == "localhost")
        {
            result.Country = "Localhost";
            return result;
        }

        if (IpPatternMatcher.IsPrivateIP(ipAddress))
        {
            result.Country = "Local Network";

            // Check VPN subnets (only private IPs)
            if (IpPatternMatcher.MatchesAny(ipAddress, _vpnConfig.VpnSubnets))
            {
                result.IsVpn = true;
                result.ConfidenceScore = _vpnConfig.SubnetMatchConfidence;
                result.RiskFactors.Add("IP matches configured VPN subnet");
                LogVpnDetectedSubnet(_logger, ipAddress);
            }
        }
        else
        {
            // Public IP - no VPN detection possible without external API
            result.Country = "Unknown";
            result.RiskFactors.Add("Public IP - no VPN detection available");
        }

        return result;
    }

    public async Task<SessionStatistics> GetSessionStatisticsAsync(
        int? warehouseId = null, DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.UserSessions.AsQueryable();

        if (warehouseId.HasValue)
            query = query.Where(s => s.WarehouseId == warehouseId.Value);

        if (from.HasValue)
            query = query.Where(s => s.StartTime >= from.Value);

        if (to.HasValue)
            query = query.Where(s => s.StartTime <= to.Value);

        var sessions = await query.ToListAsync(cancellationToken);
        var activeSessions = sessions.Where(s => s.IsActive).ToList();

        return new SessionStatistics
        {
            TotalSessions = sessions.Count,
            ActiveSessions = activeSessions.Count,
            SuspiciousSessions = sessions.Count(s => s.IsSuspicious),
            VpnSessions = sessions.Count(s => s.IsVpn),
            ConcurrentSessions = sessions.Count(s => s.IsConcurrent),
            ForcedLogouts = sessions.Count(s => s.WasForcedLogout),
            // Check if sessions with EndTime exist before calling Average
            AverageSessionDuration = sessions.Any(s => s.EndTime.HasValue)
                ? TimeSpan.FromMinutes(sessions.Where(s => s.EndTime.HasValue)
                    .Average(s => (s.EndTime!.Value - s.StartTime).TotalMinutes))
                : TimeSpan.Zero,
            TotalPageViews = sessions.Sum(s => s.PageViewsCount),
            TotalApiRequests = sessions.Sum(s => s.ApiRequestsCount),
            TopCountries = sessions
                .GroupBy(s => s.Country)
                .Select(g => new KeyValuePair<string, int>(g.Key ?? "Unknown", g.Count()))
                .OrderByDescending(kvp => kvp.Value)
                .Take(10)
                .ToList(),
            DeviceTypes = sessions
                .GroupBy(s => s.DeviceType)
                .ToDictionary(g => g.Key ?? "Unknown", g => g.Count()),
            RiskLevelDistribution = sessions
                .GroupBy(s => s.RiskLevel)
                .ToDictionary(g => g.Key.ToString(), g => g.Count())
        };
    }

    public async Task<List<Domain.Models.UserSession>> GetSuspiciousSessionsAsync(int? warehouseId = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.UserSessions
            .AsNoTracking()
            .Include(s => s.User)
            .Include(s => s.Warehouse)
            .Where(s => s.IsSuspicious);

        if (warehouseId.HasValue)
            query = query.Where(s => s.WarehouseId == warehouseId.Value);

        // Explicit projection without u.CreatedAt
        var sessions = await query
            .Select(s => new Domain.Models.UserSession
            {
                Id = s.Id,
                SessionId = s.SessionId,
                UserId = s.UserId,
                Username = s.Username,
                WarehouseId = s.WarehouseId,
                StartTime = s.StartTime,
                LastActivity = s.LastActivity,
                EndTime = s.EndTime,
                IsActive = s.IsActive,
                IpAddress = s.IpAddress,
                UserAgent = s.UserAgent,
                DeviceType = s.DeviceType,
                Browser = s.Browser,
                OperatingSystem = s.OperatingSystem,
                Country = s.Country,
                City = s.City,
                Region = s.Region,
                Latitude = s.Latitude,
                Longitude = s.Longitude,
                IsVpn = s.IsVpn,
                IsProxy = s.IsProxy,
                IsTor = s.IsTor,
                VpnProvider = s.VpnProvider,
                VpnConfidenceScore = s.VpnConfidenceScore,
                HostingProvider = s.HostingProvider,
                IsSuspicious = s.IsSuspicious,
                SuspiciousActivityCount = s.SuspiciousActivityCount,
                LastSuspiciousActivity = s.LastSuspiciousActivity,
                SuspiciousReason = s.SuspiciousReason,
                IsConcurrent = s.IsConcurrent,
                ConcurrentSessionCount = s.ConcurrentSessionCount,
                RiskLevel = s.RiskLevel,
                RiskFactors = s.RiskFactors,
                PageViewsCount = s.PageViewsCount,
                ApiRequestsCount = s.ApiRequestsCount,
                LastPageUrl = s.LastPageUrl,
                WasForcedLogout = s.WasForcedLogout,
                TerminatedByUserId = s.TerminatedByUserId,
                EndReason = s.EndReason,
                EndReasonDetails = s.EndReasonDetails,
                DeviceFingerprint = s.DeviceFingerprint,
                DeviceInfo = s.DeviceInfo,
                // Navigation properties - load minimal
                User = s.User != null ? new User
                {
                    Id = s.User.Id,
                    Username = s.User.Username,
                    Email = s.User.Email,
                    DisplayName = s.User.DisplayName,
                    Role = s.User.Role
                } : null,
                Warehouse = s.Warehouse != null ? new Warehouse
                {
                    Id = s.Warehouse.Id,
                    Name = s.Warehouse.Name
                } : null
            })
            .OrderByDescending(s => s.LastSuspiciousActivity)
            .ToListAsync(cancellationToken);

        return sessions;
    }

    public async Task<List<Domain.Models.SecurityEvent>> GetSecurityEventsAsync(int? warehouseId = null, int count = 100, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.SecurityEvents
            .Include(e => e.User)
            .Include(e => e.Session)
            .AsQueryable();

        if (warehouseId.HasValue)
        {
            query = query.Where(e => e.Session.WarehouseId == warehouseId.Value);
        }

        return await query
            .OrderByDescending(e => e.Timestamp)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task CleanupExpiredSessionsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Sessions inactive for > 30 minutes
        var expiredTime = DateTime.UtcNow.AddMinutes(-30);

        // Load only IDs, then update individually (avoids CreatedAt issue)
        var expiredSessionIds = await context.UserSessions
            .Where(s => s.IsActive && s.LastActivity < expiredTime)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        if (!expiredSessionIds.Any())
        {
            LogNoExpiredSessions(_logger);
            return;
        }

        foreach (var sessionId in expiredSessionIds)
        {
            var session = await context.UserSessions.FindAsync(sessionId);
            if (session != null)
            {
                session.IsActive = false;
                session.EndTime = DateTime.UtcNow;
                session.EndReason = SessionEndReason.Timeout;
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        LogCleanedUpExpiredSessions(_logger, expiredSessionIds.Count);
    }

    /// <summary>
    /// Creates or retrieves an API session for an API key.
    /// API sessions are specially marked and have longer timeouts.
    /// API sessions are NOT stored in CircuitUserStore (browser only).
    /// </summary>
    public async Task<Domain.Models.UserSession?> GetOrCreateApiSessionAsync(
        int userId, int warehouseId, string ipAddress, string apiKeyName, string? requestPath = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Search for an existing active API session for this user + API key
        var existingSession = await context.UserSessions
            .FirstOrDefaultAsync(s =>
                s.UserId == userId &&
                s.IsActive &&
                s.DeviceType == "API" &&
                s.Browser == apiKeyName, cancellationToken);

        if (existingSession != null)
        {
            existingSession.LastActivity = DateTime.UtcNow;
            existingSession.IpAddress = ipAddress;
            existingSession.ApiRequestsCount++;

            if (!string.IsNullOrEmpty(requestPath))
            {
                existingSession.LastPageUrl = requestPath;
            }

            context.Entry(existingSession).State = EntityState.Modified;
            await context.SaveChangesAsync(cancellationToken);

            LogApiSessionUpdated(_logger, existingSession.SessionId, apiKeyName, requestPath ?? "N/A", existingSession.ApiRequestsCount);

            return existingSession;
        }

        // Create new API session
        var user = await context.Users.FindAsync(userId);
        if (user == null)
        {
            LogApiSessionUserMissing(_logger, userId);
            return null;
        }

        var vpnResult = await DetectVpnAsync(ipAddress);

        var apiSession = new Domain.Models.UserSession
        {
            SessionId = $"api-{Guid.NewGuid():N}",
            UserId = userId,
            Username = user.Username,
            WarehouseId = warehouseId,
            StartTime = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow,
            IsActive = true,

            // API-specific marking
            DeviceType = "API",
            Browser = apiKeyName,
            OperatingSystem = "API Client",
            UserAgent = $"API-Key: {apiKeyName}",
            DeviceFingerprint = $"api-key-{apiKeyName}-{userId}",
            DeviceInfo = $"API Key: {apiKeyName}",

            // IP and geo
            IpAddress = ipAddress,
            Country = vpnResult.Country,
            City = vpnResult.City,
            Region = vpnResult.Region,

            // VPN detection
            IsVpn = vpnResult.IsVpn,
            VpnConfidenceScore = vpnResult.ConfidenceScore,

            // API sessions have lower risk (authenticated)
            RiskLevel = SessionRiskLevel.Low,
            RiskFactors = "[]",

            // Counter - first request
            ApiRequestsCount = 1,
            PageViewsCount = 0,

            // Store initial API route
            LastPageUrl = requestPath ?? "/api"
        };

        context.UserSessions.Add(apiSession);
        await context.SaveChangesAsync(cancellationToken);

        LogApiSessionCreated(_logger, apiSession.SessionId, userId, apiKeyName, ipAddress, requestPath ?? "N/A");

        return apiSession;
    }

    /// <summary>
    /// Increments the API request counter for an API session.
    /// This method is called separately from browser updates.
    /// </summary>
    public async Task IncrementApiRequestCountAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sessionId) || !sessionId.StartsWith("api-"))
        {
            LogApiIncrementSkipped(_logger, sessionId);
            return;
        }

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var session = await context.UserSessions
                .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.IsActive, cancellationToken);

            if (session != null)
            {
                session.ApiRequestsCount++;
                session.LastActivity = DateTime.UtcNow;
                await context.SaveChangesAsync(cancellationToken);

                LogApiRequestIncremented(_logger, sessionId, session.ApiRequestsCount);
            }
        }
        catch (Exception ex)
        {
            LogApiIncrementFailed(_logger, ex, sessionId);
        }
    }

    private string GetDeviceType(string userAgent)
    {
        var ua = userAgent.ToLower();
        if (ua.Contains("mobile") || ua.Contains("android") || ua.Contains("iphone"))
            return "Mobile";
        if (ua.Contains("tablet") || ua.Contains("ipad"))
            return "Tablet";
        return "Desktop";
    }

    private string GetBrowser(string userAgent)
    {
        var ua = userAgent.ToLower();
        if (ua.Contains("edg")) return "Edge";
        if (ua.Contains("chrome")) return "Chrome";
        if (ua.Contains("firefox")) return "Firefox";
        if (ua.Contains("safari")) return "Safari";
        if (ua.Contains("opera")) return "Opera";
        return "Unknown";
    }

    private string GetOperatingSystem(string userAgent)
    {
        var ua = userAgent.ToLower();
        if (ua.Contains("windows")) return "Windows";
        if (ua.Contains("mac")) return "macOS";
        if (ua.Contains("linux")) return "Linux";
        if (ua.Contains("android")) return "Android";
        if (ua.Contains("ios") || ua.Contains("iphone") || ua.Contains("ipad")) return "iOS";
        return "Unknown";
    }

    private SessionRiskLevel CalculateRiskLevel(VpnDetectionResult vpnResult, string ipAddress)
    {
        // Score-based differentiated risk assessment
        var riskScore = 0;

        // 1. VPN usage (not automatically High)
        if (vpnResult.IsVpn)
        {
            riskScore += 20;
            LogRiskVpn(_logger, ipAddress);

            // Tor = high anonymity risk
            if (vpnResult.IsTor)
            {
                riskScore += 50;
                LogRiskTor(_logger, ipAddress);
            }

            // Proxy = medium risk
            if (vpnResult.IsProxy)
            {
                riskScore += 25;
                LogRiskProxy(_logger, ipAddress);
            }
        }

        // 2. Confidence score (how certain is the detection?)
        if (vpnResult.ConfidenceScore >= 90)
        {
            riskScore += 15;
        }
        else if (vpnResult.ConfidenceScore >= 70)
        {
            riskScore += 10;
        }
        else if (vpnResult.ConfidenceScore >= 50)
        {
            riskScore += 5;
        }

        // 3. Hosting provider (could be bot/script/scraper)
        if (!string.IsNullOrEmpty(vpnResult.HostingProvider))
        {
            riskScore += 30;
            LogRiskHostingProvider(_logger, vpnResult.HostingProvider);
        }

        // 4. Unknown country for public IP (suspicious)
        if (vpnResult.Country == "Unknown" &&
            ipAddress != "Unknown" &&
            !IpPatternMatcher.IsPrivateIP(ipAddress))
        {
            riskScore += 15;
            LogRiskUnknownCountry(_logger, ipAddress);
        }

        // 5. Local network (lowest risk)
        if (vpnResult.Country == "Local Network" || IpPatternMatcher.IsPrivateIP(ipAddress))
        {
            riskScore -= 10;
            LogRiskLocalNetwork(_logger, ipAddress);
        }

        // Final assessment based on total score
        SessionRiskLevel level;

        if (riskScore >= 80)
        {
            level = SessionRiskLevel.High;
            LogRiskHigh(_logger, riskScore, ipAddress);
        }
        else if (riskScore >= 50)
        {
            level = SessionRiskLevel.Medium;
            LogRiskMedium(_logger, riskScore, ipAddress);
        }
        else if (riskScore >= 20)
        {
            level = SessionRiskLevel.Low;
            LogRiskLow(_logger, riskScore, ipAddress);
        }
        else
        {
            level = SessionRiskLevel.Low;
            LogRiskMinimal(_logger, riskScore, ipAddress);
        }

        return level;
    }
}

public sealed class VpnDetectionResult
{
    public string IpAddress { get; set; } = string.Empty;
    public bool IsVpn { get; set; }
    public bool IsProxy { get; set; }
    public bool IsTor { get; set; }
    public string? VpnProvider { get; set; }
    public string? HostingProvider { get; set; }
    public int ConfidenceScore { get; set; }
    public string? Country { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public List<string> RiskFactors { get; set; } = new();
}

public sealed class SessionStatistics
{
    public int TotalSessions { get; set; }
    public int ActiveSessions { get; set; }
    public int SuspiciousSessions { get; set; }
    public int VpnSessions { get; set; }
    public int ConcurrentSessions { get; set; }
    public int ForcedLogouts { get; set; }
    public TimeSpan AverageSessionDuration { get; set; }
    public int TotalPageViews { get; set; }
    public int TotalApiRequests { get; set; }
    public List<KeyValuePair<string, int>> TopCountries { get; set; } = new();
    public Dictionary<string, int> DeviceTypes { get; set; } = new();
    public Dictionary<string, int> RiskLevelDistribution { get; set; } = new();
}
