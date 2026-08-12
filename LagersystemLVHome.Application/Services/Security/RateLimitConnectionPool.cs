using System.Collections.Concurrent;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Connection pool for RateLimitService.
/// Prevents thread blocking under high load via async queue.
/// </summary>
public sealed class RateLimitConnectionPool : IDisposable
{
    private readonly ConcurrentQueue<TaskCompletionSource<bool>> _waitQueue = new();
    private readonly SemaphoreSlim _semaphore;
    private readonly ILogger<RateLimitConnectionPool> _logger;
    private int _activeConnections = 0;
    private readonly int _maxConnections;

    private readonly SemaphoreSlim _prioritySemaphore;
    private readonly int _reservedForWeb;

    public RateLimitConnectionPool(int maxConnections, ILogger<RateLimitConnectionPool> logger)
    {
        _maxConnections = maxConnections;
        _reservedForWeb = Math.Max(10, maxConnections / 5); // 20% reserved for web
        _semaphore = new SemaphoreSlim(maxConnections, maxConnections);
        _prioritySemaphore = new SemaphoreSlim(_reservedForWeb, _reservedForWeb);
        _logger = logger;

        _logger.LogInformation("Connection pool initialized with {Max} max connections ({Reserved} reserved for web)",
            maxConnections, _reservedForWeb);
    }

    /// <summary>
    /// Waits for an available connection with priority for web requests.
    /// </summary>
    public async Task<IDisposable> AcquireAsync(TimeSpan timeout, bool isWebRequest = false, CancellationToken cancellationToken = default)
    {
        // Web requests have priority (reserved slots)
        if (isWebRequest)
        {
            var acquired = await _prioritySemaphore.WaitAsync(timeout);

            if (!acquired)
            {
                _logger.LogWarning("Priority pool timeout for web request");
                throw new TimeoutException("Priority connection pool exhausted");
            }

            Interlocked.Increment(ref _activeConnections);
            return new PriorityConnectionLease(this);
        }

        // API requests use normal slots
        var normalAcquired = await _semaphore.WaitAsync(timeout);

        if (!normalAcquired)
        {
            _logger.LogWarning("Connection pool timeout after {Timeout}ms", timeout.TotalMilliseconds);
            throw new TimeoutException("Connection pool exhausted");
        }

        Interlocked.Increment(ref _activeConnections);
        return new ConnectionLease(this);
    }

    private void Release(bool isPriority = false)
    {
        Interlocked.Decrement(ref _activeConnections);

        if (isPriority)
            _prioritySemaphore.Release();
        else
            _semaphore.Release();
    }

    public int ActiveConnections => _activeConnections;
    public int AvailableConnections => _maxConnections - _activeConnections;
    public int AvailablePriorityConnections => _reservedForWeb - (_maxConnections - _semaphore.CurrentCount);

    private class ConnectionLease : IDisposable
    {
        private readonly RateLimitConnectionPool _pool;
        private bool _disposed;

        public ConnectionLease(RateLimitConnectionPool pool)
        {
            _pool = pool;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _pool.Release(isPriority: false);
                _disposed = true;
            }
        }
    }

    private class PriorityConnectionLease : IDisposable
    {
        private readonly RateLimitConnectionPool _pool;
        private bool _disposed;

        public PriorityConnectionLease(RateLimitConnectionPool pool)
        {
            _pool = pool;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _pool.Release(isPriority: true);
                _disposed = true;
            }
        }
    }

    public void Dispose()
    {
        _semaphore?.Dispose();
        _prioritySemaphore?.Dispose();
    }
}
