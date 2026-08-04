using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.Application.Services;

/// <inheritdoc cref="IUserProfileService"/>
public sealed class UserProfileService : IUserProfileService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly ILogger<UserProfileService> _logger;

    public UserProfileService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        ILogger<UserProfileService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<bool> AnyUsersExistAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Users.AnyAsync(cancellationToken);
    }

    public async Task<User?> GetActiveUserByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username)) return null;
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
    }

    public async Task<User?> GetActiveUserByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email && u.IsActive && !u.IsDeleted, cancellationToken);
    }

    public async Task<User?> GetUserWithWarehouseAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Users
            .AsNoTracking()
            .Include(u => u.Warehouse)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
    }

    public async Task<int> CountApprovedActiveUsersInWarehouseAsync(int warehouseId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Users.CountAsync(u =>
            u.WarehouseId == warehouseId && u.IsActive && u.ApprovalStatus == UserApprovalStatus.Approved,
            cancellationToken);
    }

    public async Task<int> CountActiveUsersInWarehouseAsync(int warehouseId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Users.CountAsync(
            u => u.WarehouseId == warehouseId && u.IsActive, cancellationToken);
    }

    public async Task<string?> GetTwoFactorRecoveryCodesAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.TwoFactorRecoveryCodes)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> UpdateConsentPreferencesAsync(int userId, bool analyticsConsent, bool fingerprintConsent, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var user = await context.Users.FindAsync([userId], cancellationToken);
            if (user is null) return false;

            var now = DateTime.UtcNow;
            user.AnalyticsConsent = analyticsConsent;
            user.AnalyticsConsentDate = now;
            user.DeviceFingerprintConsent = fingerprintConsent;
            user.DeviceFingerprintConsentDate = now;
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating consent preferences for user {UserId}", userId);
            return false;
        }
    }

    public async Task<bool> SetProfileImagePathAsync(int userId, string profileImagePath, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var user = await context.Users.FindAsync([userId], cancellationToken);
            if (user is null) return false;

            user.ProfileImagePath = profileImagePath;
            user.ProfileImageUploadedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting profile image for user {UserId}", userId);
            return false;
        }
    }

    public async Task<bool> UpdateProfileImageAsync(int userId, string? profileImagePath, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var user = await context.Users.FindAsync([userId], cancellationToken);
            if (user is null) return false;

            user.ProfileImagePath = profileImagePath;
            user.ProfileImageUploadedAt = string.IsNullOrEmpty(profileImagePath) ? null : DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating profile image for user {UserId}", userId);
            return false;
        }
    }

    public async Task<bool> ApproveAsAdminAsync(int userId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var user = await context.Users.FindAsync([userId], cancellationToken);
            if (user is null) return false;

            user.ApprovalStatus = UserApprovalStatus.Approved;
            user.Role = UserRole.Admin;
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving user {UserId} as admin", userId);
            return false;
        }
    }

    public async Task<bool> RevokeMarketingConsentAsync(int userId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var user = await context.Users.FindAsync([userId], cancellationToken);
            if (user is null) return false;

            user.MarketingConsent = false;
            user.MarketingConsentDate = null;
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking marketing consent for user {UserId}", userId);
            return false;
        }
    }

    public async Task<bool> RevokeConsentAsync(int userId, string consentType, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var user = await context.Users.FindAsync([userId], cancellationToken);
            if (user is null) return false;

            switch (consentType)
            {
                case "Analytics":
                case "Analytics & Performance":
                    user.AnalyticsConsent = false;
                    user.AnalyticsConsentDate = null;
                    break;
                case "Fingerprint":
                case "Device Fingerprinting":
                    user.DeviceFingerprintConsent = false;
                    user.DeviceFingerprintConsentDate = null;
                    break;
                default:
                    return false;
            }

            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking consent {Type} for user {UserId}", consentType, userId);
            return false;
        }
    }
}
