using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.API.DTOs;

namespace LagersystemLVHome.API.Controllers;

/// <summary>
/// API controller for user info (read-only).
/// </summary>
[ApiController]
[Route("api/users")]
public class UsersApiController : BaseApiController
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly ILogger<UsersApiController> _logger;

    public UsersApiController(IDbContextFactory<InventoryDbContext> contextFactory, ILogger<UsersApiController> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<UserInfoDto>), 200)]
    public async Task<ActionResult<ApiResponse<UserInfoDto>>> GetCurrentUser()
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var user = await context.Users
                .Include(u => u.Warehouse)
                .FirstOrDefaultAsync(u => u.Id == CurrentUserId);

            if (user == null)
            {
                return NotFound<UserInfoDto>("User not found");
            }

            var dto = new UserInfoDto
            {
                Id = user.Id,
                Username = user.Username,
                DisplayName = user.DisplayName,
                Email = user.Email,
                Role = user.Role.ToString(),
                WarehouseId = user.WarehouseId,
                WarehouseName = user.Warehouse.Name,
                TwoFactorEnabled = user.TwoFactorEnabled,
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt
            };

            _logger.LogInformation("API: User info fetched for user {UserId}", CurrentUserId);
            return Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching user info");
            return Error<UserInfoDto>("Error fetching user info", 500);
        }
    }

    [HttpGet("activity-summary")]
    [ProducesResponseType(typeof(ApiResponse<UserActivitySummaryDto>), 200)]
    public async Task<ActionResult<ApiResponse<UserActivitySummaryDto>>> GetActivitySummary()
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var userId = CurrentUserId;
            var warehouseId = CurrentWarehouseId;

            var auditLogs = await context.AuditLogs
                .Where(al => al.UserId == userId && al.WarehouseId == warehouseId)
                .OrderByDescending(al => al.Timestamp)
                .Take(100)
                .ToListAsync();

            var loginCount = auditLogs.Count(al => al.Action.Contains("LOGIN"));
            var productsCreated = auditLogs.Count(al => al.Action.Contains("PRODUCT_CREATED"));
            var stockMovements = await context.StockMovements
                .CountAsync(sm => sm.WarehouseId == warehouseId);

            var recentActivities = auditLogs.Take(10).Select(al => new RecentActivityDto
            {
                Action = al.Action,
                Entity = al.Entity,
                Timestamp = al.Timestamp,
                Details = al.Details
            }).ToList();

            var summary = new UserActivitySummaryDto
            {
                TotalActions = auditLogs.Count,
                LoginCount = loginCount,
                ProductsCreated = productsCreated,
                StockMovements = stockMovements,
                LastActivity = auditLogs.FirstOrDefault()?.Timestamp,
                RecentActivities = recentActivities
            };

            _logger.LogInformation("API: Activity summary fetched for user {UserId}", userId);
            return Success(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching activity summary");
            return Error<UserActivitySummaryDto>("Error fetching activity summary", 500);
        }
    }
}
