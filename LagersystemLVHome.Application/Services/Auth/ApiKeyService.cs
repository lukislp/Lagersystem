using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services;

public sealed class ApiKeyService : IApiKeyService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly ILogger<ApiKeyService> _logger;
    private readonly IAuditService _auditService;

    public ApiKeyService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        ILogger<ApiKeyService> logger,
        IAuditService auditService)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _auditService = auditService;
    }

    public async Task<(string clearKey, ApiKey apiKey)> GenerateApiKeyAsync(
        int userId,
        string name,
        string[]? permissions = null,
        DateTime? expiresAt = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            // Generate a secure 32-byte API key
            var keyBytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(keyBytes);
            }

            // Convert to URL-safe Base64
            var clearKey = Convert.ToBase64String(keyBytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .Replace("=", "");

            var keyHash = HashApiKey(clearKey);

            var apiKey = new ApiKey
            {
                UserId = userId,
                Name = name,
                KeyHash = keyHash,
                KeyPrefix = clearKey[..8],
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                ExpiresAt = expiresAt,
                Permissions = permissions != null ? string.Join(",", permissions) : null
            };

            context.ApiKeys.Add(apiKey);
            await context.SaveChangesAsync(cancellationToken);

            await _auditService.LogAsync(
                "API_KEY_CREATED",
                "ApiKey",
                apiKey.Id,
                new { Name = name, KeyPrefix = apiKey.KeyPrefix },
                AuditSeverity.Info);

            _logger.LogInformation("API-Key created for user {UserId} with name '{Name}'", userId, name);

            return (clearKey, apiKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating API key for user {UserId}", userId);
            throw;
        }
    }

    public async Task<User?> ValidateApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var keyHash = HashApiKey(apiKey);

            var apiKeyRecord = await context.ApiKeys
                .Include(ak => ak.User)
                .ThenInclude(u => u.Warehouse)
                .FirstOrDefaultAsync(ak => ak.KeyHash == keyHash && ak.IsActive, cancellationToken);

            if (apiKeyRecord == null)
            {
                _logger.LogWarning("Invalid API key attempt");
                return null;
            }

            if (apiKeyRecord.ExpiresAt.HasValue && apiKeyRecord.ExpiresAt.Value < DateTime.UtcNow)
            {
                _logger.LogWarning("Expired API key used: {KeyId}", apiKeyRecord.Id);
                return null;
            }

            _ = UpdateLastUsedAsync(apiKeyRecord.Id);

            _logger.LogInformation("Valid API key used: {KeyId} for user {UserId}",
                apiKeyRecord.Id, apiKeyRecord.UserId);

            return apiKeyRecord.User;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating API key");
            return null;
        }
    }

    public async Task<ApiKey?> GetApiKeyByKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var keyHash = HashApiKey(apiKey);

            return await context.ApiKeys
                .FirstOrDefaultAsync(ak => ak.KeyHash == keyHash && ak.IsActive, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting API key by key");
            return null;
        }
    }

    public async Task<List<ApiKey>> GetUserApiKeysAsync(int userId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await context.ApiKeys
                .Where(ak => ak.UserId == userId)
                .OrderByDescending(ak => ak.CreatedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting API keys for user {UserId}", userId);
            return [];
        }
    }

    public async Task<bool> RevokeApiKeyAsync(int apiKeyId, int userId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var apiKey = await context.ApiKeys
                .FirstOrDefaultAsync(ak => ak.Id == apiKeyId && ak.UserId == userId, cancellationToken);

            if (apiKey == null)
            {
                _logger.LogWarning("API key {ApiKeyId} not found or not owned by user {UserId}",
                    apiKeyId, userId);
                return false;
            }

            apiKey.IsActive = false;
            await context.SaveChangesAsync(cancellationToken);

            await _auditService.LogAsync(
                "API_KEY_REVOKED",
                "ApiKey",
                apiKeyId,
                new { Name = apiKey.Name },
                AuditSeverity.Warning);

            _logger.LogInformation("API key {ApiKeyId} revoked by user {UserId}", apiKeyId, userId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking API key {ApiKeyId}", apiKeyId);
            return false;
        }
    }

    public async Task UpdateLastUsedAsync(int apiKeyId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var apiKey = await context.ApiKeys.FindAsync(apiKeyId);
            if (apiKey != null)
            {
                apiKey.LastUsedAt = DateTime.UtcNow;
                await context.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error updating LastUsedAt for API key {ApiKeyId}", apiKeyId);
        }
    }

    private static string HashApiKey(string apiKey)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexString(hashBytes).ToLower();
    }
}
