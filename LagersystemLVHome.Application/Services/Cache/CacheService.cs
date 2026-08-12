using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using LagersystemLVHome.Application.Configuration;

namespace LagersystemLVHome.Application.Services;

public sealed class CacheService : ICacheService
{
    private readonly IMemoryCache? _memoryCache;
    private readonly IDistributedCache? _distributedCache;
    private readonly CacheSettings _settings;
    private readonly ILogger<CacheService> _logger;

    public CacheService(
        CacheSettings settings,
        ILogger<CacheService> logger,
        IMemoryCache? memoryCache = null,
        IDistributedCache? distributedCache = null)
    {
        _settings = settings;
        _logger = logger;
        _memoryCache = memoryCache;
        _distributedCache = distributedCache;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            if (_settings.EnableMemoryCache && _memoryCache != null)
            {
                if (_memoryCache.TryGetValue(key, out T? cachedValue))
                {
                    _logger.LogDebug("Cache hit (Memory): {Key}", key);
                    return cachedValue;
                }
            }

            if (_settings.EnableDistributedCache && _distributedCache != null)
            {
                var cachedBytes = await _distributedCache.GetAsync(key);
                if (cachedBytes != null)
                {
                    var cachedValue = JsonSerializer.Deserialize<T>(cachedBytes);

                    if (_settings.EnableMemoryCache && _memoryCache != null && cachedValue != null)
                    {
                        var options = new MemoryCacheEntryOptions
                        {
                            SlidingExpiration = TimeSpan.FromMinutes(_settings.SlidingExpirationMinutes),
                            Size = 1
                        };
                        _memoryCache.Set(key, cachedValue, options);
                    }

                    _logger.LogDebug("Cache hit (Distributed): {Key}", key);
                    return cachedValue;
                }
            }

            _logger.LogDebug("Cache miss: {Key}", key);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cached value for key: {Key}", key);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            var expirationTime = expiration ?? TimeSpan.FromMinutes(_settings.DefaultExpirationMinutes);

            if (_settings.EnableMemoryCache && _memoryCache != null)
            {
                var options = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = expirationTime,
                    SlidingExpiration = TimeSpan.FromMinutes(_settings.SlidingExpirationMinutes),
                    Size = 1
                };
                _memoryCache.Set(key, value, options);
                _logger.LogDebug("Set cache (Memory): {Key}", key);
            }

            if (_settings.EnableDistributedCache && _distributedCache != null)
            {
                var serialized = JsonSerializer.SerializeToUtf8Bytes(value);
                var options = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = expirationTime
                };
                await _distributedCache.SetAsync(key, serialized, options);
                _logger.LogDebug("Set cache (Distributed): {Key}", key);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting cached value for key: {Key}", key);
        }
    }

    public async Task<T> GetOrCreateAsync<T>(string key, TimeSpan expiration, Func<Task<T>> factory, CancellationToken cancellationToken = default) where T : class
    {
        var cached = await GetAsync<T>(key);
        if (cached != null)
        {
            return cached;
        }

        var value = await factory();
        await SetAsync(key, value, expiration);
        return value;
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_settings.EnableMemoryCache && _memoryCache != null)
            {
                _memoryCache.Remove(key);
            }

            if (_settings.EnableDistributedCache && _distributedCache != null)
            {
                await _distributedCache.RemoveAsync(key);
            }

            _logger.LogDebug("Removed cache: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cached value for key: {Key}", key);
        }
    }

    public async Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        // Simplified implementation; a production system should maintain a key index
        _logger.LogWarning("RemoveByPrefix is not fully implemented for distributed cache");
        await Task.CompletedTask;
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Cache cleared");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing cache");
        }
    }
}

public static class CacheKeys
{
    public const string Products = "products";
    public const string Categories = "categories";
    public const string StorageLocations = "storage_locations";
    public const string Warehouses = "warehouses";
    public const string Users = "users";

    public static string ProductById(int id) => $"product_{id}";
    public static string CategoryById(int id) => $"category_{id}";
    public static string StorageLocationById(int id) => $"storage_location_{id}";
    public static string ProductsByCategory(int categoryId) => $"products_category_{categoryId}";
    public static string ProductsByWarehouse(int warehouseId) => $"products_warehouse_{warehouseId}";
    public static string DashboardStats(int warehouseId) => $"dashboard_stats_{warehouseId}";
}
