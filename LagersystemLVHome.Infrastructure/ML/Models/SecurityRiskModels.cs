using Microsoft.ML.Data;

namespace LagersystemLVHome.Infrastructure.ML.Models;

/// <summary>
/// Input for security risk scoring.
/// </summary>
public class SecurityRiskInput
{
    [LoadColumn(0)]
    public float TotalLogins { get; set; }

    [LoadColumn(1)]
    public float FailedLoginRatio { get; set; }

    [LoadColumn(2)]
    public float SensitiveActionsCount { get; set; }

    [LoadColumn(3)]
    public float AccountAge { get; set; }

    [LoadColumn(4)]
    public float TwoFactorEnabled { get; set; }

    [LoadColumn(5)]
    public float UnusualHourActivity { get; set; }

    [LoadColumn(6)]
    public float IpAddressVariety { get; set; }

    [LoadColumn(7)]
    public float DataExportCount { get; set; }

    [LoadColumn(8)]
    public float PasswordChangeFrequency { get; set; }

    [LoadColumn(9)]
    public float AverageSessionDuration { get; set; }
}

/// <summary>
/// Output of the security risk scoring.
/// </summary>
public class SecurityRiskOutput
{
    [ColumnName("PredictedLabel")]
    public uint RiskLevel { get; set; }

    [ColumnName("Score")]
    public float[] Probabilities { get; set; } = Array.Empty<float>();
}

/// <summary>
/// Detailed security risk assessment.
/// </summary>
public class SecurityRiskAssessment
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public RiskLevel RiskLevel { get; set; }
    public double RiskScore { get; set; }
    public List<RiskFactor> RiskFactors { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
    public DateTime AssessedAt { get; set; } = DateTime.UtcNow;
    public bool RequiresTwoFactor { get; set; }
    public bool RequiresPasswordChange { get; set; }
    public bool SuggestAccountReview { get; set; }
}

public enum RiskLevel
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

public class RiskFactor
{
    public string Factor { get; set; } = string.Empty;
    public double Impact { get; set; }
    public string Description { get; set; } = string.Empty;
}
