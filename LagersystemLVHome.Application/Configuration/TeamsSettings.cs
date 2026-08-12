namespace LagersystemLVHome.Application.Configuration;

/// <summary>
/// Microsoft Teams notification settings.
/// </summary>
public class TeamsSettings
{
    /// <summary>
    /// Enable/disable Teams notifications globally.
    /// </summary>
    public bool EnableTeams { get; set; } = false;

    /// <summary>
    /// Teams Webhook URL (Incoming Webhook Connector).
    /// </summary>
    public string WebhookUrl { get; set; } = string.Empty;

    /// <summary>
    /// Enable Teams notifications for low stock alerts.
    /// </summary>
    public bool EnableForLowStock { get; set; } = true;

    /// <summary>
    /// Enable Teams notifications for expiry alerts.
    /// </summary>
    public bool EnableForExpiry { get; set; } = true;

    /// <summary>
    /// Enable Teams notifications for anomalies.
    /// </summary>
    public bool EnableForAnomalies { get; set; } = true;

    /// <summary>
    /// Enable Teams notifications for security risks.
    /// </summary>
    public bool EnableForSecurityRisks { get; set; } = true;

    /// <summary>
    /// Enable Teams notifications for system alerts.
    /// </summary>
    public bool EnableForSystemAlerts { get; set; } = true;

    /// <summary>
    /// Users to mention in Teams messages (e.g., "john.doe@company.com").
    /// </summary>
    public List<string> MentionUsers { get; set; } = new();

    /// <summary>
    /// Theme color for Teams messages (hex without #).
    /// </summary>
    public string ThemeColor { get; set; } = "0078D4";

    /// <summary>
    /// Include action buttons in Teams messages.
    /// </summary>
    public bool IncludeActionButtons { get; set; } = true;

    /// <summary>
    /// Max retry attempts for failed Teams messages.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Timeout in seconds for Teams webhook calls.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Notification channels configuration per alert type.
/// </summary>
public class NotificationChannels
{
    public ChannelConfig LowStockAlerts { get; set; } = new();
    public ChannelConfig ExpiryAlerts { get; set; } = new();
    public ChannelConfig SecurityAlerts { get; set; } = new();
    public ChannelConfig SystemAlerts { get; set; } = new();
    public ChannelConfig WeeklyReports { get; set; } = new();
    public ChannelConfig PasswordReset { get; set; } = new();
}

/// <summary>
/// Channel configuration for a specific alert type.
/// </summary>
public class ChannelConfig
{
    /// <summary>
    /// Send notification via email.
    /// </summary>
    public bool Email { get; set; } = true;

    /// <summary>
    /// Send notification via Teams.
    /// </summary>
    public bool Teams { get; set; } = false;

    /// <summary>
    /// Send notification as in-app notification.
    /// </summary>
    public bool InApp { get; set; } = true;
}
