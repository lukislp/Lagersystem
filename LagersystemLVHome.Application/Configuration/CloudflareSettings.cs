namespace LagersystemLVHome.Application.Configuration;

/// <summary>
/// Cloudflare integration settings (Free Plan features).
/// All features are optional and can be enabled individually.
/// </summary>
public class CloudflareSettings
{
    /// <summary>
    /// Master switch: enable/disable all Cloudflare features.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Cloudflare API token (optional, for API access).
    /// </summary>
    public string? ApiToken { get; set; }

    /// <summary>
    /// Cloudflare Zone ID (optional, for API access).
    /// </summary>
    public string? ZoneId { get; set; }

    /// <summary>
    /// Cloudflare Account ID (optional, for advanced features).
    /// </summary>
    public string? AccountId { get; set; }

    /// <summary>
    /// Bot protection: uses Cloudflare headers for bot detection.
    /// </summary>
    public BotProtectionSettings BotProtection { get; set; } = new();

    /// <summary>
    /// DDoS protection: Cloudflare-based DDoS filtering.
    /// </summary>
    public DDoSProtectionSettings DDoSProtection { get; set; } = new();

    /// <summary>
    /// IP geolocation: uses Cloudflare headers for geo data.
    /// </summary>
    public GeoLocationSettings GeoLocation { get; set; } = new();

    /// <summary>
    /// Rate limiting: Cloudflare-based rate limiting (in addition to local).
    /// </summary>
    public CloudflareRateLimitSettings RateLimiting { get; set; } = new();

    /// <summary>
    /// Security level: Cloudflare security level (Low, Medium, High, UnderAttack).
    /// </summary>
    public SecurityLevelSettings SecurityLevel { get; set; } = new();

    /// <summary>
    /// Challenge passage: automatic challenge handling.
    /// </summary>
    public ChallengeSettings Challenge { get; set; } = new();

    /// <summary>
    /// Performance and caching settings.
    /// </summary>
    public CloudflarePerformanceSettings Performance { get; set; } = new();

    /// <summary>
    /// Analytics settings.
    /// </summary>
    public AnalyticsSettings Analytics { get; set; } = new();

    /// <summary>
    /// Auto-escalation on attacks.
    /// </summary>
    public AutoEscalationSettings AutoEscalation { get; set; } = new();

    /// <summary>
    /// Page rules management.
    /// </summary>
    public PageRulesSettings PageRules { get; set; } = new();

    /// <summary>
    /// SSL/TLS settings.
    /// </summary>
    public SslTlsSettings SslTls { get; set; } = new();

    /// <summary>
    /// Firewall rules.
    /// </summary>
    public FirewallSettings Firewall { get; set; } = new();
}

/// <summary>
/// Bot protection via Cloudflare headers.
/// </summary>
public class BotProtectionSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Block requests without Cloudflare bot-score header.
    /// </summary>
    public bool RequireBotScoreHeader { get; set; } = false;

    /// <summary>
    /// Minimum bot score (0-99, low = likely bot). Default: 30.
    /// </summary>
    public int MinimumBotScore { get; set; } = 30;

    /// <summary>
    /// Bot score below this value is logged as suspicious.
    /// </summary>
    public int SuspiciousBotScoreThreshold { get; set; } = 50;

    /// <summary>
    /// Block known bad bots.
    /// </summary>
    public bool BlockKnownBadBots { get; set; } = true;

    /// <summary>
    /// Block scrapers.
    /// </summary>
    public bool BlockScrapers { get; set; } = false;

    /// <summary>
    /// Allowed bot user-agents (e.g. Googlebot, Bingbot).
    /// </summary>
    public List<string> AllowedBots { get; set; } = new()
    {
        "Googlebot",
        "Bingbot",
        "facebookexternalhit"
    };
}

