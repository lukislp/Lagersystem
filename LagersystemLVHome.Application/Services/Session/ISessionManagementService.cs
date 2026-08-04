using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using LagersystemLVHome.Application.Configuration;
using LagersystemLVHome.Application.Utilities;
using Microsoft.Extensions.Options;

namespace LagersystemLVHome.Application.Services;

public interface ISessionManagementService
{
    // Session CRUD
    Task<Domain.Models.UserSession> CreateSessionAsync(int userId, int warehouseId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
    Task<Domain.Models.UserSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<List<Domain.Models.UserSession>> GetActiveSessionsAsync(int? warehouseId = null, CancellationToken cancellationToken = default);
    Task<List<Domain.Models.UserSession>> GetActiveSessionsAsync(int? warehouseId, bool onlyActive, CancellationToken cancellationToken = default);
    Task<List<Domain.Models.UserSession>> GetUserSessionsAsync(int userId, bool onlyActive = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a session by UserId AND DeviceFingerprint (for correct session assignment).
    /// </summary>
    Task<Domain.Models.UserSession?> GetSessionByUserAndFingerprintAsync(int userId, string? deviceFingerprint, bool onlyActive = true, CancellationToken cancellationToken = default);

    Task UpdateSessionActivityAsync(string sessionId, string? pageUrl = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the device fingerprint of the active session identified by
    /// <paramref name="sessionId"/>. Returns <c>true</c> if the session was
    /// found and updated.
    /// </summary>
    Task<bool> UpdateSessionFingerprintAsync(string sessionId, string fingerprint, CancellationToken cancellationToken = default);
    Task EndSessionAsync(string sessionId, SessionEndReason reason, string? details = null, int? terminatedByUserId = null, CancellationToken cancellationToken = default);

    // Remote logout
    Task ForceLogoutAsync(string sessionId, int adminUserId, string reason, CancellationToken cancellationToken = default);
    Task ForceLogoutUserAsync(int userId, int adminUserId, string reason, CancellationToken cancellationToken = default);

    // Session hijacking detection
    Task<bool> DetectSessionHijackingAsync(string sessionId, string currentIp, string currentUserAgent, CancellationToken cancellationToken = default);
    Task MarkSessionSuspiciousAsync(string sessionId, string reason, CancellationToken cancellationToken = default);

    // Concurrent login prevention
    Task<bool> CheckConcurrentLoginAsync(int userId, string newSessionId, CancellationToken cancellationToken = default);
    Task TerminatePreviousSessionsAsync(int userId, string currentSessionId, CancellationToken cancellationToken = default);

    // VPN detection
    Task<VpnDetectionResult> DetectVpnAsync(string ipAddress, CancellationToken cancellationToken = default);

    // API-key session management (with requestPath parameter)
    Task<Domain.Models.UserSession?> GetOrCreateApiSessionAsync(int userId, int warehouseId, string ipAddress, string apiKeyName, string? requestPath = null, CancellationToken cancellationToken = default);

    // API session activity update (separated from browser updates)
    Task IncrementApiRequestCountAsync(string sessionId, CancellationToken cancellationToken = default);

    // Statistics
    Task<SessionStatistics> GetSessionStatisticsAsync(int? warehouseId = null, DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default);
    Task<List<Domain.Models.UserSession>> GetSuspiciousSessionsAsync(int? warehouseId = null, CancellationToken cancellationToken = default);
    Task<List<Domain.Models.SecurityEvent>> GetSecurityEventsAsync(int? warehouseId = null, int count = 100, CancellationToken cancellationToken = default);

    // Cleanup
    Task CleanupExpiredSessionsAsync(CancellationToken cancellationToken = default);
}
