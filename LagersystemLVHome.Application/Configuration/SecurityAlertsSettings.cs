namespace LagersystemLVHome.Application.Configuration;

/// <summary>
/// Security alert email configuration.
/// Configures which security events trigger email notifications.
/// </summary>
public class SecurityAlertsSettings
{
    /// <summary>
    /// Master switch for all security alerts.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Burst attack configuration.
    /// </summary>
    public BurstAttackSettings BurstAttack { get; set; } = new();

    /// <summary>
    /// Brute-force attack configuration.
    /// </summary>
    public BruteForceSettings BruteForce { get; set; } = new();

    /// <summary>
    /// DDoS pattern configuration.
    /// </summary>
    public DDoSSettings DDoS { get; set; } = new();

    /// <summary>
    /// Slow-rate attack configuration.
    /// </summary>
    public SlowRateSettings SlowRate { get; set; } = new();
}

public class BurstAttackSettings
{
    /// <summary>
    /// Enable email notification on burst attack.
    /// </summary>
    public bool EmailEnabled { get; set; } = true;

    /// <summary>
    /// Number of requests that trigger a burst attack alert.
    /// </summary>
    public int Threshold { get; set; } = 50;

    /// <summary>
    /// Time window in seconds.
    /// </summary>
    public int TimeWindowSeconds { get; set; } = 5;
}

public class BruteForceSettings
{
    /// <summary>
    /// Enable email notification on brute-force attack.
    /// </summary>
    public bool EmailEnabled { get; set; } = true;

    /// <summary>
    /// Number of failed login attempts.
    /// </summary>
    public int Threshold { get; set; } = 10;

    /// <summary>
    /// Time window in minutes.
    /// </summary>
    public int TimeWindowMinutes { get; set; } = 15;
}

public class DDoSSettings
{
    /// <summary>
    /// Enable email notification on DDoS pattern.
    /// </summary>
    public bool EmailEnabled { get; set; } = true;

    /// <summary>
    /// Minimum number of unique IPs.
    /// </summary>
    public int MinUniqueIPs { get; set; } = 20;

    /// <summary>
    /// Minimum number of total requests.
    /// </summary>
    public int MinTotalRequests { get; set; } = 1000;

    /// <summary>
    /// Time window in minutes.
    /// </summary>
    public int TimeWindowMinutes { get; set; } = 5;
}

public class SlowRateSettings
{
    /// <summary>
    /// Enable email notification on slow-rate attack.
    /// </summary>
    public bool EmailEnabled { get; set; } = false;

    /// <summary>
    /// Minimum number of active hours in 24h.
    /// </summary>
    public int MinActiveHours { get; set; } = 12;

    /// <summary>
    /// Minimum number of suspicious IPs.
    /// </summary>
    public int MinOffenders { get; set; } = 5;
}