/// <summary>
/// DDoS protection settings.
/// </summary>
public class DDoSProtectionSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Block requests with high threat score.
    /// </summary>
    public bool BlockHighThreatScore { get; set; } = true;

    /// <summary>
    /// Threat score threshold (0-100, higher = more dangerous).
    /// </summary>
    public int ThreatScoreThreshold { get; set; } = 10;

    /// <summary>
    /// Tighten rate limiting on DDoS detection.
    /// </summary>
    public bool EnhanceRateLimitingOnAttack { get; set; } = true;
}

/// <summary>
/// Geo-location via Cloudflare headers.
/// </summary>
public class GeoLocationSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Use Cloudflare GeoIP instead of MaxMind.
    /// </summary>
    public bool UseCloudflareGeoIP { get; set; } = true;

    /// <summary>
    /// Block requests from specific countries (ISO 3166-1 alpha-2 codes).
    /// </summary>
    public List<string> BlockedCountries { get; set; } = new();

    /// <summary>
    /// Allow only requests from specific countries (empty = all allowed).
    /// </summary>
    public List<string> AllowedCountries { get; set; } = new();

    /// <summary>
    /// Log all access from unusual countries.
    /// </summary>
    public bool LogUnusualCountries { get; set; } = true;
}

/// <summary>
/// Cloudflare rate limiting (in addition to local).
/// </summary>
public class CloudflareRateLimitSettings
{
    /// <summary>
    /// Optional since local rate limiting is already active.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Use Cloudflare rate limit headers.
    /// </summary>
    public bool UseCloudflareHeaders { get; set; } = false;

    /// <summary>
    /// Log rate limit violations.
    /// </summary>
    public bool LogViolations { get; set; } = true;
}

/// <summary>
/// Security level settings.
/// </summary>
public class SecurityLevelSettings
{
    /// <summary>
    /// Current security level: Off, EssentiallyOff, Low, Medium, High, UnderAttack.
    /// </summary>
    public CloudflareSecurityLevel Level { get; set; } = CloudflareSecurityLevel.Medium;

    /// <summary>
    /// Automatically switch to "Under Attack" on DDoS detection (requires API token).
    /// </summary>
    public bool AutoEscalateOnAttack { get; set; } = false;
}

/// <summary>
/// Challenge settings.
/// </summary>
public class ChallengeSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Challenge type: Managed, JavaScript, Interactive.
    /// </summary>
    public ChallengeType Type { get; set; } = ChallengeType.Managed;

    /// <summary>
    /// Validate challenge passage (checks CF-Challenge-Passed header).
    /// </summary>
    public bool ValidateChallengePassage { get; set; } = true;
}

/// <summary>
/// Cloudflare security levels.
/// </summary>
public enum CloudflareSecurityLevel
{
    Off,
    EssentiallyOff,
    Low,
    Medium,
    High,
    UnderAttack
}

/// <summary>
/// Challenge types.
/// </summary>
public enum ChallengeType
{
    /// <summary>
    /// Managed challenge (recommended, Free Plan).
    /// </summary>
    Managed,

    /// <summary>
    /// JavaScript challenge (legacy).
    /// </summary>
    JavaScript,

    /// <summary>
    /// Interactive challenge (CAPTCHA).
    /// </summary>
    Interactive
}

/// <summary>
/// Performance and caching settings.
/// </summary>
public class CloudflarePerformanceSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Auto-minify (HTML, CSS, JS).
    /// </summary>
    public AutoMinifySettings AutoMinify { get; set; } = new();

    /// <summary>
    /// Brotli compression.
    /// </summary>
    public bool EnableBrotli { get; set; } = true;

    /// <summary>
    /// Browser cache TTL (seconds). Default: 4 hours.
    /// </summary>
    public int BrowserCacheTtl { get; set; } = 14400;

    /// <summary>
    /// Cache level (Aggressive, Basic, Simplified).
    /// </summary>
    public string CacheLevel { get; set; } = "standard";

    /// <summary>
    /// Always Online (shows cached version on server outage).
    /// </summary>
    public bool AlwaysOnline { get; set; } = true;

    /// <summary>
    /// Development mode (bypass cache for testing).
    /// </summary>
    public bool DevelopmentMode { get; set; } = false;
}

