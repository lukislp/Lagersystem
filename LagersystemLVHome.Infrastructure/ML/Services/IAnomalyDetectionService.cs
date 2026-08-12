using LagersystemLVHome.Infrastructure.ML.Models;

namespace LagersystemLVHome.Infrastructure.ML.Services;

/// <summary>
/// Service for anomaly detection in the audit system.
/// </summary>
public interface IAnomalyDetectionService
{
    /// <summary>
    /// Analyzes user behavior and detects anomalies.
    /// </summary>
    Task<AnomalyAnalysisResult> AnalyzeUserBehaviorAsync(int userId, DateTime? from = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for anomalies across all user activities.
    /// </summary>
    Task<List<AnomalyAnalysisResult>> DetectAnomaliesAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Trains the ML model with current audit data.
    /// </summary>
    Task<bool> TrainModelAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Indicates whether the model is trained and ready for use.
    /// </summary>
    bool IsModelReady { get; }

    /// <summary>
    /// Date of the last training.
    /// </summary>
    DateTime? LastTrainingDate { get; }
}
