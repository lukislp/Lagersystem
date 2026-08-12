namespace LagersystemLVHome.Application.Configuration;

/// <summary>
/// Rate limiting configuration.
/// Configures different rate limit tiers for API endpoints.
/// </summary>
public class RateLimitSettings
{
    /// <summary>
    /// Enable/disable rate limiting globally.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Default rate limit for unauthenticated requests.
    /// </summary>
    public RateLimitPolicy Anonymous { get; set; } = new()
    {
        PermitLimit = 10,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0
    };

    /// <summary>
    /// Rate limit for authenticated users.
    /// </summary>
    public RateLimitPolicy Authenticated { get; set; } = new()
    {
        PermitLimit = 100,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 5
    };

    /// <summary>
    /// Rate limit for Admin/Manager.
    /// </summary>
    public RateLimitPolicy Admin { get; set; } = new()
    {
        PermitLimit = 500,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 10
    };

    /// <summary>
    /// Rate limit for SuperAdmin (effectively unlimited).
    /// </summary>
    public RateLimitPolicy SuperAdmin { get; set; } = new()
    {
        PermitLimit = 10000,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 20
    };

    /// <summary>
    /// Endpoint-specific rate limit overrides.
    /// Key = endpoint pattern (e.g. "/api/products/*").
    /// </summary>
    public Dictionary<string, RateLimitPolicy> EndpointOverrides { get; set; } = new()
    {
        ["/api/auth/login"] = new RateLimitPolicy
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(5),
            QueueLimit = 0
        },
        ["/api/auth/register"] = new RateLimitPolicy
        {
            PermitLimit = 3,
            Window = TimeSpan.FromMinutes(10),
            QueueLimit = 0
        },
        ["/api/sensors/*"] = new RateLimitPolicy
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 5
        }
    };

    /// <summary>
    /// IP whitelist (no rate limits applied).
    /// Empty = whitelist disabled (all IPs are rate limited).
    /// </summary>
    public List<string> WhitelistedIPs { get; set; } = new();

    /// <summary>
    /// IP blacklist (completely blocked).
    /// </summary>
    public List<string> BlacklistedIPs { get; set; } = new();

    /// <summary>
    /// HTTP status code returned on rate limit violation.
    /// </summary>
    public int StatusCode { get; set; } = 429;

    /// <summary>
    /// Retry-After header value in seconds.
    /// </summary>
    public int RetryAfterSeconds { get; set; } = 60;

    /// <summary>
    /// Enable logging on rate limit violations.
    /// </summary>
    public bool LogViolations { get; set; } = true;
}

/// <summary>
/// Rate limit policy definition.
/// </summary>
public class RateLimitPolicy
{
    /// <summary>
    /// Number of allowed requests.
    /// </summary>
    public int PermitLimit { get; set; }

    /// <summary>
    /// Time window for rate limiting.
    /// </summary>
    public TimeSpan Window { get; set; }

    /// <summary>
    /// Number of requests allowed in queue (0 = no queue).
    /// </summary>
    public int QueueLimit { get; set; }
}
