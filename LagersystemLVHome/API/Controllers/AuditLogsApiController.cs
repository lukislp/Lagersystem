using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.API.DTOs;

namespace LagersystemLVHome.API.Controllers;

[ApiController]
[Route("api/audit")]
public class AuditLogsApiController : BaseApiController
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly ILogger<AuditLogsApiController> _logger;

    public AuditLogsApiController(
        IDbContextFactory<InventoryDbContext> contextFactory,
        ILogger<AuditLogsApiController> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    [HttpGet("logs")]
    public async Task<ActionResult<ApiResponse<List<AuditLogDto>>>> GetLogs([FromQuery] int limit = 50)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var logs = await context.AuditLogs
            .Include(al => al.User)
            .Where(al => al.WarehouseId == CurrentWarehouseId)
            .OrderByDescending(al => al.Timestamp)
            .Take(limit)
            .Select(al => new AuditLogDto
            {
                Id = al.Id,
                Action = al.Action,
                Entity = al.Entity,
                EntityId = al.EntityId,
                Details = al.Details,
                Severity = al.Severity.ToString(),
                UserId = al.UserId,
                Username = al.User != null ? al.User.Username : null,
                IpAddress = al.IpAddress,
                Timestamp = al.Timestamp
            })
            .ToListAsync();
        return Success(logs);
    }

    [HttpGet("logs/by-entity")]
    public async Task<ActionResult<ApiResponse<List<AuditLogDto>>>> GetLogsByEntity(
        [FromQuery] string entity,
        [FromQuery] int? id = null,
        [FromQuery] int limit = 50)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var query = context.AuditLogs
            .Include(al => al.User)
            .Where(al => al.WarehouseId == CurrentWarehouseId && al.Entity == entity);

        if (id.HasValue)
            query = query.Where(al => al.EntityId == id.Value);

        var logs = await query
            .OrderByDescending(al => al.Timestamp)
            .Take(limit)
            .Select(al => new AuditLogDto
            {
                Id = al.Id,
                Action = al.Action,
                Entity = al.Entity,
                EntityId = al.EntityId,
                Details = al.Details,
                Severity = al.Severity.ToString(),
                UserId = al.UserId,
                Username = al.User != null ? al.User.Username : null,
                IpAddress = al.IpAddress,
                Timestamp = al.Timestamp
            })
            .ToListAsync();
        return Success(logs);
    }

    [HttpGet("logs/by-user")]
    public async Task<ActionResult<ApiResponse<List<AuditLogDto>>>> GetLogsByUser(
        [FromQuery] int userId,
        [FromQuery] int limit = 50)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var logs = await context.AuditLogs
            .Include(al => al.User)
            .Where(al => al.WarehouseId == CurrentWarehouseId && al.UserId == userId)
            .OrderByDescending(al => al.Timestamp)
            .Take(limit)
            .Select(al => new AuditLogDto
            {
                Id = al.Id,
                Action = al.Action,
                Entity = al.Entity,
                EntityId = al.EntityId,
                Details = al.Details,
                Severity = al.Severity.ToString(),
                UserId = al.UserId,
                Username = al.User != null ? al.User.Username : null,
                IpAddress = al.IpAddress,
                Timestamp = al.Timestamp
            })
            .ToListAsync();
        return Success(logs);
    }
}
