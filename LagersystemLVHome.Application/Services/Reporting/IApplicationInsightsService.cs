using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services;

public interface IApplicationInsightsService
{
    // Page Views
    Task TrackPageViewAsync(PageView pageView, CancellationToken cancellationToken = default);
    Task<List<PageView>> GetRecentPageViewsAsync(int count = 100, CancellationToken cancellationToken = default);
    Task<Dictionary<string, int>> GetTopPagesAsync(DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default);
    Task<Dictionary<string, int>> GetPageViewsByUserAsync(DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default);

    // API Requests
    Task TrackApiRequestAsync(ApiRequest request, CancellationToken cancellationToken = default);
    Task<List<ApiRequest>> GetRecentApiRequestsAsync(int count = 100, CancellationToken cancellationToken = default);
    Task<Dictionary<string, int>> GetApiEndpointStatsAsync(DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default);
    Task<double> GetApiSuccessRateAsync(DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default);

    // Performance Metrics
    Task TrackPerformanceMetricAsync(PerformanceMetric metric, CancellationToken cancellationToken = default);
    Task<List<PerformanceMetric>> GetPerformanceHistoryAsync(int hours = 24, CancellationToken cancellationToken = default);
    Task<PerformanceMetric> GetCurrentPerformanceAsync(CancellationToken cancellationToken = default);

    // User Activity
    Task TrackUserActivityAsync(UserActivity activity, CancellationToken cancellationToken = default);
    Task<List<UserActivity>> GetUserActivityAsync(int userId, int count = 50, CancellationToken cancellationToken = default);
    Task<List<UserActivity>> GetRecentActivityAsync(int count = 50, CancellationToken cancellationToken = default);
    Task<Dictionary<string, int>> GetActivityTypeStatsAsync(DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default);

    // Analytics
    Task<ApplicationInsightsStats> GetDashboardStatsAsync(DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default);
    Task<List<UserSessionInfo>> GetActiveSessionsAsync(CancellationToken cancellationToken = default);
    Task<Dictionary<string, int>> GetDeviceTypeStatsAsync(DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default);
    Task<Dictionary<string, int>> GetBrowserStatsAsync(DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default);
}

public sealed class ApplicationInsightsStats
{
    // Overall
    public int TotalPageViews { get; set; }
    public int TotalApiRequests { get; set; }
    public int TotalUsers { get; set; }
    public int ActiveUsers { get; set; }
    public int TotalSessions { get; set; }

    // Performance
    public double AvgPageLoadTimeMs { get; set; }
    public double AvgApiResponseTimeMs { get; set; }
    public double ApiSuccessRate { get; set; }
    public double CacheHitRatio { get; set; }

    // Top Items
    public List<KeyValuePair<string, int>> TopPages { get; set; } = new();
    public List<KeyValuePair<string, int>> TopUsers { get; set; } = new();
    public List<KeyValuePair<string, int>> TopApiEndpoints { get; set; } = new();

    // Device Stats
    public Dictionary<string, int> DeviceTypes { get; set; } = new();
    public Dictionary<string, int> Browsers { get; set; } = new();
    public Dictionary<string, int> OperatingSystems { get; set; } = new();

    // Time-based
    public List<HourlyStats> HourlyPageViews { get; set; } = new();
    public List<HourlyStats> HourlyApiRequests { get; set; } = new();

    // Advanced Metrics
    public int UniqueVisitors { get; set; }
    public double AvgSessionDuration { get; set; }
    public double BounceRate { get; set; }
    public List<KeyValuePair<string, int>> TopReferrers { get; set; } = new();
    public List<KeyValuePair<string, int>> TopCountries { get; set; } = new();
    public List<KeyValuePair<string, int>> TopCities { get; set; } = new();
    public List<KeyValuePair<string, int>> PeakHours { get; set; } = new();
    public double ErrorRate { get; set; }
    public List<KeyValuePair<string, double>> SlowPages { get; set; } = new();
    public List<KeyValuePair<string, double>> FastestPages { get; set; } = new();
    public List<KeyValuePair<string, int>> MostUsedFeatures { get; set; } = new();
    public double UserRetention { get; set; }
    public Dictionary<string, int> NewVsReturningUsers { get; set; } = new();
    public List<KeyValuePair<string, int>> TopErrorPages { get; set; } = new();
    public List<KeyValuePair<string, double>> ApiEndpointPerformance { get; set; } = new();
    public Dictionary<string, int> WarehouseActivity { get; set; } = new();
    public Dictionary<string, int> RoleActivity { get; set; } = new();
}

public sealed class HourlyStats
{
    public DateTime Hour { get; set; }
    public int Count { get; set; }
}

public sealed class UserSessionInfo
{
    public string SessionId { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime LastActivity { get; set; }
    public int PageViews { get; set; }
    public string CurrentPage { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
}
