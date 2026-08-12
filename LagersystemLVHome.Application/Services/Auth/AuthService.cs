using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.Extensions.Logging;
using DbUserSession = LagersystemLVHome.Domain.Models.UserSession;
using CircuitUserSession = LagersystemLVHome.Application.Services.UserSession;

namespace LagersystemLVHome.Application.Services;

public sealed partial class AuthService : IAuthService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly CustomAuthStateProvider _authStateProvider;
    private readonly CircuitUserStore _userStore;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuthService> _logger;
    private readonly ISessionManagementService? _sessionManagementService;
    private readonly ITwoFactorService? _twoFactorService;
    private readonly IAuditService? _auditService;
    private readonly IEmailOtpService? _emailOtpService;
    private readonly IPasswordlessLoginService? _passwordlessLoginService;
    private readonly IUserIpAccessService? _ipAccessService;
    private readonly IDeviceFingerprintService? _deviceFingerprintService;
    private readonly ISessionMonitorService? _sessionMonitorService;

    public AuthService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        CustomAuthStateProvider authStateProvider,
        CircuitUserStore userStore,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuthService> logger,
        ISessionManagementService? sessionManagementService = null,
        ITwoFactorService? twoFactorService = null,
        IAuditService? auditService = null,
        IEmailOtpService? emailOtpService = null,
        IPasswordlessLoginService? passwordlessLoginService = null,
        IUserIpAccessService? ipAccessService = null,
        IDeviceFingerprintService? deviceFingerprintService = null,
        ISessionMonitorService? sessionMonitorService = null)
    {
        _contextFactory = contextFactory;
        _authStateProvider = authStateProvider;
        _userStore = userStore;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _sessionManagementService = sessionManagementService;
        _twoFactorService = twoFactorService;
        _auditService = auditService;
        _emailOtpService = emailOtpService;
        _passwordlessLoginService = passwordlessLoginService;
        _ipAccessService = ipAccessService;
        _deviceFingerprintService = deviceFingerprintService;
        _sessionMonitorService = sessionMonitorService;
    }

    public async Task<Result<User>> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context.Users
            .Include(u => u.Warehouse)
            .FirstOrDefaultAsync(u => u.Username == username, cancellationToken);

        if (user == null)
        {
            await _auditService.SafeLogAsync(_logger, "LOGIN_FAILED", "User", null, new { Username = username, Reason = "User not found" }, AuditSeverity.Warning);
            return Result<User>.Failure(LoginFailures.UserNotFound);
        }

        if (user.LockedUntil.HasValue && user.LockedUntil > DateTime.UtcNow)
        {
            var remainingMinutes = Math.Ceiling((user.LockedUntil.Value - DateTime.UtcNow).TotalMinutes);
            await _auditService.SafeLogAsync(_logger, "LOGIN_BLOCKED", "User", user.Id,
                new { Reason = "Account locked", RemainingMinutes = remainingMinutes },
                AuditSeverity.Warning);
            return Result<User>.Failure(LoginFailures.AccountLocked, remainingMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // Check IP restrictions before verifying password
        var clientIp = _httpContextAccessor.GetClientIp();
        if (!string.IsNullOrEmpty(clientIp) && _ipAccessService is not null)
        {
            var ipResult = await _ipAccessService.CheckAccessAsync(user.Id, clientIp);
            if (!ipResult.IsAllowed)
            {
                LogLoginIpDenied(_logger, clientIp, user.Id, ipResult.Message);
                await _auditService.SafeLogAsync(_logger, "LOGIN_IP_DENIED", "User", user.Id,
                    new { IpAddress = clientIp, Message = ipResult.Message, MatchedRule = ipResult.MatchedRule },
                    AuditSeverity.Warning);
                return Result<User>.Failure(LoginFailures.IpDenied, ipResult.MatchedRule);
            }

            if (ipResult.RestrictionsEnabled)
            {
                LogIpAccessCheckPassed(_logger, user.Id, clientIp, ipResult.MatchedRule);
            }
        }

        // Verify password
        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            user.FailedLoginAttempts++;
            user.LastFailedLoginAt = DateTime.UtcNow;

            // Lock after 5 failed attempts
            if (user.FailedLoginAttempts >= 5)
            {
                user.LockedUntil = DateTime.UtcNow.AddMinutes(15);
                await _auditService.SafeLogAsync(_logger, "ACCOUNT_LOCKED", "User", user.Id,
                    new { Reason = "Too many failed attempts", Attempts = user.FailedLoginAttempts },
                    AuditSeverity.Critical);
            }

            await context.SaveChangesAsync(cancellationToken);
            await _auditService.SafeLogAsync(_logger, "LOGIN_FAILED", "User", user.Id,
                new { Reason = "Invalid password", Attempts = user.FailedLoginAttempts },
                AuditSeverity.Warning);

            return Result<User>.Failure(LoginFailures.InvalidPassword);
        }

        // Check GDPR consent
        if (!user.GdprConsentGiven)
        {
            await _auditService.SafeLogAsync(_logger, "LOGIN_DENIED", "User", user.Id,
                new { Reason = "GDPR consent required" },
                AuditSeverity.Warning);
            return Result<User>.Failure(LoginFailures.GdprConsentRequired);
        }

        // Check mandatory granular consents (analytics & device fingerprinting)
        if (!user.AnalyticsConsent || !user.DeviceFingerprintConsent)
        {
            await _auditService.SafeLogAsync(_logger, "LOGIN_DENIED", "User", user.Id,
                new
                {
                    Reason = "Granular consents required",
                    AnalyticsConsent = user.AnalyticsConsent,
                    DeviceFingerprintConsent = user.DeviceFingerprintConsent
                },
                AuditSeverity.Warning);
            return Result<User>.Failure(LoginFailures.GranularConsentRequired);
        }

        if (!user.IsActive || user.IsDeleted)
        {
            await _auditService.SafeLogAsync(_logger, "LOGIN_DENIED", "User", user.Id,
                new { Reason = "Account inactive or deleted" },
                AuditSeverity.Warning);
            return Result<User>.Failure(LoginFailures.Inactive);
        }

        // Check approval status
        if (user.ApprovalStatus != UserApprovalStatus.Approved)
        {
            await _auditService.SafeLogAsync(_logger, "LOGIN_DENIED", "User", user.Id,
                new { Reason = "Not approved", Status = user.ApprovalStatus.ToString() },
                AuditSeverity.Warning);

            var failureCode = user.ApprovalStatus == UserApprovalStatus.Rejected
                ? LoginFailures.Rejected
                : LoginFailures.PendingApproval;
            return Result<User>.Failure(failureCode);
        }

        // Successful login
        user.LastLoginAt = DateTime.UtcNow;
        user.FailedLoginAttempts = 0;
        user.LastFailedLoginAt = null;
        user.LockedUntil = null;
        user.LastLoginIp = _httpContextAccessor.GetClientIp();

        await context.SaveChangesAsync(cancellationToken);
        await _auditService.SafeLogAsync(_logger, "LOGIN_SUCCESS", "User", user.Id,
            new { Username = user.Username, IP = user.LastLoginIp },
            AuditSeverity.Info);

        // Create session for the session management dashboard (optional)
        if (_sessionManagementService != null)
        {
            try
            {
                var ipAddress = _httpContextAccessor.GetClientIp() ?? "Unknown";
                var userAgent = _httpContextAccessor?.HttpContext?.Request.Headers["User-Agent"].ToString() ?? "Unknown";

                // Each device/browser gets its own session
                var session = await _sessionManagementService.CreateSessionAsync(
                    user.Id,
                    user.WarehouseId,
                    ipAddress,
                    userAgent
                );

                // Save device fingerprint from cookie
                if (_deviceFingerprintService is not null && _httpContextAccessor?.HttpContext is not null)
                {
                    try
                    {
                        var thumbmarkFp = _httpContextAccessor.HttpContext.Request.Cookies["DeviceFingerprint"];
                        if (!string.IsNullOrEmpty(thumbmarkFp))
                        {
                            await _deviceFingerprintService.SaveDeviceFingerprintAsync(
                                session.Id,
                                thumbmarkFp,
                                _httpContextAccessor.HttpContext
                            );
                            LogFingerprintSavedFromCookie(_logger, session.SessionId);
                        }
                        else
                        {
                            LogNoFingerprintCookieYet(_logger, session.SessionId);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogFingerprintError(_logger, ex, user.Id);
                    }
                }

                // Store session ID in CircuitUserStore (circuit-specific)
                _userStore.SetSessionId(session.SessionId);

                LogSessionCreated(_logger, user.Id, session.SessionId, session.DeviceType);

                // Start session monitor
                try
                {
                    if (_sessionMonitorService is not null)
                    {
                        var circuitId = GetCurrentCircuitId();
                        if (!string.IsNullOrEmpty(circuitId))
                        {
                            await _sessionMonitorService.StartMonitoringAsync(user.Id, session.SessionId, circuitId);
                            LogSessionMonitorStarted(_logger, user.Id, session.SessionId, circuitId);
                        }
                        else
                        {
                            await _sessionMonitorService.StartMonitoringAsync(user.Id, session.SessionId);
                            LogSessionMonitorStartedWithoutCircuit(_logger, user.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogSessionMonitorStartError(_logger, ex, user.Id);
                }
            }
            catch (Exception ex)
            {
                LogSessionCreateError(_logger, ex, user.Id);
            }
        }

        // MarkUserAsAuthenticated must happen last (after cookie/session setup)
        await _authStateProvider.MarkUserAsAuthenticated(user);

        return Result<User>.Success(user);
    }

    public async Task<User?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var user = _userStore.GetUser();
        if (user == null)
        {
            // Attempt to restore session from cookie (mobile fix)
            var restored = await TryRestoreSessionFromCookieAsync();
            if (restored)
            {
                user = _userStore.GetUser();
            }
        }

        if (user == null)
            return null;

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Users
            .Include(u => u.Warehouse)
            .FirstOrDefaultAsync(u => u.Id == user.UserId && u.IsActive && !u.IsDeleted, cancellationToken);
    }

    /// <summary>
    /// Restores the user session from a cookie with device matching.
    /// API sessions are not restored for browser clients.
    /// </summary>
    private async Task<bool> TryRestoreSessionFromCookieAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var existingUser = _userStore.GetUser();
            if (existingUser != null)
            {
                LogSessionExistsInStore(_logger);
                return true;
            }

            var existingSessionId = _userStore.GetSessionId();
            if (!string.IsNullOrEmpty(existingSessionId))
            {
                LogSessionIdExistsInStore(_logger, existingSessionId);
                return false;
            }

            var sessionCookie = _httpContextAccessor?.HttpContext?.Request.Cookies["LagerSystem.SessionId"];

            if (string.IsNullOrEmpty(sessionCookie))
            {
                LogNoSessionCookie(_logger);
                return false;
            }

            // API sessions (with "api-" prefix) must not be restored for browsers
            if (sessionCookie.StartsWith("api-", StringComparison.OrdinalIgnoreCase))
            {
                LogApiSessionRestoreDeniedInBrowser(_logger, sessionCookie);
                return false;
            }

            if (_sessionManagementService == null)
            {
                LogSessionMgmtUnavailable(_logger);
                return false;
            }

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var dbSession = await context.UserSessions
                .Include(s => s.User)
                    .ThenInclude(u => u.Warehouse)
                .FirstOrDefaultAsync(s => s.SessionId == sessionCookie && s.IsActive, cancellationToken);

            if (dbSession == null || dbSession.User == null)
            {
                LogSessionInvalidOrInactive(_logger, sessionCookie);
                return false;
            }

            // API sessions (DeviceType="API") must not be restored for browsers
            if (dbSession.DeviceType == "API")
            {
                LogApiDeviceTypeRestoreDenied(_logger, sessionCookie);
                return false;
            }

            // Verify device fingerprint for additional security
            var currentUserAgent = _httpContextAccessor?.HttpContext?.Request.Headers["User-Agent"].ToString() ?? "";
            var currentIp = _httpContextAccessor.GetClientIp() ?? "Unknown";

            // Simple device type check (mobile vs desktop)
            var currentIsMobile = IsMobileDevice(currentUserAgent);
            var sessionIsMobile = dbSession.DeviceType?.Contains("Mobile", StringComparison.OrdinalIgnoreCase) ?? false;

            if (currentIsMobile != sessionIsMobile)
            {
                LogDeviceTypeMismatch(_logger, dbSession.DeviceType, currentIsMobile ? "Mobile" : "Desktop", sessionCookie);
                return false;
            }

            // Check if session has expired (more than 7 days inactive)
            var sessionTimeout = TimeSpan.FromDays(7);
            if (DateTime.UtcNow - dbSession.LastActivity > sessionTimeout)
            {
                LogSessionExpiredInactivity(_logger, sessionCookie, dbSession.LastActivity);
                return false;
            }

            if (!dbSession.User.IsActive || dbSession.User.IsDeleted || dbSession.User.ApprovalStatus != UserApprovalStatus.Approved)
            {
                LogSessionUserNoLongerActive(_logger, dbSession.User.Id);
                return false;
            }

            var circuitId = GetOrCreateCircuitIdForRestoration();

            var userSession = new CircuitUserSession
            {
                UserId = dbSession.User.Id,
                Username = dbSession.User.Username,
                Email = dbSession.User.Email,
                DisplayName = dbSession.User.DisplayName,
                WarehouseId = dbSession.User.WarehouseId,
                Role = dbSession.User.Role
            };

            _userStore.RestoreUserFromDbSession(userSession, circuitId);
            _userStore.SetSessionId(sessionCookie);

            // Update LastActivity and IP
            dbSession.LastActivity = DateTime.UtcNow;
            dbSession.IpAddress = currentIp;
            await context.SaveChangesAsync(cancellationToken);

            LogSessionRestoredFromCookie(_logger, dbSession.User.Username, dbSession.User.Id, sessionCookie, circuitId, dbSession.DeviceType);

            await _authStateProvider.MarkUserAsAuthenticated(dbSession.User);

            return true;
        }
        catch (Exception ex)
        {
            LogSessionRestoreError(_logger, ex);
            return false;
        }
    }

    private bool IsMobileDevice(string userAgent)
    {
        if (string.IsNullOrEmpty(userAgent))
            return false;

        var mobileKeywords = new[] { "Mobile", "Android", "iPhone", "iPad", "iPod", "webOS", "BlackBerry", "Opera Mini", "IEMobile" };
        return mobileKeywords.Any(keyword => userAgent.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private string? GetCurrentCircuitId()
    {
        var httpContext = _httpContextAccessor?.HttpContext;
        if (httpContext == null)
            return null;

        if (httpContext.Items.TryGetValue("CircuitId", out var circuitIdObj) && circuitIdObj is string circuitId)
            return circuitId;

        return null;
    }

    /// <summary>
    /// Gets the current circuit ID or creates a new one for session restoration.
    /// </summary>
    private string GetOrCreateCircuitIdForRestoration()
    {
        var httpContext = _httpContextAccessor?.HttpContext;
        if (httpContext == null)
        {
            var newCircuitId = $"restored-{Guid.NewGuid():N}";
            LogCircuitGeneratedNoHttpContext(_logger, newCircuitId);
            return newCircuitId;
        }

        if (httpContext.Items.TryGetValue("CircuitId", out var circuitIdObj) && circuitIdObj is string circuitId && !string.IsNullOrEmpty(circuitId))
        {
            LogCircuitFromHttpContext(_logger, circuitId);
            return circuitId;
        }

        var restoredCircuitId = $"restored-{Guid.NewGuid():N}";
        httpContext.Items["CircuitId"] = restoredCircuitId;

        LogCircuitGeneratedForRestoration(_logger, restoredCircuitId);
        return restoredCircuitId;
    }

    public async Task<string?> GetCurrentSessionIdAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Check CircuitUserStore (Blazor-persisted session ID)
            var sessionId = _userStore.GetSessionId();

            if (!string.IsNullOrEmpty(sessionId))
                return sessionId;

            // 2. Fallback: HttpContext.Items (set in middleware)
            sessionId = _httpContextAccessor.HttpContext?.Items["SessionId"]?.ToString();

            if (!string.IsNullOrEmpty(sessionId))
                return sessionId;

            // 3. Fallback: cookie-based insights session ID (for reporting)
            var cookieSessionId = _httpContextAccessor.HttpContext?.Request.Cookies["InsightsSessionId"];
            if (!string.IsNullOrEmpty(cookieSessionId))
                return cookieSessionId;

            return null;
        }
        catch (Exception ex)
        {
            LogGetCurrentSessionIdError(_logger, ex);
            return null;
        }
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var currentUser = await GetCurrentUserAsync();
        if (currentUser != null)
        {
            await _auditService.SafeLogAsync(_logger, "LOGOUT", "User", currentUser.Id, null, AuditSeverity.Info);

            // End active session (optional)
            if (_sessionManagementService != null)
            {
                try
                {
                    var sessionId = _userStore.GetSessionId();

                    if (string.IsNullOrEmpty(sessionId))
                    {
                        sessionId = _httpContextAccessor?.HttpContext?.Items["SessionId"]?.ToString();
                    }

                    if (!string.IsNullOrEmpty(sessionId))
                    {
                        await _sessionManagementService.EndSessionAsync(
                            sessionId,
                            SessionEndReason.UserLogout,
                            "User initiated logout"
                        );

                        LogLogoutSessionEnded(_logger, sessionId, currentUser.Id);
                    }
                    else
                    {
                        LogLogoutNoSessionId(_logger, currentUser.Id);
                    }
                }
                catch (Exception ex)
                {
                    LogLogoutSessionEndError(_logger, ex, currentUser.Id);
                }
            }
        }

        await _authStateProvider.MarkUserAsLoggedOut();
    }

    public bool IsAuthenticated()
    {
        var user = _userStore.GetUser();
        return user != null;
    }

    public int GetCurrentWarehouseId()
    {
        var user = _userStore.GetUser();
        return user?.WarehouseId ?? 1;
    }

    public async Task<bool> Verify2FACodeAsync(int userId, string code, CancellationToken cancellationToken = default)
    {
        if (_twoFactorService == null)
        {
            LogTwoFactorServiceUnavailable(_logger);
            return false;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context.Users.FindAsync(userId);
        if (user == null || !user.TwoFactorEnabled || string.IsNullOrEmpty(user.TwoFactorSecret))
            return false;

        if (user.TwoFAFailedAttempts >= 10)
        {
            if (user.TwoFALockedUntil.HasValue && user.TwoFALockedUntil > DateTime.UtcNow)
            {
                var remaining = (user.TwoFALockedUntil.Value - DateTime.UtcNow).TotalMinutes;
                LogTwoFaLocked(_logger, userId, Math.Ceiling(remaining));

                await _auditService.SafeLogAsync(_logger, "2FA_LOCKED", "User", userId,
                    new { RemainingMinutes = Math.Ceiling(remaining), Attempts = user.TwoFAFailedAttempts },
                    AuditSeverity.Critical);

                return false;
            }

            user.TwoFAFailedAttempts = 0;
            user.TwoFALockedUntil = null;
        }

        var isValid = _twoFactorService.ValidateCode(user.TwoFactorSecret, code);

        if (!isValid)
        {
            user.TwoFAFailedAttempts++;

            if (user.TwoFAFailedAttempts >= 10)
            {
                user.TwoFALockedUntil = DateTime.UtcNow.AddMinutes(15);

                await _auditService.SafeLogAsync(_logger, "2FA_LOCKED_DUE_TO_FAILED_ATTEMPTS", "User", userId,
                    new { Attempts = user.TwoFAFailedAttempts },
                    AuditSeverity.Critical);

                LogTwoFaLockedAfterAttempts(_logger, userId, user.TwoFAFailedAttempts);
            }
            else
            {
                await _auditService.SafeLogAsync(_logger, "2FA_VERIFICATION_FAILED", "User", userId,
                    new { Attempts = user.TwoFAFailedAttempts, RemainingAttempts = 10 - user.TwoFAFailedAttempts },
                    AuditSeverity.Warning);
            }

            await context.SaveChangesAsync(cancellationToken);
        }
        else
        {
            user.TwoFAFailedAttempts = 0;
            user.TwoFALockedUntil = null;
            await context.SaveChangesAsync(cancellationToken);
        }

        return isValid;
    }

    public async Task<bool> Verify2FARecoveryCodeAsync(int userId, string recoveryCode, CancellationToken cancellationToken = default)
    {
        if (_twoFactorService == null)
        {
            LogTwoFactorServiceUnavailable(_logger);
            return false;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context.Users.FindAsync(userId);
        if (user == null || !user.TwoFactorEnabled || string.IsNullOrEmpty(user.TwoFactorRecoveryCodes))
            return false;

        var isValid = _twoFactorService.ValidateRecoveryCode(user.TwoFactorRecoveryCodes, recoveryCode);

        if (isValid)
        {
            user.TwoFactorRecoveryCodes = _twoFactorService.RemoveUsedRecoveryCode(
                user.TwoFactorRecoveryCodes,
                recoveryCode
            );
            await context.SaveChangesAsync(cancellationToken);
            await _auditService.SafeLogAsync(_logger, "2FA_RECOVERY_CODE_USED", "User", userId, null, AuditSeverity.Info);
        }
        else
        {
            await _auditService.SafeLogAsync(_logger, "2FA_RECOVERY_CODE_FAILED", "User", userId, null, AuditSeverity.Warning);
        }

        return isValid;
    }

    // Passwordless login (magic link)
    public async Task<Result<User>> LoginWithMagicLinkAsync(string token, CancellationToken cancellationToken = default)
    {
        if (_passwordlessLoginService is null)
        {
            LogPasswordlessUnavailable(_logger);
            return Result<User>.Failure(LoginFailures.PasswordlessUnavailable);
        }

        var ipAddress = _httpContextAccessor.GetClientIp();
        var userAgent = _httpContextAccessor?.HttpContext?.Request.Headers["User-Agent"].ToString();

        var user = await _passwordlessLoginService.ValidateMagicLinkAsync(token, ipAddress, userAgent);

        if (user == null)
            return Result<User>.Failure(LoginFailures.MagicLinkInvalid);

        // Check GDPR consent
        if (!user.GdprConsentGiven)
        {
            LogMagicLinkGdprDenied(_logger, user.Id);
            return Result<User>.Failure(LoginFailures.GdprConsentRequired);
        }

        if (!user.AnalyticsConsent || !user.DeviceFingerprintConsent)
        {
            LogMagicLinkConsentDenied(_logger, user.Id);
            return Result<User>.Failure(LoginFailures.GranularConsentRequired);
        }

        // Check IP restrictions
        if (_ipAccessService is not null && !string.IsNullOrEmpty(ipAddress))
        {
            var ipResult = await _ipAccessService.CheckAccessAsync(user.Id, ipAddress);
            if (!ipResult.IsAllowed)
            {
                LogMagicLinkIpDenied(_logger, ipAddress, user.Id, ipResult.Message);
                await _auditService.SafeLogAsync(_logger, "MAGIC_LINK_LOGIN_IP_DENIED", "User", user.Id,
                    new { IpAddress = ipAddress, Message = ipResult.Message }, AuditSeverity.Warning);
                return Result<User>.Failure(LoginFailures.IpDenied, ipResult.MatchedRule);
            }
        }

        if (_sessionManagementService != null)
        {
            try
            {
                var session = await _sessionManagementService.CreateSessionAsync(
                    user.Id,
                    user.WarehouseId,
                    ipAddress ?? "Unknown",
                    userAgent ?? "Unknown"
                );

                _userStore.SetSessionId(session.SessionId);

                LogMagicLinkSuccess(_logger, user.Id, session.SessionId);
            }
            catch (Exception ex)
            {
                LogMagicLinkSessionError(_logger, ex);
            }
        }

        await _authStateProvider.MarkUserAsAuthenticated(user);

        return Result<User>.Success(user);
    }

    // Passkey/WebAuthn login
    public async Task<Result<User>> LoginWithPasskeyAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context.Users
            .Include(u => u.Warehouse)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
        {
            LogPasskeyUserNotFound(_logger, userId);
            return Result<User>.Failure(LoginFailures.UserNotFound);
        }

        if (!user.IsActive || user.IsDeleted)
        {
            LogPasskeyInactive(_logger, userId);
            await _auditService.SafeLogAsync(_logger, "PASSKEY_LOGIN_DENIED", "User", userId,
                new { Reason = "User inactive" }, AuditSeverity.Warning);
            return Result<User>.Failure(LoginFailures.Inactive);
        }

        if (user.ApprovalStatus != UserApprovalStatus.Approved)
        {
            LogPasskeyNotApproved(_logger, userId, user.ApprovalStatus);
            await _auditService.SafeLogAsync(_logger, "PASSKEY_LOGIN_DENIED", "User", userId,
                new { Reason = "Not approved", Status = user.ApprovalStatus.ToString() }, AuditSeverity.Warning);
            var failureCode = user.ApprovalStatus == UserApprovalStatus.Rejected
                ? LoginFailures.Rejected
                : LoginFailures.PendingApproval;
            return Result<User>.Failure(failureCode);
        }

        if (!user.GdprConsentGiven)
        {
            LogPasskeyGdprDenied(_logger, userId);
            return Result<User>.Failure(LoginFailures.GdprConsentRequired);
        }

        if (!user.AnalyticsConsent || !user.DeviceFingerprintConsent)
        {
            LogPasskeyConsentDenied(_logger, userId);
            return Result<User>.Failure(LoginFailures.GranularConsentRequired);
        }

        // Check IP restrictions
        var clientIp = _httpContextAccessor.GetClientIp();
        if (!string.IsNullOrEmpty(clientIp) && _ipAccessService is not null)
        {
            var ipResult = await _ipAccessService.CheckAccessAsync(user.Id, clientIp);
            if (!ipResult.IsAllowed)
            {
                LogPasskeyIpDenied(_logger, clientIp, userId);
                await _auditService.SafeLogAsync(_logger, "PASSKEY_LOGIN_IP_DENIED", "User", userId,
                    new { IpAddress = clientIp, Message = ipResult.Message }, AuditSeverity.Warning);
                return Result<User>.Failure(LoginFailures.IpDenied, ipResult.MatchedRule);
            }
        }

        // Update login data
        user.LastLoginAt = DateTime.UtcNow;
        user.LastLoginIp = clientIp;
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        await context.SaveChangesAsync(cancellationToken);

        await _auditService.SafeLogAsync(_logger, "PASSKEY_LOGIN_SUCCESS", "User", userId,
            new { IP = clientIp }, AuditSeverity.Info);

        if (_sessionManagementService != null)
        {
            try
            {
                var ipAddress = clientIp ?? "Unknown";
                var userAgent = _httpContextAccessor?.HttpContext?.Request.Headers["User-Agent"].ToString() ?? "Unknown";

                var session = await _sessionManagementService.CreateSessionAsync(
                    user.Id,
                    user.WarehouseId,
                    ipAddress,
                    userAgent
                );

                _userStore.SetSessionId(session.SessionId);

                LogPasskeyLoginSuccess(_logger, user.Id, session.SessionId);

                // Save device fingerprint
                if (_deviceFingerprintService is not null && _httpContextAccessor?.HttpContext is not null)
                {
                    try
                    {
                        var thumbmarkFp = _httpContextAccessor.HttpContext.Request.Cookies["DeviceFingerprint"];
                        if (!string.IsNullOrEmpty(thumbmarkFp))
                        {
                            await _deviceFingerprintService.SaveDeviceFingerprintAsync(session.Id, thumbmarkFp, _httpContextAccessor.HttpContext);
                            LogPasskeyFingerprintSavedFromCookie(_logger, session.SessionId);
                        }
                        else
                        {
                            LogPasskeyNoFingerprintCookie(_logger, session.SessionId);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogPasskeyFingerprintError(_logger, ex);
                    }
                }

                // Start session monitor
                try
                {
                    if (_sessionMonitorService is not null)
                    {
                        var circuitId = GetCurrentCircuitId();
                        if (!string.IsNullOrEmpty(circuitId))
                        {
                            await _sessionMonitorService.StartMonitoringAsync(user.Id, session.SessionId, circuitId);
                        }
                        else
                        {
                            await _sessionMonitorService.StartMonitoringAsync(user.Id, session.SessionId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogPasskeySessionMonitorError(_logger, ex);
                }
            }
            catch (Exception ex)
            {
                LogPasskeySessionCreateError(_logger, ex);
            }
        }

        await _authStateProvider.MarkUserAsAuthenticated(user);

        return Result<User>.Success(user);
    }

    // E-Mail OTP 2FA methods

    public async Task<bool> VerifyEmailOtpAsync(int userId, string code, CancellationToken cancellationToken = default)
    {
        if (_emailOtpService == null)
        {
            LogEmailOtpServiceUnavailable(_logger);
            return false;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var user = await context.Users.FindAsync(userId);
        if (user == null) return false;

        // Check 2FA lockout (same mechanism as authenticator)
        if (user.TwoFAFailedAttempts >= 10)
        {
            if (user.TwoFALockedUntil.HasValue && user.TwoFALockedUntil > DateTime.UtcNow)
            {
                var remaining = (user.TwoFALockedUntil.Value - DateTime.UtcNow).TotalMinutes;
                LogTwoFaLocked(_logger, userId, Math.Ceiling(remaining));
                return false;
            }
            // Lockout expired
            user.TwoFAFailedAttempts = 0;
            user.TwoFALockedUntil = null;
        }

        var isValid = await _emailOtpService.ValidateOtpAsync(userId, code);

        if (!isValid)
        {
            user.TwoFAFailedAttempts++;
            if (user.TwoFAFailedAttempts >= 10)
            {
                user.TwoFALockedUntil = DateTime.UtcNow.AddMinutes(15);
                await _auditService.SafeLogAsync(_logger, "2FA_LOCKED_DUE_TO_FAILED_ATTEMPTS", "User", userId,
                    new { Method = TwoFactorMethods.EmailOtp, Attempts = user.TwoFAFailedAttempts }, AuditSeverity.Critical);
            }
            else
            {
                await _auditService.SafeLogAsync(_logger, "EMAIL_OTP_VERIFICATION_FAILED", "User", userId,
                    new { Attempts = user.TwoFAFailedAttempts }, AuditSeverity.Warning);
            }
            await context.SaveChangesAsync(cancellationToken);
        }
        else
        {
            user.TwoFAFailedAttempts = 0;
            user.TwoFALockedUntil = null;
            await context.SaveChangesAsync(cancellationToken);
            await _auditService.SafeLogAsync(_logger, "EMAIL_OTP_VERIFIED", "User", userId, null, AuditSeverity.Info);
        }

        return isValid;
    }
}
