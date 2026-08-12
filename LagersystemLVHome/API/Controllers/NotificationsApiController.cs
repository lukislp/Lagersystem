using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.API.DTOs;

namespace LagersystemLVHome.API.Controllers;

[ApiController]
[Route("api/notifications")]
public class NotificationsApiController : BaseApiController
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly ILogger<NotificationsApiController> _logger;

    public NotificationsApiController(IDbContextFactory<InventoryDbContext> contextFactory, ILogger<NotificationsApiController> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<NotificationDto>>>> GetNotifications([FromQuery] int limit = 50)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var notifications = await context.Notifications
            .Where(n => n.UserId == CurrentUserId && n.WarehouseId == CurrentWarehouseId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .Select(n => new NotificationDto
            {
                Id = n.Id,
                Type = n.Type.ToString(),
                Title = n.Title,
                Message = n.Message,
                Severity = "Info",
                IsRead = n.IsRead,
                CreatedAt = n.CreatedAt,
                RelatedEntity = null,
                RelatedEntityId = null
            })
            .ToListAsync();
        return Success(notifications);
    }

    [HttpGet("unread")]
    public async Task<ActionResult<ApiResponse<List<NotificationDto>>>> GetUnreadNotifications()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var notifications = await context.Notifications
            .Where(n => n.UserId == CurrentUserId && n.WarehouseId == CurrentWarehouseId && !n.IsRead)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new NotificationDto
            {
                Id = n.Id,
                Type = n.Type.ToString(),
                Title = n.Title,
                Message = n.Message,
                Severity = "Info",
                IsRead = n.IsRead,
                CreatedAt = n.CreatedAt,
                RelatedEntity = null,
                RelatedEntityId = null
            })
            .ToListAsync();
        return Success(notifications);
    }

    [HttpPut("{id}/read")]
    public async Task<ActionResult<ApiResponse<NotificationDto>>> MarkAsRead(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var notification = await context.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == CurrentUserId && n.WarehouseId == CurrentWarehouseId);
        if (notification == null) return NotFound<NotificationDto>("Notification not found");

        notification.IsRead = true;
        await context.SaveChangesAsync();

        var dto = new NotificationDto
        {
            Id = notification.Id,
            Type = notification.Type.ToString(),
            Title = notification.Title,
            Message = notification.Message,
            Severity = "Info",
            IsRead = notification.IsRead,
            CreatedAt = notification.CreatedAt,
            RelatedEntity = null,
            RelatedEntityId = null
        };
        return Success(dto, "Notification marked as read");
    }
}
