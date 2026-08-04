using LagersystemLVHome.Domain.Models;
using Microsoft.Extensions.Logging;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Stufe 8/10 — LoggerMessage source generator template + AuthService catalog.
///
/// Pattern:
///   [LoggerMessage(EventId = N, Level = LogLevel.X, Message = "... {Param}")]
///   private static partial void LogXxx(ILogger logger, T param);
///
/// At call sites the generated method replaces `_logger.LogX("... {Param}", value)`
/// with `LogXxx(_logger, value)` — zero allocations, no boxing, compile-time
/// validation of the message template against the parameter list.
///
/// EventId allocation (per *.Log.cs catalog file):
///   AuthService                : 1000–1099
///   SessionManagementService   : 1100–1299
///   BackupManagementService    : 3000–3099
///   NotificationService        : 4000–4099
///   (extend as further services are migrated)
///
/// CA1848 status: removed from Directory.Build.props NoWarn baseline as of
/// Stufe 10. Now flows as a non-blocking warning via WarningsNotAsErrors so
/// the remaining ~500 unmigrated call sites stay visible without failing the
/// build. Once all services have a *.Log.cs partial, drop CA1848 from
/// WarningsNotAsErrors as well.
/// </summary>
public sealed partial class AuthService
{
    // --- Login / IP access (1000-1019) ---

