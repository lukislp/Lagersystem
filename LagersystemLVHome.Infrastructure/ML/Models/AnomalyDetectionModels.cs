using Microsoft.ML.Data;

namespace LagersystemLVHome.Infrastructure.ML.Models;

/// <summary>
/// Input for anomaly detection based on audit log data.
/// </summary>
public class AuditBehaviorInput
{
    [LoadColumn(0)]
    public float HourOfDay { get; set; }

    [LoadColumn(1)]
    public float DayOfWeek { get; set; }

    [LoadColumn(2)]
    public float ActionCount { get; set; }

    [LoadColumn(3)]
    public float FailedLoginCount { get; set; }

    [LoadColumn(4)]
    public float UniqueIpCount { get; set; }

    [LoadColumn(5)]
    public float TimeSinceLastAction { get; set; }

    [LoadColumn(6)]
    public float ActionDiversity { get; set; }

    [LoadColumn(7)]
    public float IpChangeFrequency { get; set; }

    [LoadColumn(8)]
    public float SensitiveActionCount { get; set; }
}

/// <summary>
/// Output of the anomaly detection.
/// </summary>
public class AnomalyDetectionOutput
{
    [VectorType(9)]
    public float[] Features { get; set; } = Array.Empty<float>();

    [ColumnName("PredictedLabel")]
    public bool IsAnomaly { get; set; }

    [ColumnName("Score")]
    public float AnomalyScore { get; set; }
}

/// <summary>
/// Risk level for anomaly detection.
/// </summary>
public enum AnomalyRiskLevel
{
    Normal = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

/// <summary>
/// Result of the anomaly analysis for UI display.
/// </summary>
public class AnomalyAnalysisResult
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public double AnomalyScore { get; set; }
    public bool IsHighRisk { get; set; }
    public AnomalyRiskLevel RiskLevel { get; set; }
    public List<string> DetectedPatterns { get; set; } = new();
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
    public string RecommendedAction { get; set; } = string.Empty;
}
