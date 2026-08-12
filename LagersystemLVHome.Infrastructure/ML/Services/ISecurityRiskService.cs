using LagersystemLVHome.Infrastructure.ML.Models;

namespace LagersystemLVHome.Infrastructure.ML.Services;

/// <summary>
/// Service for security risk scoring.
/// </summary>
public interface ISecurityRiskService
{
    /// <summary>
    /// Calculates the security risk for a user.
    /// </summary>
    Task<SecurityRiskAssessment> AssessUserRiskAsync(int userId, CancellationToken cancellationToken = default);

    Task<List<SecurityRiskAssessment>> GetHighRiskUsersAsync(CancellationToken cancellationToken = default);

    Task UpdateAllRiskScoresAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Trains the risk scoring model.
    /// </summary>
    Task<bool> TrainModelAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Indicates whether the model is ready for use.
    /// </summary>
    bool IsModelReady { get; }

    /// <summary>
    /// Calculates global system risk based on rate limiting threats.
    /// Used for the security threats dashboard page.
    /// </summary>
    Task<double> CalculateGlobalSystemRiskAsync(CancellationToken cancellationToken = default);
}