public class AutoMinifySettings
{
    public bool Html { get; set; } = true;
    public bool Css { get; set; } = true;
    public bool Js { get; set; } = true;
}

/// <summary>
/// Analytics settings.
/// </summary>
public class AnalyticsSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Interval for automatic analytics update (minutes).
    /// </summary>
    public int UpdateIntervalMinutes { get; set; } = 15;

    /// <summary>
    /// Number of days for historical data.
    /// </summary>
    public int HistoricalDataDays { get; set; } = 30;

    /// <summary>
    /// Show threat statistics.
    /// </summary>
    public bool ShowThreats { get; set; } = true;

    /// <summary>
    /// Show cache metrics.
    /// </summary>
    public bool ShowCacheMetrics { get; set; } = true;

    /// <summary>
    /// Show bandwidth usage.
    /// </summary>
    public bool ShowBandwidth { get; set; } = true;
}

/// <summary>
/// Auto-escalation on attacks.
/// </summary>
public class AutoEscalationSettings
{
    /// <summary>
    /// Requires API token.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Automatically switch to "Under Attack" mode.
    /// </summary>
    public bool EnableUnderAttackMode { get; set; } = true;

    /// <summary>
    /// Threat score threshold for escalation.
    /// </summary>
    public int ThreatScoreThreshold { get; set; } = 50;

    /// <summary>
    /// Number of threats in time window for escalation.
    /// </summary>
    public int ThreatsCountThreshold { get; set; } = 100;

    /// <summary>
    /// Time window for threat counting (minutes).
    /// </summary>
    public int TimeWindowMinutes { get; set; } = 5;

    /// <summary>
    /// Automatically return to normal mode after X minutes.
    /// </summary>
    public int AutoDeEscalateAfterMinutes { get; set; } = 60;

    /// <summary>
    /// Send notification on escalation.
    /// </summary>
    public bool NotifyOnEscalation { get; set; } = true;
}

/// <summary>
/// Page rules settings.
/// </summary>
public class PageRulesSettings
{
    /// <summary>
    /// Requires API token.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Predefined page rules.
    /// </summary>
    public List<PageRule> Rules { get; set; } = new();
}

public class PageRule
{
    public string Pattern { get; set; } = string.Empty;
    public Dictionary<string, string> Settings { get; set; } = new();
    public int Priority { get; set; }
}

/// <summary>
/// SSL/TLS settings.
/// </summary>
public class SslTlsSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// SSL mode: Off, Flexible, Full, Full (Strict).
    /// </summary>
    public string Mode { get; set; } = "full";

    /// <summary>
    /// Always use HTTPS.
    /// </summary>
    public bool AlwaysUseHttps { get; set; } = true;

    /// <summary>
    /// Minimum TLS version (1.0, 1.1, 1.2, 1.3).
    /// </summary>
    public string MinTlsVersion { get; set; } = "1.2";

    /// <summary>
    /// Automatic HTTPS rewrites.
    /// </summary>
    public bool AutomaticHttpsRewrites { get; set; } = true;
}

/// <summary>
/// Firewall settings.
/// </summary>
public class FirewallSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Managed rules (Free Plan).
    /// </summary>
    public bool EnableManagedRules { get; set; } = true;

    /// <summary>
    /// Custom firewall rules (requires API token).
    /// </summary>
    public List<FirewallRule> CustomRules { get; set; } = new();
}

public class FirewallRule
{
    public string Expression { get; set; } = string.Empty;

    /// <summary>
    /// Action: block, challenge, js_challenge, managed_challenge.
    /// </summary>
    public string Action { get; set; } = "block";

    public string Description { get; set; } = string.Empty;
}
