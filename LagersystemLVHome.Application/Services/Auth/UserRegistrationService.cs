using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.Application.Services;

/// <inheritdoc cref="IUserRegistrationService"/>
public sealed class UserRegistrationService : IUserRegistrationService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly IAuditService? _auditService;
    private readonly ILogger<UserRegistrationService> _logger;

    public UserRegistrationService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        ILogger<UserRegistrationService> logger,
        IHttpContextAccessor? httpContextAccessor = null,
        IAuditService? auditService = null)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
        _auditService = auditService;
    }

    public async Task<User?> RegisterAsync(
        string username,
        string email,
        string password,
        string displayName,
        int warehouseId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        if (await context.Users.AnyAsync(u => u.Username == username, cancellationToken))
        {
            await _auditService.SafeLogAsync(_logger, "REGISTER_FAILED", "User", null,
                new { Username = username, Reason = "Username exists" }, AuditSeverity.Warning);
            return null;
        }

        if (await context.Users.AnyAsync(u => u.Email == email, cancellationToken))
        {
            await _auditService.SafeLogAsync(_logger, "REGISTER_FAILED", "User", null,
                new { Email = email, Reason = "Email exists" }, AuditSeverity.Warning);
            return null;
        }

        var warehouse = await context.Warehouses.FindAsync([warehouseId], cancellationToken);
        if (warehouse is null || !warehouse.IsActive)
        {
            await _auditService.SafeLogAsync(_logger, "REGISTER_FAILED", "User", null,
                new { WarehouseId = warehouseId, Reason = "Warehouse invalid" }, AuditSeverity.Warning);
            return null;
        }

        var approvedUserCount = await context.Users.CountAsync(u =>
            u.WarehouseId == warehouseId &&
            u.IsActive &&
            u.ApprovalStatus == UserApprovalStatus.Approved, cancellationToken);

        if (approvedUserCount >= warehouse.MaxUsers)
        {
            await _auditService.SafeLogAsync(_logger, "REGISTER_FAILED", "User", null,
                new { WarehouseId = warehouseId, Reason = "Warehouse full" }, AuditSeverity.Warning);
            return null;
        }

        var now = DateTime.UtcNow;
        var user = new User
        {
            Username = username,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            DisplayName = displayName,
            WarehouseId = warehouseId,
            Role = UserRole.User,
            ApprovalStatus = UserApprovalStatus.Pending,
            LastLoginAt = now,
            LastPasswordChangeAt = now,
            LastLoginIp = _httpContextAccessor.GetClientIp()
        };

        context.Users.Add(user);
        await context.SaveChangesAsync(cancellationToken);

        await _auditService.SafeLogAsync(_logger, "REGISTER_SUCCESS", "User", user.Id,
            new { user.Username, user.Email, WarehouseId = warehouseId }, AuditSeverity.Info);

        return user;
    }

    public async Task<List<User>> GetPendingUsersAsync(int warehouseId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Users
            .AsNoTracking()
            .Include(u => u.Warehouse)
            .Where(u => u.WarehouseId == warehouseId && u.ApprovalStatus == UserApprovalStatus.Pending)
            .OrderBy(u => u.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ApproveUserAsync(int userId, int approvedByUserId, string? notes = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context.Users.FindAsync([userId], cancellationToken);
        if (user is null || user.ApprovalStatus != UserApprovalStatus.Pending)
            return false;

        user.ApprovalStatus = UserApprovalStatus.Approved;
        user.ApprovedByUserId = approvedByUserId;
        user.ApprovedAt = DateTime.UtcNow;
        user.ApprovalNotes = notes;

        await context.SaveChangesAsync(cancellationToken);
        await _auditService.SafeLogAsync(_logger, "USER_APPROVED", "User", userId,
            new { ApprovedBy = approvedByUserId, Notes = notes }, AuditSeverity.Info);

        return true;
    }

    public async Task<bool> RejectUserAsync(int userId, int rejectedByUserId, string? notes = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context.Users.FindAsync([userId], cancellationToken);
        if (user is null || user.ApprovalStatus != UserApprovalStatus.Pending)
            return false;

        user.ApprovalStatus = UserApprovalStatus.Rejected;
        user.ApprovedByUserId = rejectedByUserId;
        user.ApprovedAt = DateTime.UtcNow;
        user.ApprovalNotes = notes;
        user.IsActive = false;

        await context.SaveChangesAsync(cancellationToken);
        await _auditService.SafeLogAsync(_logger, "USER_REJECTED", "User", userId,
            new { RejectedBy = rejectedByUserId, Notes = notes }, AuditSeverity.Warning);

        return true;
    }

    public async Task<bool> ChangeUserRoleAsync(int userId, UserRole newRole, int changedByUserId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context.Users.FindAsync([userId], cancellationToken);
        var changedByUser = await context.Users.FindAsync([changedByUserId], cancellationToken);

        if (user is null || changedByUser is null)
            return false;

        if (newRole == UserRole.SuperAdmin)
        {
            await _auditService.SafeLogAsync(_logger, "ROLE_CHANGE_DENIED", "User", userId,
                new { Reason = "SuperAdmin role cannot be assigned", AttemptedBy = changedByUserId },
                AuditSeverity.Warning);
            return false;
        }

        if (user.Role == UserRole.SuperAdmin)
        {
            await _auditService.SafeLogAsync(_logger, "ROLE_CHANGE_DENIED", "User", userId,
                new { Reason = "SuperAdmin role cannot be changed", CurrentRole = user.Role.ToString(), AttemptedBy = changedByUserId },
                AuditSeverity.Warning);
            return false;
        }

        if (changedByUser.Role == UserRole.Admin)
        {
            if (user.Role >= UserRole.Admin)
            {
                await _auditService.SafeLogAsync(_logger, "ROLE_CHANGE_DENIED", "User", userId,
                    new { Reason = "Admin cannot change Admin or SuperAdmin roles", CurrentRole = user.Role.ToString(), AttemptedBy = changedByUserId },
                    AuditSeverity.Warning);
                return false;
            }

            if (newRole >= UserRole.Admin)
            {
                await _auditService.SafeLogAsync(_logger, "ROLE_CHANGE_DENIED", "User", userId,
                    new { Reason = "Admin cannot assign Admin or SuperAdmin role", TargetRole = newRole.ToString(), AttemptedBy = changedByUserId },
                    AuditSeverity.Warning);
                return false;
            }
        }
        else if (changedByUser.Role < UserRole.Admin)
        {
            await _auditService.SafeLogAsync(_logger, "ROLE_CHANGE_DENIED", "User", userId,
                new { Reason = "Insufficient permissions", ChangedByRole = changedByUser.Role.ToString() },
                AuditSeverity.Warning);
            return false;
        }

        var oldRole = user.Role;
        user.Role = newRole;

        await context.SaveChangesAsync(cancellationToken);
        await _auditService.SafeLogAsync(_logger, "ROLE_CHANGED", "User", userId,
            new
            {
                OldRole = oldRole.ToString(),
                NewRole = newRole.ToString(),
                ChangedBy = changedByUserId,
                ChangedByUsername = changedByUser.Username
            },
            AuditSeverity.Info);

        return true;
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
