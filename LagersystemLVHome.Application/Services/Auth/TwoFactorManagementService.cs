using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.Application.Services;

/// <inheritdoc cref="ITwoFactorManagementService"/>
public sealed class TwoFactorManagementService : ITwoFactorManagementService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly ITwoFactorService? _twoFactorService;
    private readonly IEmailOtpService? _emailOtpService;
    private readonly IAuditService? _auditService;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ILogger<TwoFactorManagementService> _logger;

    public TwoFactorManagementService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        ILogger<TwoFactorManagementService> logger,
        ITwoFactorService? twoFactorService = null,
        IEmailOtpService? emailOtpService = null,
        IAuditService? auditService = null,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _twoFactorService = twoFactorService;
        _emailOtpService = emailOtpService;
        _auditService = auditService;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<bool> Enable2FAAsync(int userId, string secret, string verificationCode, CancellationToken cancellationToken = default)
    {
        if (_twoFactorService is null)
        {
            _logger.LogWarning("TwoFactorService not available");
            return false;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context.Users.FindAsync([userId], cancellationToken);
        if (user is null) return false;

        if (!_twoFactorService.ValidateCode(secret, verificationCode))
        {
            await _auditService.SafeLogAsync(_logger, "2FA_ENABLE_FAILED", "User", userId,
                new { Reason = "Invalid verification code" }, AuditSeverity.Warning);
            return false;
        }

        user.TwoFactorEnabled = true;
        user.TwoFactorSecret = secret;
        user.TwoFactorRecoveryCodes = System.Text.Json.JsonSerializer.Serialize(
            _twoFactorService.GenerateRecoveryCodes());

        await context.SaveChangesAsync(cancellationToken);
        await _auditService.SafeLogAsync(_logger, "2FA_ENABLED", "User", userId, null, AuditSeverity.Info);
        return true;
    }

    public async Task<bool> Disable2FAAsync(int userId, string password, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context.Users.FindAsync([userId], cancellationToken);
        if (user is null) return false;

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            await _auditService.SafeLogAsync(_logger, "2FA_DISABLE_FAILED", "User", userId,
                new { Reason = "Invalid password" }, AuditSeverity.Warning);
            return false;
        }

        user.TwoFactorEnabled = false;
        user.TwoFactorSecret = null;
        user.TwoFactorRecoveryCodes = null;

        await context.SaveChangesAsync(cancellationToken);
        await _auditService.SafeLogAsync(_logger, "2FA_DISABLED", "User", userId, null, AuditSeverity.Info);
        return true;
    }

    public async Task<bool> EnableEmailOtpAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context.Users.FindAsync([userId], cancellationToken);
        if (user is null) return false;

        user.EmailOtpEnabled = true;

        // If no authenticator is active, set EmailOtp as preferred method.
        if (!user.TwoFactorEnabled)
        {
            user.Preferred2FAMethod = TwoFactorMethods.EmailOtp;
        }

        await context.SaveChangesAsync(cancellationToken);
        await _auditService.SafeLogAsync(_logger, "EMAIL_OTP_ENABLED", "User", userId, null, AuditSeverity.Info);

        _logger.LogInformation("E-Mail OTP enabled for user {UserId}", userId);
        return true;
    }

    public async Task<bool> DisableEmailOtpAsync(int userId, string password, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context.Users.FindAsync([userId], cancellationToken);
        if (user is null) return false;

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            await _auditService.SafeLogAsync(_logger, "EMAIL_OTP_DISABLE_FAILED", "User", userId,
                new { Reason = "Invalid password" }, AuditSeverity.Warning);
            return false;
        }

        user.EmailOtpEnabled = false;

        // If authenticator is still active, fall back to it as preferred method.
        if (user.TwoFactorEnabled)
        {
            user.Preferred2FAMethod = TwoFactorMethods.Authenticator;
        }

        await context.SaveChangesAsync(cancellationToken);
        await _auditService.SafeLogAsync(_logger, "EMAIL_OTP_DISABLED", "User", userId, null, AuditSeverity.Info);
        return true;
    }

    public async Task<bool> SendEmailOtpAsync(int userId, CancellationToken cancellationToken = default)
    {
        if (_emailOtpService is null)
        {
            _logger.LogWarning("EmailOtpService not available");
            return false;
        }

        var ipAddress = _httpContextAccessor.GetClientIp();
        return await _emailOtpService.SendOtpAsync(userId, ipAddress);
    }

    public async Task<bool> SetPreferred2FAMethodAsync(int userId, string method, CancellationToken cancellationToken = default)
    {
        if (!TwoFactorMethods.IsKnown(method)) return false;

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context.Users.FindAsync([userId], cancellationToken);
        if (user is null) return false;

        // Verify the chosen method is actually enabled.
        if (method == TwoFactorMethods.Authenticator && !user.TwoFactorEnabled) return false;
        if (method == TwoFactorMethods.EmailOtp && !user.EmailOtpEnabled) return false;

        user.Preferred2FAMethod = method;
        await context.SaveChangesAsync(cancellationToken);

        await _auditService.SafeLogAsync(_logger, "2FA_PREFERRED_METHOD_CHANGED", "User", userId,
            new { Method = method }, AuditSeverity.Info);
        return true;
    }

    public async Task<TwoFactorMethodInfo> Get2FAMethodsAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.TwoFactorEnabled, u.EmailOtpEnabled, u.Preferred2FAMethod })
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null) return new TwoFactorMethodInfo();

        return new TwoFactorMethodInfo
        {
            AuthenticatorEnabled = user.TwoFactorEnabled,
            EmailOtpEnabled = user.EmailOtpEnabled,
            PreferredMethod = user.Preferred2FAMethod
        };
    }

    }
