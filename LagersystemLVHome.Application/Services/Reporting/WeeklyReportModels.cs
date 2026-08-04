namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Models for weekly reports.
/// </summary>
public sealed class WeeklyReportData
{
    public DateTime ReportStart { get; set; }
    public DateTime ReportEnd { get; set; }
    public ApplicationInsightsReportData InsightsData { get; set; } = new();
    public SecurityCenterReportData SecurityData { get; set; } = new();
    public SecurityThreatsReportData SecurityThreats { get; set; } = new();
}

public sealed class ApplicationInsightsReportData
{
    // KPIs
    public int TotalPageViews { get; set; }
    public int TotalApiRequests { get; set; }
    public int ActiveUsers { get; set; }
    public int UniqueVisitors { get; set; }
    public double AvgSessionDurationMinutes { get; set; }
    public double BounceRatePercent { get; set; }
    public double ErrorRatePercent { get; set; }

    // Performance
    public double AvgPageLoadTimeMs { get; set; }
    public double AvgApiResponseTimeMs { get; set; }
    public double ApiSuccessRatePercent { get; set; }

    // Top Lists
    public List<KeyValuePair<string, int>> TopPages { get; set; } = new();
    public List<KeyValuePair<string, int>> TopUsers { get; set; } = new();
    public List<KeyValuePair<string, int>> TopApiEndpoints { get; set; } = new();
    public List<KeyValuePair<string, double>> SlowestPages { get; set; } = new();
    public List<KeyValuePair<string, double>> FastestPages { get; set; } = new();
    public List<KeyValuePair<string, int>> MostUsedFeatures { get; set; } = new();

    // Devices and Browsers
    public Dictionary<string, int> DeviceTypes { get; set; } = new();
    public Dictionary<string, int> Browsers { get; set; } = new();
    public Dictionary<string, int> OperatingSystems { get; set; } = new();

    // Geography and Traffic
    public List<KeyValuePair<string, int>> TopCountries { get; set; } = new();
    public List<KeyValuePair<string, int>> TopReferrers { get; set; } = new();
    public List<KeyValuePair<string, int>> PeakHours { get; set; } = new();

    // User Engagement
    public double UserRetention { get; set; }
    public Dictionary<string, int> NewVsReturningUsers { get; set; } = new();
    public Dictionary<string, int> RoleActivity { get; set; } = new();
    public Dictionary<string, int> WarehouseActivity { get; set; } = new();

    // Errors
    public List<KeyValuePair<string, int>> TopErrorPages { get; set; } = new();
    public List<KeyValuePair<string, double>> ApiEndpointPerformance { get; set; } = new();

    // Trends (Daily)
    public List<DailyStats> DailyPageViews { get; set; } = new();
    public List<DailyStats> DailyApiRequests { get; set; } = new();
}

public sealed class SecurityCenterReportData
{
    // Security Overview
    public int TotalAnomalies { get; set; }
    public int CriticalAnomalies { get; set; }
    public int HighRiskUsersCount { get; set; }
    public int TotalSecurityEvents { get; set; }

    // Risk Breakdown
    public int LowRiskCount { get; set; }
    public int MediumRiskCount { get; set; }
    public int HighRiskCount { get; set; }
    public int CriticalRiskCount { get; set; }

    // Top Security Issues
    public List<AnomalyReportItem> TopAnomalies { get; set; } = new();
    public List<SecurityRiskReportItem> HighRiskUsersList { get; set; } = new();

    // Audit Logs Summary
    public int TotalAuditLogs { get; set; }
    public Dictionary<string, int> ActionTypes { get; set; } = new();
    public List<KeyValuePair<string, int>> TopAuditedUsers { get; set; } = new();

    // Failed Actions
    public int FailedLoginAttempts { get; set; }
    public int UnauthorizedAccessAttempts { get; set; }
}

public sealed class SecurityRiskReportItem
{
    public string Username { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = string.Empty;
    public double RiskScore { get; set; }
    public List<string> RiskFactors { get; set; } = new();
}

/// <summary>
/// Security threats report data (in-memory from RateLimitService).
/// </summary>
public sealed class SecurityThreatsReportData
{
    public double GlobalRiskScore { get; set; }
    public int TotalThreats { get; set; }

    public List<ThreatIncident> BurstAttacks { get; set; } = new();
    public List<ThreatIncident> BruteForceAttacks { get; set; } = new();
    public List<ThreatIncident> DDoSPatterns { get; set; } = new();
    public List<ThreatIncident> SlowRateAttacks { get; set; } = new();
}

/// <summary>
/// Single security threat incident.
/// </summary>
public sealed class ThreatIncident
{
    public DateTime Timestamp { get; set; }
    public string Identifier { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;

    // Burst Attack
    public int RequestCount { get; set; }
    public double DurationSeconds { get; set; }
    public double RequestsPerSecond { get; set; }

    // Brute-Force
    public int FailedAttempts { get; set; }
    public double DurationMinutes { get; set; }
    public List<string> TargetedEndpoints { get; set; } = new();

    // DDoS
    public int UniqueIPs { get; set; }
    public int TotalRequests { get; set; }
    public double AverageRequestsPerIP { get; set; }
    public List<string> SuspiciousIPs { get; set; } = new();

    // Slow-Rate
    public int SuspiciousPatternCount { get; set; }
    public List<string> ConsistentOffenders { get; set; } = new();
}

public sealed class DailyStats
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
}

public sealed class AnomalyReportItem
{
    public string Type { get; set; } = string.Empty;
    public double Score { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime DetectedAt { get; set; }
}
