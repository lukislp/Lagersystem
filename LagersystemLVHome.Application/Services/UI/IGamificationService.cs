namespace LagersystemLVHome.Application.Services;

public interface IGamificationService
{
    Task RecordActionAsync(int userId, string action, string? details = null, CancellationToken cancellationToken = default);
    Task MigrateFromAuditLogsAsync(int userId, CancellationToken cancellationToken = default);
    Task<UserGamificationProfile> GetUserProfileAsync(int userId, CancellationToken cancellationToken = default);
    Task<List<UserLeaderboardEntry>> GetLeaderboardAsync(int? warehouseId, CancellationToken cancellationToken = default);
    Task<List<Achievement>> GetAchievementsAsync(int userId, CancellationToken cancellationToken = default);
    Task<UserStreakInfo> GetStreakInfoAsync(int userId, CancellationToken cancellationToken = default);
}
