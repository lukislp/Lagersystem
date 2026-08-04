using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services;

public interface IApiKeyService
{
    /// <returns>Tuple containing the clear key (shown once) and the ApiKey entity.</returns>
    Task<(string clearKey, ApiKey apiKey)> GenerateApiKeyAsync(int userId, string name, string[]? permissions = null, DateTime? expiresAt = null, CancellationToken cancellationToken = default);

    Task<User?> ValidateApiKeyAsync(string apiKey, CancellationToken cancellationToken = default);

    Task<ApiKey?> GetApiKeyByKeyAsync(string apiKey, CancellationToken cancellationToken = default);

    Task<List<ApiKey>> GetUserApiKeysAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes an API key.
    /// </summary>
    Task<bool> RevokeApiKeyAsync(int apiKeyId, int userId, CancellationToken cancellationToken = default);

    Task UpdateLastUsedAsync(int apiKeyId, CancellationToken cancellationToken = default);
}
