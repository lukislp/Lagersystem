using Microsoft.Extensions.Logging;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Stufe 9 — LoggerMessage source generator catalog for SessionManagementService.
/// EventId range 1100–1199 (per the AuthService.Log.cs roadmap).
/// </summary>
public sealed partial class SessionManagementService
{
    // --- Session lifecycle (1100-1119) ---

    [LoggerMessage(EventId = 1100, Level = LogLevel.Information,
        Message = "Session created for user {UserId} from {IpAddress} (VPN: {IsVpn})")]
    private static partial void LogSessionCreated(ILogger logger, int userId, string? ipAddress, bool isVpn);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Information,
        Message = "Session {SessionId} ended. Reason: {Reason}")]
    private static partial void LogSessionEnded(ILogger logger, string? sessionId, SessionEndReason reason);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Warning,
        Message = "Session {SessionId} marked as suspicious: {Reason}")]
    private static partial void LogSessionMarkedSuspicious(ILogger logger, string? sessionId, string? reason);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Warning,
        Message = "UpdateSessionFingerprintAsync: session {SessionId} not found or inactive")]
    private static partial void LogFingerprintUpdateSessionMissing(ILogger logger, string? sessionId);

    // --- Session lookup (1120-1139) ---

    [LoggerMessage(EventId = 1120, Level = LogLevel.Debug,
        Message = "Found session {SessionId} for user {UserId} by exact fingerprint match")]
    private static partial void LogSessionFoundByFingerprint(ILogger logger, string? sessionId, int userId);

    [LoggerMessage(EventId = 1121, Level = LogLevel.Debug,
        Message = "No session found for user {UserId} with fingerprint {Fingerprint}")]
    private static partial void LogSessionNotFoundByFingerprint(ILogger logger, int userId, string? fingerprint);

    [LoggerMessage(EventId = 1122, Level = LogLevel.Debug,
        Message = "Found session {SessionId} for user {UserId} by user-agent match")]
    private static partial void LogSessionFoundByUserAgent(ILogger logger, string? sessionId, int userId);

    [LoggerMessage(EventId = 1123, Level = LogLevel.Warning,
        Message = "Using fallback session {SessionId} for user {UserId} (no fingerprint or UA match). Session Fingerprint: {SessionFP}, Session UA: {SessionUA}")]
    private static partial void LogFallbackSession(ILogger logger, string? sessionId, int userId, string? sessionFP, string? sessionUA);

    // --- Session update (1140-1159) ---

    [LoggerMessage(EventId = 1140, Level = LogLevel.Debug,
        Message = "Updating session {SessionId} IP: {OldIp} -> {NewIp}")]
    private static partial void LogSessionIpUpdate(ILogger logger, string? sessionId, string? oldIp, string? newIp);

    [LoggerMessage(EventId = 1141, Level = LogLevel.Information,
        Message = "Updating GeoLocation for session {SessionId}: {OldCountry} -> {NewCountry}")]
    private static partial void LogSessionGeoUpdate(ILogger logger, string? sessionId, string? oldCountry, string? newCountry);

    [LoggerMessage(EventId = 1142, Level = LogLevel.Warning,
        Message = "Failed to update GeoLocation for session {SessionId}")]
    private static partial void LogSessionGeoUpdateFailed(ILogger logger, Exception ex, string? sessionId);

    [LoggerMessage(EventId = 1143, Level = LogLevel.Debug,
        Message = "Updating session {SessionId} User-Agent")]
    private static partial void LogSessionUserAgentUpdate(ILogger logger, string? sessionId);

    [LoggerMessage(EventId = 1144, Level = LogLevel.Debug,
        Message = "Skipping impossible travel detection: Both countries are local ({OldCountry} -> {NewCountry})")]
    private static partial void LogSkipImpossibleTravelLocal(ILogger logger, string? oldCountry, string? newCountry);

    // --- Cleanup (1160-1169) ---

    [LoggerMessage(EventId = 1160, Level = LogLevel.Information,
        Message = "No expired sessions to cleanup")]
    private static partial void LogNoExpiredSessions(ILogger logger);

    [LoggerMessage(EventId = 1161, Level = LogLevel.Information,
        Message = "Cleaned up {Count} expired sessions")]
    private static partial void LogCleanedUpExpiredSessions(ILogger logger, int count);

    // --- API sessions (1170-1189) ---

    [LoggerMessage(EventId = 1170, Level = LogLevel.Information,
        Message = "API session {SessionId} updated for key '{ApiKeyName}'. Request: {RequestPath}, Total: {Count}")]
    private static partial void LogApiSessionUpdated(ILogger logger, string? sessionId, string? apiKeyName, string? requestPath, int count);

    [LoggerMessage(EventId = 1171, Level = LogLevel.Warning,
        Message = "Cannot create API session - user {UserId} not found")]
    private static partial void LogApiSessionUserMissing(ILogger logger, int userId);

    [LoggerMessage(EventId = 1172, Level = LogLevel.Information,
        Message = "Created new API session {SessionId} for user {UserId}, key '{ApiKeyName}' from {IpAddress}. Initial request: {RequestPath}")]
    private static partial void LogApiSessionCreated(ILogger logger, string? sessionId, int userId, string? apiKeyName, string? ipAddress, string? requestPath);

    [LoggerMessage(EventId = 1173, Level = LogLevel.Debug,
        Message = "IncrementApiRequestCountAsync skipped - not an API session: {SessionId}")]
    private static partial void LogApiIncrementSkipped(ILogger logger, string? sessionId);

    [LoggerMessage(EventId = 1174, Level = LogLevel.Debug,
        Message = "API session {SessionId} request count incremented to {Count}")]
    private static partial void LogApiRequestIncremented(ILogger logger, string? sessionId, int count);

    [LoggerMessage(EventId = 1175, Level = LogLevel.Warning,
        Message = "Failed to increment API request count for session {SessionId}")]
    private static partial void LogApiIncrementFailed(ILogger logger, Exception ex, string? sessionId);

    // --- VPN / risk detection (1190-1199) ---

    [LoggerMessage(EventId = 1190, Level = LogLevel.Error,
        Message = "Error detecting VPN for IP {IpAddress}")]
    private static partial void LogVpnDetectionError(ILogger logger, Exception ex, string? ipAddress);

    [LoggerMessage(EventId = 1191, Level = LogLevel.Information,
        Message = "VPN detected for IP {IpAddress} via subnet match")]
    private static partial void LogVpnDetectedSubnet(ILogger logger, string? ipAddress);

    [LoggerMessage(EventId = 1192, Level = LogLevel.Debug,
        Message = "VPN detected for IP {IpAddress}, risk score +20")]
    private static partial void LogRiskVpn(ILogger logger, string? ipAddress);

    [LoggerMessage(EventId = 1193, Level = LogLevel.Warning,
        Message = "Tor detected for IP {IpAddress}, risk score +50")]
    private static partial void LogRiskTor(ILogger logger, string? ipAddress);

    [LoggerMessage(EventId = 1194, Level = LogLevel.Information,
        Message = "Proxy detected for IP {IpAddress}, risk score +25")]
    private static partial void LogRiskProxy(ILogger logger, string? ipAddress);

    [LoggerMessage(EventId = 1195, Level = LogLevel.Information,
        Message = "Hosting provider detected: {Provider}, risk score +30")]
    private static partial void LogRiskHostingProvider(ILogger logger, string? provider);

    [LoggerMessage(EventId = 1196, Level = LogLevel.Debug,
        Message = "Unknown country for public IP {IpAddress}, risk score +15")]
    private static partial void LogRiskUnknownCountry(ILogger logger, string? ipAddress);

    [LoggerMessage(EventId = 1197, Level = LogLevel.Debug,
        Message = "Local network IP {IpAddress}, risk score -10")]
    private static partial void LogRiskLocalNetwork(ILogger logger, string? ipAddress);

    [LoggerMessage(EventId = 1198, Level = LogLevel.Warning,
        Message = "HIGH risk session detected: Score={RiskScore}, IP={IpAddress}")]
    private static partial void LogRiskHigh(ILogger logger, int riskScore, string? ipAddress);

    [LoggerMessage(EventId = 1199, Level = LogLevel.Information,
        Message = "MEDIUM risk session detected: Score={RiskScore}, IP={IpAddress}")]
    private static partial void LogRiskMedium(ILogger logger, int riskScore, string? ipAddress);

    [LoggerMessage(EventId = 1200, Level = LogLevel.Debug,
        Message = "LOW risk session detected: Score={RiskScore}, IP={IpAddress}")]
    private static partial void LogRiskLow(ILogger logger, int riskScore, string? ipAddress);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Debug,
        Message = "Minimal risk session: Score={RiskScore}, IP={IpAddress}")]
    private static partial void LogRiskMinimal(ILogger logger, int riskScore, string? ipAddress);
}