    [LoggerMessage(EventId = 1000, Level = LogLevel.Warning,
        Message = "Login denied - IP {IP} not allowed for user {UserId}: {Reason}")]
    private static partial void LogLoginIpDenied(ILogger logger, string? ip, int userId, string? reason);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "IP access check passed for user {UserId}: IP={IP}, Rule={Rule}")]
    private static partial void LogIpAccessCheckPassed(ILogger logger, int userId, string? ip, string? rule);

    // --- Session lifecycle (1020-1039) ---

    [LoggerMessage(EventId = 1020, Level = LogLevel.Information,
        Message = "Thumbmarkjs fingerprint from cookie saved for session {SessionId}")]
    private static partial void LogFingerprintSavedFromCookie(ILogger logger, string? sessionId);

    [LoggerMessage(EventId = 1021, Level = LogLevel.Information,
        Message = "No DeviceFingerprint cookie yet for session {SessionId} - ThumbmarkFingerprintCapture will set it after render")]
    private static partial void LogNoFingerprintCookieYet(ILogger logger, string? sessionId);

    [LoggerMessage(EventId = 1022, Level = LogLevel.Error,
        Message = "Error generating/saving device fingerprint for user {UserId}")]
    private static partial void LogFingerprintError(ILogger logger, Exception ex, int userId);

    [LoggerMessage(EventId = 1023, Level = LogLevel.Information,
        Message = "New session created for user {UserId}: SessionId={SessionId}, Device={DeviceType}")]
    private static partial void LogSessionCreated(ILogger logger, int userId, string? sessionId, string? deviceType);

    [LoggerMessage(EventId = 1024, Level = LogLevel.Error,
        Message = "Error creating session for user {UserId}")]
    private static partial void LogSessionCreateError(ILogger logger, Exception ex, int userId);

    // --- Session monitor (1040-1059) ---

    [LoggerMessage(EventId = 1040, Level = LogLevel.Information,
        Message = "Session monitor started for user {UserId}, session {SessionId}, circuit {CircuitId}")]
    private static partial void LogSessionMonitorStarted(ILogger logger, int userId, string? sessionId, string? circuitId);

    [LoggerMessage(EventId = 1041, Level = LogLevel.Warning,
        Message = "Session monitor started WITHOUT circuit ID for user {UserId}")]
    private static partial void LogSessionMonitorStartedWithoutCircuit(ILogger logger, int userId);

    [LoggerMessage(EventId = 1042, Level = LogLevel.Error,
        Message = "Error starting session monitor for user {UserId}")]
    private static partial void LogSessionMonitorStartError(ILogger logger, Exception ex, int userId);

    // --- Session restoration (1043-1058) ---

    [LoggerMessage(EventId = 1043, Level = LogLevel.Debug,
        Message = "Session already exists in CircuitUserStore, skipping restoration")]
    private static partial void LogSessionExistsInStore(ILogger logger);

    [LoggerMessage(EventId = 1044, Level = LogLevel.Debug,
        Message = "SessionId already exists in CircuitUserStore: {SessionId}, skipping restoration")]
    private static partial void LogSessionIdExistsInStore(ILogger logger, string? sessionId);

    [LoggerMessage(EventId = 1045, Level = LogLevel.Debug,
        Message = "No session cookie found")]
    private static partial void LogNoSessionCookie(ILogger logger);

    [LoggerMessage(EventId = 1046, Level = LogLevel.Warning,
        Message = "Attempted to restore API session {SessionId} in browser - DENIED")]
    private static partial void LogApiSessionRestoreDeniedInBrowser(ILogger logger, string? sessionId);

    [LoggerMessage(EventId = 1047, Level = LogLevel.Warning,
        Message = "SessionManagementService not available for session restoration")]
    private static partial void LogSessionMgmtUnavailable(ILogger logger);

    [LoggerMessage(EventId = 1048, Level = LogLevel.Warning,
        Message = "Session cookie found but session is invalid or not active: {SessionId}")]
    private static partial void LogSessionInvalidOrInactive(ILogger logger, string? sessionId);

    [LoggerMessage(EventId = 1049, Level = LogLevel.Warning,
        Message = "Attempted to restore API session {SessionId} (DeviceType=API) in browser - DENIED")]
    private static partial void LogApiDeviceTypeRestoreDenied(ILogger logger, string? sessionId);

    [LoggerMessage(EventId = 1050, Level = LogLevel.Warning,
        Message = "Device type mismatch! Session DeviceType={SessionDevice}, Current={CurrentDevice}. NOT restoring session to prevent cross-device conflicts. SessionId={SessionId}")]
    private static partial void LogDeviceTypeMismatch(ILogger logger, string? sessionDevice, string? currentDevice, string? sessionId);

    [LoggerMessage(EventId = 1051, Level = LogLevel.Warning,
        Message = "Session expired due to inactivity: {SessionId}, LastActivity: {LastActivity}")]
    private static partial void LogSessionExpiredInactivity(ILogger logger, string? sessionId, DateTime lastActivity);

    [LoggerMessage(EventId = 1052, Level = LogLevel.Warning,
        Message = "Session found but user is no longer active: UserId={UserId}")]
    private static partial void LogSessionUserNoLongerActive(ILogger logger, int userId);

    [LoggerMessage(EventId = 1053, Level = LogLevel.Information,
        Message = "Session restored from cookie for user {Username} (UserId: {UserId}, SessionId: {SessionId}, CircuitId: {CircuitId}, Device: {Device})")]
    private static partial void LogSessionRestoredFromCookie(ILogger logger, string? username, int userId, string? sessionId, string? circuitId, string? device);

    [LoggerMessage(EventId = 1054, Level = LogLevel.Error,
        Message = "Error restoring session from cookie")]
    private static partial void LogSessionRestoreError(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 1055, Level = LogLevel.Information,
        Message = "Generated new circuit ID for restoration (no HttpContext): {CircuitId}")]
    private static partial void LogCircuitGeneratedNoHttpContext(ILogger logger, string? circuitId);

    [LoggerMessage(EventId = 1056, Level = LogLevel.Debug,
        Message = "Using existing circuit ID from HttpContext: {CircuitId}")]
    private static partial void LogCircuitFromHttpContext(ILogger logger, string? circuitId);

    [LoggerMessage(EventId = 1057, Level = LogLevel.Information,
        Message = "Generated new circuit ID for restoration: {CircuitId}")]
    private static partial void LogCircuitGeneratedForRestoration(ILogger logger, string? circuitId);

    [LoggerMessage(EventId = 1058, Level = LogLevel.Error,
        Message = "Error getting current session ID")]
    private static partial void LogGetCurrentSessionIdError(ILogger logger, Exception ex);

    // --- Logout (1059-1061) ---

    [LoggerMessage(EventId = 1059, Level = LogLevel.Information,
        Message = "Session {SessionId} ended for user {UserId}")]
    private static partial void LogLogoutSessionEnded(ILogger logger, string? sessionId, int userId);

    [LoggerMessage(EventId = 1060, Level = LogLevel.Warning,
        Message = "No session ID found for logout, user {UserId} may have multiple active sessions")]
    private static partial void LogLogoutNoSessionId(ILogger logger, int userId);

    [LoggerMessage(EventId = 1061, Level = LogLevel.Error,
        Message = "Error ending session for user {UserId}")]
    private static partial void LogLogoutSessionEndError(ILogger logger, Exception ex, int userId);

    // --- 2FA (1062-1064) ---

    [LoggerMessage(EventId = 1062, Level = LogLevel.Warning,
        Message = "TwoFactorService not available")]
    private static partial void LogTwoFactorServiceUnavailable(ILogger logger);

    [LoggerMessage(EventId = 1063, Level = LogLevel.Warning,
        Message = "2FA locked for user {UserId}, {Minutes} minutes remaining")]
    private static partial void LogTwoFaLocked(ILogger logger, int userId, double minutes);

    [LoggerMessage(EventId = 1064, Level = LogLevel.Critical,
        Message = "2FA locked for user {UserId} after {Attempts} failed attempts")]
    private static partial void LogTwoFaLockedAfterAttempts(ILogger logger, int userId, int attempts);

    // --- Magic link login (1065-1070) ---

    [LoggerMessage(EventId = 1065, Level = LogLevel.Warning,
        Message = "PasswordlessLoginService not available")]
    private static partial void LogPasswordlessUnavailable(ILogger logger);

    [LoggerMessage(EventId = 1066, Level = LogLevel.Warning,
        Message = "Magic link login denied - missing GDPR consent for user {UserId}")]
    private static partial void LogMagicLinkGdprDenied(ILogger logger, int userId);

    [LoggerMessage(EventId = 1067, Level = LogLevel.Warning,
        Message = "Magic link login denied - missing granular consents for user {UserId}")]
    private static partial void LogMagicLinkConsentDenied(ILogger logger, int userId);

    [LoggerMessage(EventId = 1068, Level = LogLevel.Warning,
        Message = "Magic link login denied - IP {IP} not allowed for user {UserId}: {Reason}")]
    private static partial void LogMagicLinkIpDenied(ILogger logger, string? ip, int userId, string? reason);

    [LoggerMessage(EventId = 1069, Level = LogLevel.Information,
        Message = "Magic link login successful for user {UserId}, Session: {SessionId}")]
    private static partial void LogMagicLinkSuccess(ILogger logger, int userId, string? sessionId);

    [LoggerMessage(EventId = 1070, Level = LogLevel.Error,
        Message = "Error creating session for magic link login")]
    private static partial void LogMagicLinkSessionError(ILogger logger, Exception ex);

    // --- Passkey login (1071-1082) ---

    [LoggerMessage(EventId = 1071, Level = LogLevel.Warning,
        Message = "Passkey login failed - user not found: {UserId}")]
    private static partial void LogPasskeyUserNotFound(ILogger logger, int userId);

    [LoggerMessage(EventId = 1072, Level = LogLevel.Warning,
        Message = "Passkey login denied - user inactive: {UserId}")]
    private static partial void LogPasskeyInactive(ILogger logger, int userId);

    [LoggerMessage(EventId = 1073, Level = LogLevel.Warning,
        Message = "Passkey login denied - user not approved: {UserId}, Status={Status}")]
    private static partial void LogPasskeyNotApproved(ILogger logger, int userId, UserApprovalStatus status);

    [LoggerMessage(EventId = 1074, Level = LogLevel.Warning,
        Message = "Passkey login denied - missing GDPR consent: {UserId}")]
    private static partial void LogPasskeyGdprDenied(ILogger logger, int userId);

    [LoggerMessage(EventId = 1075, Level = LogLevel.Warning,
        Message = "Passkey login denied - missing granular consents: {UserId}")]
    private static partial void LogPasskeyConsentDenied(ILogger logger, int userId);

    [LoggerMessage(EventId = 1076, Level = LogLevel.Warning,
        Message = "Passkey login denied - IP {IP} not allowed for user {UserId}")]
    private static partial void LogPasskeyIpDenied(ILogger logger, string? ip, int userId);

    [LoggerMessage(EventId = 1077, Level = LogLevel.Information,
        Message = "Passkey login successful for user {UserId}, Session: {SessionId}")]
    private static partial void LogPasskeyLoginSuccess(ILogger logger, int userId, string? sessionId);

    [LoggerMessage(EventId = 1078, Level = LogLevel.Information,
        Message = "Thumbmarkjs fingerprint from cookie saved for passkey session {SessionId}")]
    private static partial void LogPasskeyFingerprintSavedFromCookie(ILogger logger, string? sessionId);

    [LoggerMessage(EventId = 1079, Level = LogLevel.Information,
        Message = "No DeviceFingerprint cookie for passkey session {SessionId} - ThumbmarkFingerprintCapture will set it")]
    private static partial void LogPasskeyNoFingerprintCookie(ILogger logger, string? sessionId);

    [LoggerMessage(EventId = 1080, Level = LogLevel.Error,
        Message = "Error saving device fingerprint for passkey login")]
    private static partial void LogPasskeyFingerprintError(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 1081, Level = LogLevel.Error,
        Message = "Error starting session monitor for passkey login")]
    private static partial void LogPasskeySessionMonitorError(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 1082, Level = LogLevel.Error,
        Message = "Error creating session for passkey login")]
    private static partial void LogPasskeySessionCreateError(ILogger logger, Exception ex);

    // --- Email OTP (1083) ---

    [LoggerMessage(EventId = 1083, Level = LogLevel.Warning,
        Message = "EmailOtpService not available")]
    private static partial void LogEmailOtpServiceUnavailable(ILogger logger);
}
