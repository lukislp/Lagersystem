namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Service for Microsoft Teams notifications.
/// </summary>
public interface ITeamsService
{
    /// <summary>
    /// Sends a simple message to Teams.
    /// </summary>
    Task<bool> SendMessageAsync(string title, string message, string? themeColor = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a low-stock alert to Teams.
    /// </summary>
    Task<bool> SendLowStockAlertAsync(string productName, int currentStock, int minStock, string? warehouseName = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an expiry alert to Teams.
    /// </summary>
    Task<bool> SendExpiryAlertAsync(string productName, DateTime expiryDate, int quantity, string? location = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an anomaly alert to Teams.
    /// </summary>
    Task<bool> SendAnomalyAlertAsync(string anomalyType, double score, string description, string? affectedEntity = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a security risk alert to Teams.
    /// </summary>
    Task<bool> SendSecurityRiskAlertAsync(string username, string riskLevel, double riskScore, List<string> riskFactors, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a system alert to Teams.
    /// </summary>
    Task<bool> SendSystemAlertAsync(string title, string message, string severity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an adaptive card to Teams (extended formatting).
    /// </summary>
    Task<bool> SendAdaptiveCardAsync(object adaptiveCard, CancellationToken cancellationToken = default);

    bool IsEnabled();

    /// <summary>
    /// Checks whether Teams notifications are enabled for a specific type.
    /// </summary>
    bool IsEnabledForType(string notificationType);
}
