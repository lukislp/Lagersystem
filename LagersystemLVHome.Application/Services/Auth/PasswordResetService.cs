using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.Application.Services;

/// <inheritdoc cref="IPasswordResetService"/>
public sealed class PasswordResetService : IPasswordResetService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly IAuditService? _auditService;
    private readonly ILogger<PasswordResetService> _logger;

    public PasswordResetService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        ILogger<PasswordResetService> logger,
        IHttpContextAccessor? httpContextAccessor = null,
        IAuditService? auditService = null)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
        _auditService = auditService;
    }

    public async Task<bool> ChangePasswordAsync(int userId, string oldPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context.Users.FindAsync([userId], cancellationToken);
        if (user is null) return false;

        if (!BCrypt.Net.BCrypt.Verify(oldPassword, user.PasswordHash))
        {
            await _auditService.SafeLogAsync(_logger, "PASSWORD_CHANGE_FAILED", "User", userId,
                new { Reason = "Invalid old password" }, AuditSeverity.Warning);
            return false;
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.LastPasswordChangeAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        await _auditService.SafeLogAsync(_logger, "PASSWORD_CHANGED", "User", userId, null, AuditSeverity.Info);
        return true;
    }

    public async Task<string?> RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context.Users
            .FirstOrDefaultAsync(u => u.Email == email && u.IsActive && !u.IsDeleted, cancellationToken);

        if (user is null)
        {
            await _auditService.SafeLogAsync(_logger, "PASSWORD_RESET_REQUESTED_INVALID", "User", null,
                new { Email = email }, AuditSeverity.Warning);
            return null;
        }

        var oldTokens = await context.PasswordResetTokens
            .Where(t => t.UserId == user.Id && !t.IsUsed)
            .ToListAsync(cancellationToken);
        context.PasswordResetTokens.RemoveRange(oldTokens);

        var token = new PasswordResetToken
        {
            UserId = user.Id,
            Token = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            IpAddress = _httpContextAccessor.GetClientIp()
        };

        context.PasswordResetTokens.Add(token);
        await context.SaveChangesAsync(cancellationToken);

        await _auditService.SafeLogAsync(_logger, "PASSWORD_RESET_REQUESTED", "User", user.Id, null, AuditSeverity.Info);

        return token.Token;
    }

    public async Task<bool> ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var resetToken = await context.PasswordResetTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == token && !t.IsUsed, cancellationToken);

        if (resetToken is null || resetToken.ExpiresAt < DateTime.UtcNow)
        {
            await _auditService.SafeLogAsync(_logger, "PASSWORD_RESET_FAILED", "User", null,
                new { Reason = "Invalid or expired token" }, AuditSeverity.Warning);
            return false;
        }

        resetToken.User.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        resetToken.User.LastPasswordChangeAt = DateTime.UtcNow;
        resetToken.IsUsed = true;

        resetToken.User.FailedLoginAttempts = 0;
        resetToken.User.LockedUntil = null;

        await context.SaveChangesAsync(cancellationToken);

        await _auditService.SafeLogAsync(_logger, "PASSWORD_RESET_SUCCESS", "User", resetToken.UserId, null, AuditSeverity.Info);
        return true;
    }

    public async Task<bool> ValidateResetTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var resetToken = await context.PasswordResetTokens
            .FirstOrDefaultAsync(t => t.Token == token && !t.IsUsed, cancellationToken);

        return resetToken is not null && resetToken.ExpiresAt >= DateTime.UtcNow;
    }

    private string? GetClientIp()
    {
        var context = _httpContextAccessor?.HttpContext;
        if (context is null) return null;

        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString();
    }

    private async Task LogAuditAsync(string action, string entity, int? entityId, object? details, AuditSeverity severity, CancellationToken cancellationToken = default)
    {
        if (_auditService is null) return;
        try
        {
            await _auditService.LogAsync(action, entity, entityId, details, severity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging audit entry for action {Action}", action);
        }
    }
}
