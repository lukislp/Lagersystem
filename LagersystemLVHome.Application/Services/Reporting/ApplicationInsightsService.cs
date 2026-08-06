using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace LagersystemLVHome.Application.Services;

public sealed class ApplicationInsightsService : IApplicationInsightsService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly ILogger<ApplicationInsightsService> _logger;

    public ApplicationInsightsService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        ILogger<ApplicationInsightsService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }


    public async Task TrackPageViewAsync(PageView pageView, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        try
        {
            context.PageViews.Add(pageView);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error tracking page view");
        }
    }

    public async Task<List<PageView>> GetRecentPageViewsAsync(int count = 100, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.PageViews
            .Include(pv => pv.User)
            .OrderByDescending(pv => pv.Timestamp)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<string, int>> GetTopPagesAsync(DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.PageViews.AsQueryable();

        if (from.HasValue)
            query = query.Where(pv => pv.Timestamp >= from.Value);
        if (to.HasValue)
            query = query.Where(pv => pv.Timestamp <= to.Value);

        return await query
            .GroupBy(pv => pv.PageUrl)
            .Select(g => new { Page = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToDictionaryAsync(x => x.Page, x => x.Count);
    }

    public async Task<Dictionary<string, int>> GetPageViewsByUserAsync(DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.PageViews.AsQueryable();

        if (from.HasValue)
            query = query.Where(pv => pv.Timestamp >= from.Value);
        if (to.HasValue)
            query = query.Where(pv => pv.Timestamp <= to.Value);

        return await query
            .GroupBy(pv => pv.Username)
            .Select(g => new { Username = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToDictionaryAsync(x => x.Username, x => x.Count);
    }



    public async Task TrackApiRequestAsync(ApiRequest request, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        try
        {
            context.ApiRequests.Add(request);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error tracking API request");
        }
    }

    public async Task<List<ApiRequest>> GetRecentApiRequestsAsync(int count = 100, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.ApiRequests
            .OrderByDescending(r => r.Timestamp)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<string, int>> GetApiEndpointStatsAsync(DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.ApiRequests.AsQueryable();

        if (from.HasValue)
            query = query.Where(r => r.Timestamp >= from.Value);
        if (to.HasValue)
            query = query.Where(r => r.Timestamp <= to.Value);

        return await query
            .GroupBy(r => r.Endpoint)
            .Select(g => new { Endpoint = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToDictionaryAsync(x => x.Endpoint, x => x.Count);
    }

    public async Task<double> GetApiSuccessRateAsync(DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.ApiRequests.AsQueryable();

        if (from.HasValue)
            query = query.Where(r => r.Timestamp >= from.Value);
        if (to.HasValue)
            query = query.Where(r => r.Timestamp <= to.Value);

        var total = await query.CountAsync(cancellationToken);
        if (total == 0) return 100;

        var successful = await query.CountAsync(r => r.StatusCode >= 200 && r.StatusCode < 300, cancellationToken);
        return (double)successful / total * 100;
    }



    public async Task TrackPerformanceMetricAsync(PerformanceMetric metric, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        try
        {
            context.PerformanceMetrics.Add(metric);
            await context.SaveChangesAsync(cancellationToken);

            var cutoffDate = DateTime.UtcNow.AddDays(-7);
            var oldMetrics = await context.PerformanceMetrics
                .Where(m => m.Timestamp < cutoffDate)
                .ToListAsync(cancellationToken);

            if (oldMetrics.Any())
            {
                context.PerformanceMetrics.RemoveRange(oldMetrics);
                await context.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error tracking performance metric");
        }
    }

    public async Task<List<PerformanceMetric>> GetPerformanceHistoryAsync(int hours = 24, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var from = DateTime.UtcNow.AddHours(-hours);
        return await context.PerformanceMetrics
            .Where(m => m.Timestamp >= from)
            .OrderBy(m => m.Timestamp)
            .ToListAsync(cancellationToken);
    }

    public async Task<PerformanceMetric> GetCurrentPerformanceAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var process = Process.GetCurrentProcess();

        return new PerformanceMetric
        {
            CpuUsagePercent = GetCpuUsage(),
            MemoryUsedMB = process.WorkingSet64 / 1024 / 1024,
            // TotalAvailableMemoryBytes reflects the container's cgroup memory limit when
            // running in Docker (as this app typically does), which is more meaningful here
            // than raw host physical memory.
            MemoryTotalMB = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024,
            ActiveUsers = await GetActiveUserCountAsync(context),
            TotalRequests = await context.ApiRequests
                .Where(r => r.Timestamp >= DateTime.UtcNow.AddHours(-1))
                .CountAsync(cancellationToken),
            AvgResponseTimeMs = await context.ApiRequests
                .Where(r => r.Timestamp >= DateTime.UtcNow.AddHours(-1))
                .AverageAsync(r => (double?)r.DurationMs, cancellationToken) ?? 0
        };
    }

    private double GetCpuUsage()
    {
        try
        {
            var startTime = DateTime.UtcNow;
            var startCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;
            System.Threading.Thread.Sleep(100);
            var endTime = DateTime.UtcNow;
            var endCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;
            var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
            var totalMsPassed = (endTime - startTime).TotalMilliseconds;
            var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);
            return cpuUsageTotal * 100;
        }
        catch
        {
            return 0;
        }
    }

    private async Task<int> GetActiveUserCountAsync(InventoryDbContext context, CancellationToken cancellationToken = default)
    {
        var fiveMinutesAgo = DateTime.UtcNow.AddMinutes(-5);
        return await context.PageViews
            .Where(pv => pv.Timestamp >= fiveMinutesAgo)
            .Select(pv => pv.UserId)
            .Distinct()
            .CountAsync(cancellationToken);
    }



    public async Task TrackUserActivityAsync(UserActivity activity, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        try
        {
            context.UserActivities.Add(activity);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error tracking user activity");
        }
    }

    public async Task<List<UserActivity>> GetUserActivityAsync(int userId, int count = 50, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.UserActivities
            .Include(ua => ua.User)
            .Where(ua => ua.UserId == userId)
            .OrderByDescending(ua => ua.Timestamp)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<UserActivity>> GetRecentActivityAsync(int count = 50, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.UserActivities
            .Include(ua => ua.User)
            .OrderByDescending(ua => ua.Timestamp)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<string, int>> GetActivityTypeStatsAsync(DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.UserActivities.AsQueryable();

        if (from.HasValue)
            query = query.Where(ua => ua.Timestamp >= from.Value);
        if (to.HasValue)
            query = query.Where(ua => ua.Timestamp <= to.Value);

        return await query
            .GroupBy(ua => ua.ActivityType)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToDictionaryAsync(x => x.Type, x => x.Count);
    }



    public async Task<ApplicationInsightsStats> GetDashboardStatsAsync(DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var fromDate = from ?? DateTime.UtcNow.AddDays(-7);
        var toDate = to ?? DateTime.UtcNow;

        var stats = new ApplicationInsightsStats
        {
            TotalPageViews = await context.PageViews
                .Where(pv => pv.Timestamp >= fromDate && pv.Timestamp <= toDate)
                .CountAsync(cancellationToken),

            TotalApiRequests = await context.ApiRequests
                .Where(r => r.Timestamp >= fromDate && r.Timestamp <= toDate)
                .CountAsync(cancellationToken),

            TotalUsers = await context.Users.CountAsync(cancellationToken),

            ActiveUsers = await context.PageViews
                .Where(pv => pv.Timestamp >= DateTime.UtcNow.AddMinutes(-5))
                .Select(pv => pv.UserId)
                .Distinct()
                .CountAsync(cancellationToken),

            TotalSessions = await context.PageViews
                .Where(pv => pv.Timestamp >= fromDate && pv.Timestamp <= toDate)
                .Select(pv => pv.SessionId)
                .Distinct()
                .CountAsync(cancellationToken),

            AvgPageLoadTimeMs = await context.PageViews
                .Where(pv => pv.Timestamp >= fromDate && pv.Timestamp <= toDate && pv.LoadTimeMs.HasValue)
                .AverageAsync(pv => (double?)pv.LoadTimeMs, cancellationToken) ?? 0,

            AvgApiResponseTimeMs = await context.ApiRequests
                .Where(r => r.Timestamp >= fromDate && r.Timestamp <= toDate)
                .AverageAsync(r => (double?)r.DurationMs, cancellationToken) ?? 0,

            ApiSuccessRate = await GetApiSuccessRateAsync(fromDate, toDate),

            TopPages = (await GetTopPagesAsync(fromDate, toDate)).ToList(),
            TopUsers = (await GetPageViewsByUserAsync(fromDate, toDate)).ToList(),
            TopApiEndpoints = (await GetApiEndpointStatsAsync(fromDate, toDate)).ToList(),

            DeviceTypes = await GetDeviceTypeStatsAsync(fromDate, toDate),
            Browsers = await GetBrowserStatsAsync(fromDate, toDate),
            OperatingSystems = await GetOperatingSystemStatsAsync(fromDate, toDate),

            HourlyPageViews = await GetHourlyPageViewsAsync(fromDate, toDate),
            HourlyApiRequests = await GetHourlyApiRequestsAsync(fromDate, toDate),

            UniqueVisitors = await GetUniqueVisitorsAsync(fromDate, toDate),
            AvgSessionDuration = await GetAvgSessionDurationAsync(fromDate, toDate),
            BounceRate = await GetBounceRateAsync(fromDate, toDate),
            TopReferrers = await GetTopReferrersAsync(fromDate, toDate),
            TopCountries = await GetTopCountriesAsync(fromDate, toDate),
            TopCities = await GetTopCitiesAsync(fromDate, toDate),
            PeakHours = await GetPeakHoursAsync(fromDate, toDate),
            ErrorRate = await GetErrorRateAsync(fromDate, toDate),
            SlowPages = await GetSlowestPagesAsync(fromDate, toDate),
            FastestPages = await GetFastestPagesAsync(fromDate, toDate),
            MostUsedFeatures = await GetMostUsedFeaturesAsync(fromDate, toDate),
            UserRetention = await GetUserRetentionAsync(fromDate, toDate),
            NewVsReturningUsers = await GetNewVsReturningUsersAsync(fromDate, toDate),
            TopErrorPages = await GetTopErrorPagesAsync(fromDate, toDate),
            ApiEndpointPerformance = await GetApiEndpointPerformanceAsync(fromDate, toDate),
            WarehouseActivity = await GetWarehouseActivityAsync(fromDate, toDate),
            RoleActivity = await GetRoleActivityAsync(fromDate, toDate)
        };

        return stats;
    }

    private async Task<int> GetUniqueVisitorsAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.PageViews
            .Where(pv => pv.Timestamp >= from && pv.Timestamp <= to)
            .Select(pv => pv.IpAddress)
            .Distinct()
            .CountAsync(cancellationToken);
    }

    private async Task<double> GetAvgSessionDurationAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var sessions = await context.PageViews
            .Where(pv => pv.Timestamp >= from && pv.Timestamp <= to)
            .GroupBy(pv => pv.SessionId)
            .Select(g => new
            {
                Duration = (g.Max(pv => pv.Timestamp) - g.Min(pv => pv.Timestamp)).TotalMinutes
            })
            .ToListAsync(cancellationToken);

        return sessions.Any() ? sessions.Average(s => s.Duration) : 0;
    }

    private async Task<double> GetBounceRateAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var totalSessions = await context.PageViews
            .Where(pv => pv.Timestamp >= from && pv.Timestamp <= to)
            .Select(pv => pv.SessionId)
            .Distinct()
            .CountAsync(cancellationToken);

        if (totalSessions == 0) return 0;

        var singlePageSessions = await context.PageViews
            .Where(pv => pv.Timestamp >= from && pv.Timestamp <= to)
            .GroupBy(pv => pv.SessionId)
            .Where(g => g.Count() == 1)
            .CountAsync(cancellationToken);

        return (double)singlePageSessions / totalSessions * 100;
    }

    private async Task<List<KeyValuePair<string, int>>> GetTopReferrersAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.PageViews
            .Where(pv => pv.Timestamp >= from && pv.Timestamp <= to && !string.IsNullOrEmpty(pv.Referrer))
            .GroupBy(pv => pv.Referrer)
            .Select(g => new { Referrer = g.Key!, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .Select(x => new KeyValuePair<string, int>(x.Referrer, x.Count))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<KeyValuePair<string, int>>> GetTopCountriesAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.PageViews
            .Where(pv => pv.Timestamp >= from && pv.Timestamp <= to && !string.IsNullOrEmpty(pv.Country))
            .GroupBy(pv => pv.Country)
            .Select(g => new { Country = g.Key!, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .Select(x => new KeyValuePair<string, int>(x.Country, x.Count))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<KeyValuePair<string, int>>> GetTopCitiesAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.PageViews
            .Where(pv => pv.Timestamp >= from && pv.Timestamp <= to && !string.IsNullOrEmpty(pv.City))
            .GroupBy(pv => pv.City)
            .Select(g => new { City = g.Key!, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .Select(x => new KeyValuePair<string, int>(x.City, x.Count))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<KeyValuePair<string, int>>> GetPeakHoursAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var pageViews = await context.PageViews
            .Where(pv => pv.Timestamp >= from && pv.Timestamp <= to)
            .Select(pv => new { pv.Timestamp })
            .ToListAsync(cancellationToken);

        return pageViews
            .GroupBy(pv => pv.Timestamp.Hour)
            .Select(g => new KeyValuePair<string, int>($"{g.Key:D2}:00", g.Count()))
            .OrderByDescending(x => x.Value)
            .Take(5)
            .ToList();
    }

    private async Task<double> GetErrorRateAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var totalRequests = await context.ApiRequests
            .Where(r => r.Timestamp >= from && r.Timestamp <= to)
            .CountAsync(cancellationToken);

        if (totalRequests == 0) return 0;

        var errorRequests = await context.ApiRequests
            .Where(r => r.Timestamp >= from && r.Timestamp <= to && r.IsError)
            .CountAsync(cancellationToken);

        return (double)errorRequests / totalRequests * 100;
    }

    private async Task<List<KeyValuePair<string, double>>> GetSlowestPagesAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.PageViews
            .Where(pv => pv.Timestamp >= from && pv.Timestamp <= to && pv.LoadTimeMs.HasValue)
            .GroupBy(pv => pv.PageUrl)
            .Select(g => new { Page = g.Key, AvgLoadTime = g.Average(pv => pv.LoadTimeMs!.Value) })
            .OrderByDescending(x => x.AvgLoadTime)
            .Take(10)
            .Select(x => new KeyValuePair<string, double>(x.Page, x.AvgLoadTime))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<KeyValuePair<string, double>>> GetFastestPagesAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.PageViews
            .Where(pv => pv.Timestamp >= from && pv.Timestamp <= to && pv.LoadTimeMs.HasValue)
            .GroupBy(pv => pv.PageUrl)
            .Select(g => new { Page = g.Key, AvgLoadTime = g.Average(pv => pv.LoadTimeMs!.Value) })
            .OrderBy(x => x.AvgLoadTime)
            .Take(10)
            .Select(x => new KeyValuePair<string, double>(x.Page, x.AvgLoadTime))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<KeyValuePair<string, int>>> GetMostUsedFeaturesAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var features = new Dictionary<string, string>
        {
            ["/scanner"] = "Scanner",
            ["/products"] = "Products Management",
            ["/categories"] = "Categories",
            ["/movements"] = "Stock Movements",
            ["/low-stock"] = "Low Stock Alerts",
            ["/expiry-monitoring"] = "Expiry Monitoring",
            ["/ml-test-dashboard"] = "ML Dashboard",
            ["/security-center"] = "Security Center",
            ["/admin"] = "Admin Panel"
        };

        var pageViews = await context.PageViews
            .Where(pv => pv.Timestamp >= from && pv.Timestamp <= to)
            .GroupBy(pv => pv.PageUrl)
            .Select(g => new { Page = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return pageViews
            .Where(pv => features.ContainsKey(pv.Page))
            .Select(pv => new KeyValuePair<string, int>(features[pv.Page], pv.Count))
            .OrderByDescending(x => x.Value)
            .ToList();
    }

    private async Task<double> GetUserRetentionAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var firstPeriodUsers = await context.PageViews
            .Where(pv => pv.Timestamp >= from && pv.Timestamp < from.AddDays((to - from).Days / 2))
            .Select(pv => pv.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (!firstPeriodUsers.Any()) return 0;

        var returningUsers = await context.PageViews
            .Where(pv => pv.Timestamp >= from.AddDays((to - from).Days / 2) && pv.Timestamp <= to)
            .Where(pv => firstPeriodUsers.Contains(pv.UserId))
            .Select(pv => pv.UserId)
            .Distinct()
            .CountAsync(cancellationToken);

        return (double)returningUsers / firstPeriodUsers.Count * 100;
    }

    private async Task<Dictionary<string, int>> GetNewVsReturningUsersAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var allUsers = await context.PageViews
            .Where(pv => pv.Timestamp >= from && pv.Timestamp <= to)
            .Select(pv => pv.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var returningUsers = await context.PageViews
            .Where(pv => pv.Timestamp < from)
            .Select(pv => pv.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var newUsers = allUsers.Except(returningUsers).Count();
        var returning = allUsers.Intersect(returningUsers).Count();

        return new Dictionary<string, int>
        {
            ["New Users"] = newUsers,
            ["Returning Users"] = returning
        };
    }

    private async Task<List<KeyValuePair<string, int>>> GetTopErrorPagesAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.ApiRequests
            .Where(r => r.Timestamp >= from && r.Timestamp <= to && r.IsError)
            .GroupBy(r => r.Endpoint)
            .Select(g => new { Endpoint = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .Select(x => new KeyValuePair<string, int>(x.Endpoint, x.Count))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<KeyValuePair<string, double>>> GetApiEndpointPerformanceAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.ApiRequests
            .Where(r => r.Timestamp >= from && r.Timestamp <= to)
            .GroupBy(r => r.Endpoint)
            .Select(g => new { Endpoint = g.Key, AvgDuration = g.Average(r => r.DurationMs) })
            .OrderByDescending(x => x.AvgDuration)
            .Take(10)
            .Select(x => new KeyValuePair<string, double>(x.Endpoint, x.AvgDuration))
            .ToListAsync(cancellationToken);
    }

    private async Task<Dictionary<string, int>> GetWarehouseActivityAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.PageViews
            .Where(pv => pv.Timestamp >= from && pv.Timestamp <= to && pv.WarehouseName != null)
            .GroupBy(pv => pv.WarehouseName)
            .Select(g => new { Warehouse = g.Key!, Count = g.Count() })
            .ToDictionaryAsync(x => x.Warehouse, x => x.Count);
    }

    private async Task<Dictionary<string, int>> GetRoleActivityAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.PageViews
            .Where(pv => pv.Timestamp >= from && pv.Timestamp <= to)
            .GroupBy(pv => pv.UserRole)
            .Select(g => new { Role = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Role, x => x.Count);
    }

    private async Task<Dictionary<string, int>> GetOperatingSystemStatsAsync(DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.PageViews.AsQueryable();

        if (from.HasValue)
            query = query.Where(pv => pv.Timestamp >= from.Value);
        if (to.HasValue)
            query = query.Where(pv => pv.Timestamp <= to.Value);

        return await query
            .Where(pv => pv.OperatingSystem != null)
            .GroupBy(pv => pv.OperatingSystem!)
            .Select(g => new { OS = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToDictionaryAsync(x => x.OS, x => x.Count);
    }

    public async Task<List<UserSessionInfo>> GetActiveSessionsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var fiveMinutesAgo = DateTime.UtcNow.AddMinutes(-5);

        return await context.PageViews
            .Where(pv => pv.Timestamp >= fiveMinutesAgo)
            .GroupBy(pv => pv.SessionId)
            .Select(g => new UserSessionInfo
            {
                SessionId = g.Key,
                UserId = g.First().UserId,
                Username = g.First().Username,
                StartTime = g.Min(pv => pv.Timestamp),
                LastActivity = g.Max(pv => pv.Timestamp),
                PageViews = g.Count(),
                CurrentPage = g.OrderByDescending(pv => pv.Timestamp).First().PageUrl,
                DeviceType = g.First().DeviceType ?? "Unknown"
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<string, int>> GetDeviceTypeStatsAsync(DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.PageViews.AsQueryable();

        if (from.HasValue)
            query = query.Where(pv => pv.Timestamp >= from.Value);
        if (to.HasValue)
            query = query.Where(pv => pv.Timestamp <= to.Value);

        return await query
            .Where(pv => pv.DeviceType != null)
            .GroupBy(pv => pv.DeviceType!)
            .Select(g => new { Device = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Device, x => x.Count);
    }

    public async Task<Dictionary<string, int>> GetBrowserStatsAsync(DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.PageViews.AsQueryable();

        if (from.HasValue)
            query = query.Where(pv => pv.Timestamp >= from.Value);
        if (to.HasValue)
            query = query.Where(pv => pv.Timestamp <= to.Value);

        return await query
            .Where(pv => pv.Browser != null)
            .GroupBy(pv => pv.Browser!)
            .Select(g => new { Browser = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToDictionaryAsync(x => x.Browser, x => x.Count);
    }

    private async Task<List<HourlyStats>> GetHourlyPageViewsAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Client-side evaluation for complex DateTime operations
        var pageViews = await context.PageViews
            .Where(pv => pv.Timestamp >= from && pv.Timestamp <= to)
            .Select(pv => new { pv.Timestamp })
            .ToListAsync(cancellationToken);

        _logger.LogInformation("GetHourlyPageViewsAsync: {Count} PageViews loaded from {From} to {To}",
            pageViews.Count, from.ToString("yyyy-MM-dd HH:mm:ss"), to.ToString("yyyy-MM-dd HH:mm:ss"));

        if (pageViews.Any())
        {
            var firstTimestamp = pageViews.Min(pv => pv.Timestamp);
            var lastTimestamp = pageViews.Max(pv => pv.Timestamp);
            _logger.LogInformation("  First entry: {First}, Last entry: {Last}",
                firstTimestamp.ToString("yyyy-MM-dd HH:mm:ss"), lastTimestamp.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        // Create dictionary with all hours in the time range (local time)
        var hourlyData = new Dictionary<DateTime, int>();

        var localFrom = from.ToLocalTime();
        var localTo = to.ToLocalTime();
        var startHour = new DateTime(localFrom.Year, localFrom.Month, localFrom.Day, localFrom.Hour, 0, 0);
        var endHour = new DateTime(localTo.Year, localTo.Month, localTo.Day, localTo.Hour, 0, 0);

        for (var hour = startHour; hour <= endHour; hour = hour.AddHours(1))
        {
            hourlyData[hour] = 0;
        }

        // Fill in actual counts (convert to local time)
        foreach (var pv in pageViews)
        {
            var localHour = new DateTime(
                pv.Timestamp.ToLocalTime().Year,
                pv.Timestamp.ToLocalTime().Month,
                pv.Timestamp.ToLocalTime().Day,
                pv.Timestamp.ToLocalTime().Hour,
                0, 0
            );

            if (hourlyData.ContainsKey(localHour))
            {
                hourlyData[localHour]++;
            }
        }

        var hourlyStats = hourlyData
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => new HourlyStats
            {
                Hour = kvp.Key,
                Count = kvp.Value
            })
            .ToList();

        _logger.LogInformation("Hourly stats: {Count} hours grouped (including empty hours)", hourlyStats.Count);
        foreach (var stat in hourlyStats.Take(3))
        {
            _logger.LogInformation("    {Hour}: {Count} views", stat.Hour.ToString("yyyy-MM-dd HH:mm"), stat.Count);
        }
        if (hourlyStats.Count > 3)
        {
            _logger.LogInformation("    ... and {More} more hours", hourlyStats.Count - 3);
            var last = hourlyStats.Last();
            _logger.LogInformation("    Last hour: {Hour}: {Count} views", last.Hour.ToString("yyyy-MM-dd HH:mm"), last.Count);
        }

        return hourlyStats;
    }

    private async Task<List<HourlyStats>> GetHourlyApiRequestsAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Client-side evaluation for complex DateTime operations
        var requests = await context.ApiRequests
            .Where(r => r.Timestamp >= from && r.Timestamp <= to)
            .Select(r => new { r.Timestamp })
            .ToListAsync(cancellationToken);

        _logger.LogInformation("GetHourlyApiRequestsAsync: {Count} ApiRequests loaded from {From} to {To}",
            requests.Count, from.ToString("yyyy-MM-dd HH:mm:ss"), to.ToString("yyyy-MM-dd HH:mm:ss"));

        var hourlyData = new Dictionary<DateTime, int>();

        var localFrom = from.ToLocalTime();
        var localTo = to.ToLocalTime();
        var startHour = new DateTime(localFrom.Year, localFrom.Month, localFrom.Day, localFrom.Hour, 0, 0);
        var endHour = new DateTime(localTo.Year, localTo.Month, localTo.Day, localTo.Hour, 0, 0);

        for (var hour = startHour; hour <= endHour; hour = hour.AddHours(1))
        {
            hourlyData[hour] = 0;
        }

        foreach (var req in requests)
        {
            var localHour = new DateTime(
                req.Timestamp.ToLocalTime().Year,
                req.Timestamp.ToLocalTime().Month,
                req.Timestamp.ToLocalTime().Day,
                req.Timestamp.ToLocalTime().Hour,
                0, 0
            );

            if (hourlyData.ContainsKey(localHour))
            {
                hourlyData[localHour]++;
            }
        }

        return hourlyData
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => new HourlyStats
            {
                Hour = kvp.Key,
                Count = kvp.Value
            })
            .ToList();
    }

}
