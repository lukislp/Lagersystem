using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services;

public interface ITeamPresenceService
{
    Task<List<UserPresence>> GetOnlineUsersInWarehouseAsync(int warehouseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all online users across all warehouses (for SuperAdmin).
    /// </summary>
    Task<List<UserPresence>> GetAllOnlineUsersAsync(CancellationToken cancellationToken = default);

    Task UpdateUserPresenceAsync(int userId, string currentPage, string deviceType, CancellationToken cancellationToken = default);

    Task SetCustomStatusAsync(int userId, PresenceStatus status, string? customMessage = null, CancellationToken cancellationToken = default);

    Task RemoveUserPresenceAsync(int userId, string sessionId, CancellationToken cancellationToken = default);

    Task<int> GetOnlineCountInWarehouseAsync(int warehouseId, CancellationToken cancellationToken = default);
}
