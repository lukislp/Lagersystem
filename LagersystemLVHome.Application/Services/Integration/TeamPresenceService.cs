using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.Application.Services;

public sealed class TeamPresenceService : ITeamPresenceService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly ILogger<TeamPresenceService> _logger;
    private readonly Dictionary<string, UserPresence> _presenceCache = new();
    private readonly object _lock = new();

    public TeamPresenceService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        ILogger<TeamPresenceService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<List<UserPresence>> GetOnlineUsersInWarehouseAsync(int warehouseId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            // Widened from the "online" 5-minute threshold so DeterminePresenceStatus's Idle
            // (5-15 min) and Away (15-30 min) branches are actually reachable, instead of every
            // recently-inactive teammate silently disappearing from the presence list.
            var presenceWindowStart = DateTime.UtcNow.AddMinutes(-30);

            var activeSessions = await context.UserSessions
                .Where(s => s.LastActivity >= presenceWindowStart &&
                    s.IsActive &&
                    s.WarehouseId == warehouseId &&
                    s.DeviceType != "API")
                .Include(s => s.User)
                .ThenInclude(u => u!.Warehouse)
                .OrderByDescending(s => s.LastActivity)
                .ToListAsync(cancellationToken);

            var presenceList = new List<UserPresence>();

            foreach (var session in activeSessions)
            {
                if (session.User == null) continue;

                var presence = new UserPresence
                {
                    UserId = session.UserId,
                    Username = session.User.Username,
                    FullName = session.User.DisplayName,
                    Role = session.User.Role.ToString(),
                    WarehouseId = warehouseId,
                    WarehouseName = session.User.Warehouse?.Name ?? "Unknown",
                    CurrentPage = session.LastPageUrl ?? "/",
                    DeviceType = session.DeviceType ?? "Desktop",
                    LastSeen = session.LastActivity,
                    SessionId = session.SessionId,
                    IpAddress = session.IpAddress ?? "",
                    Status = DeterminePresenceStatus(session.LastActivity),
                    ProfileImagePath = session.User.ProfileImagePath
                };

                lock (_lock)
                {
                    var cacheKey = $"{session.UserId}_{session.SessionId}";
                    // SetCustomStatusAsync writes under "{userId}_default" until a session-keyed
                    // entry already exists, so a status set for a session we haven't read yet
                    // (the common case) only surfaces via this fallback key.
                    if (_presenceCache.TryGetValue(cacheKey, out var cached) ||
                        _presenceCache.TryGetValue($"{session.UserId}_default", out cached))
                    {
                        presence.CustomStatus = cached.CustomStatus;
                        if (cached.Status == PresenceStatus.DoNotDisturb || cached.Status == PresenceStatus.Away)
                        {
                            presence.Status = cached.Status;
                        }
                    }
                }

                presenceList.Add(presence);
            }

            return presenceList
                .GroupBy(p => p.UserId)
                .Select(g => g.OrderByDescending(p => p.LastSeen).First())
                .OrderBy(p => p.Status)
                .ThenBy(p => p.Username)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting online users in warehouse {WarehouseId}", warehouseId);
            return new List<UserPresence>();
        }
    }

    public async Task<List<UserPresence>> GetAllOnlineUsersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            // Widened from the "online" 5-minute threshold so DeterminePresenceStatus's Idle
            // (5-15 min) and Away (15-30 min) branches are actually reachable, instead of every
            // recently-inactive teammate silently disappearing from the presence list.
            var presenceWindowStart = DateTime.UtcNow.AddMinutes(-30);

            var activeSessions = await context.UserSessions
                .Where(s => s.LastActivity >= presenceWindowStart && s.IsActive && s.DeviceType != "API")
                .Include(s => s.User)
                .ThenInclude(u => u!.Warehouse)
                .OrderByDescending(s => s.LastActivity)
                .ToListAsync(cancellationToken);

            var presenceList = new List<UserPresence>();

            foreach (var session in activeSessions)
            {
                if (session.User == null) continue;

                var presence = new UserPresence
                {
                    UserId = session.UserId,
                    Username = session.User.Username,
                    FullName = session.User.DisplayName,
                    Role = session.User.Role.ToString(),
                    WarehouseId = session.User.WarehouseId,
                    WarehouseName = session.User.Warehouse?.Name ?? "No Warehouse",
                    CurrentPage = session.LastPageUrl ?? "/",
                    DeviceType = session.DeviceType ?? "Desktop",
                    LastSeen = session.LastActivity,
                    SessionId = session.SessionId,
                    IpAddress = session.IpAddress ?? "",
                    Status = DeterminePresenceStatus(session.LastActivity),
                    ProfileImagePath = session.User.ProfileImagePath
                };

                lock (_lock)
                {
                    var cacheKey = $"{session.UserId}_{session.SessionId}";
                    // SetCustomStatusAsync writes under "{userId}_default" until a session-keyed
                    // entry already exists, so a status set for a session we haven't read yet
                    // (the common case) only surfaces via this fallback key.
                    if (_presenceCache.TryGetValue(cacheKey, out var cached) ||
                        _presenceCache.TryGetValue($"{session.UserId}_default", out cached))
                    {
                        presence.CustomStatus = cached.CustomStatus;
                        if (cached.Status == PresenceStatus.DoNotDisturb || cached.Status == PresenceStatus.Away)
                        {
                            presence.Status = cached.Status;
                        }
                    }
                }

                presenceList.Add(presence);
            }

            return presenceList
                .GroupBy(p => p.UserId)
                .Select(g => g.OrderByDescending(p => p.LastSeen).First())
                .OrderBy(p => p.WarehouseName)
                .ThenBy(p => p.Status)
                .ThenBy(p => p.Username)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all online users");
            return new List<UserPresence>();
        }
    }

    public async Task UpdateUserPresenceAsync(int userId, string currentPage, string deviceType, CancellationToken cancellationToken = default)
    {
        try
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var session = await context.UserSessions
                .Where(s => s.UserId == userId && s.IsActive)
                .OrderByDescending(s => s.LastActivity)
                .FirstOrDefaultAsync(cancellationToken);

            if (session != null)
            {
                session.LastPageUrl = currentPage;
                session.DeviceType = deviceType;
                session.LastActivity = DateTime.UtcNow;
                await context.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating user presence for user {UserId}", userId);
        }
    }

    public Task SetCustomStatusAsync(int userId, PresenceStatus status, string? customMessage = null, CancellationToken cancellationToken = default)
    {
        try
        {
            lock (_lock)
            {
                var keysToUpdate = _presenceCache.Keys
                    .Where(k => k.StartsWith($"{userId}_"))
                    .ToList();

                foreach (var key in keysToUpdate)
                {
                    if (_presenceCache.TryGetValue(key, out var presence))
                    {
                        presence.Status = status;
                        presence.CustomStatus = customMessage;
                    }
                }

                if (!keysToUpdate.Any())
                {
                    _presenceCache[$"{userId}_default"] = new UserPresence
                    {
                        UserId = userId,
                        Status = status,
                        CustomStatus = customMessage,
                        LastSeen = DateTime.UtcNow
                    };
                }
            }

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting custom status for user {UserId}", userId);
            return Task.CompletedTask;
        }
    }

    public Task RemoveUserPresenceAsync(int userId, string sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            lock (_lock)
            {
                var cacheKey = $"{userId}_{sessionId}";
                _presenceCache.Remove(cacheKey);
            }

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing user presence for user {UserId}", userId);
            return Task.CompletedTask;
        }
    }

    public async Task<int> GetOnlineCountInWarehouseAsync(int warehouseId, CancellationToken cancellationToken = default)
    {
        try
        {
            var users = await GetOnlineUsersInWarehouseAsync(warehouseId);
            return users.Count(u => u.IsOnline);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting online count for warehouse {WarehouseId}", warehouseId);
            return 0;
        }
    }

    private PresenceStatus DeterminePresenceStatus(DateTime lastActivity)
    {
        var minutesSinceActivity = (DateTime.UtcNow - lastActivity).TotalMinutes;

        if (minutesSinceActivity < 5)
            return PresenceStatus.Online;
        else if (minutesSinceActivity < 15)
            return PresenceStatus.Idle;
        else
            return PresenceStatus.Away;
    }
}
