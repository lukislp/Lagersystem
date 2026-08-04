using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.Infrastructure.ML.Models;
using LagersystemLVHome.Application.Services;

namespace LagersystemLVHome.Infrastructure.ML.Services;

/// <summary>
/// Security risk service using rule-based scoring (no machine learning).
/// Calculates security risks based on user activities and behavior.
/// </summary>
public class SecurityRiskService : ISecurityRiskService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly ILogger<SecurityRiskService> _logger;
    private readonly IServiceProvider _serviceProvider;

    private const double CRITICAL_THRESHOLD = 75.0;
    private const double HIGH_THRESHOLD = 50.0;
    private const double MEDIUM_THRESHOLD = 25.0;

    public bool IsModelReady => true;

    public SecurityRiskService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        ILogger<SecurityRiskService> logger,
        IServiceProvider serviceProvider)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _serviceProvider = serviceProvider;

        _logger.LogInformation("Security Risk Service initialized (rule-based scoring + rate limiting integration)");
    }


    public async Task<SecurityRiskAssessment> AssessUserRiskAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        try
        {
            var user = await context.Users
                .Include(u => u.Warehouse)
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

            if (user == null)
                throw new ArgumentException($"User {userId} not found");

            var features = await CollectUserFeaturesAsync(context, userId, user);
            var (riskScore, riskFactors) = CalculateRiskScore(features, user);
            var riskLevel = DetermineRiskLevel(riskScore);
            var recommendations = GenerateRecommendations(riskLevel, features, user);

            var assessment = new SecurityRiskAssessment
            {
                UserId = userId,
                Username = user.Username,
                RiskLevel = riskLevel,
                RiskScore = riskScore,
                RiskFactors = riskFactors,
                Recommendations = recommendations,
                AssessedAt = DateTime.UtcNow,
                RequiresTwoFactor = !user.TwoFactorEnabled && riskScore >= 40,
                RequiresPasswordChange = user.LastPasswordChangeAt == null ||
                    (DateTime.UtcNow - user.LastPasswordChangeAt.Value).Days > 90,
                SuggestAccountReview = riskScore >= 60
            };

            _logger.LogInformation(
                "Risk assessment completed for user {UserId}: {RiskLevel} ({Score:F1})",
                userId, riskLevel, riskScore);

            return assessment;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assessing security risk for user {UserId}", userId);

            return new SecurityRiskAssessment
            {
                UserId = userId,
                Username = "Unknown",
                RiskLevel = RiskLevel.Low,
                RiskScore = 0,
                RiskFactors = new List<RiskFactor>(),
                Recommendations = new List<string>(),
                AssessedAt = DateTime.UtcNow
            };
        }
    }

    public async Task<List<SecurityRiskAssessment>> GetHighRiskUsersAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        try
        {
            var activeUsers = await context.Users
                .Where(u => u.IsActive && !u.IsDeleted)
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);

            var assessments = new List<SecurityRiskAssessment>();

            foreach (var userId in activeUsers)
            {
                var assessment = await AssessUserRiskAsync(userId);
                if (assessment.RiskLevel >= RiskLevel.High)
                {
                    assessments.Add(assessment);
                }
            }

            return assessments
                .OrderByDescending(a => a.RiskScore)
                .Take(10)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting high risk users");
            return new List<SecurityRiskAssessment>();
        }
    }

    public async Task UpdateAllRiskScoresAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        try
        {
            _logger.LogInformation("Updating risk scores for all active users...");

            var activeUsers = await context.Users
                .Where(u => u.IsActive && !u.IsDeleted)
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);

            var processedCount = 0;
            foreach (var userId in activeUsers)
            {
                await AssessUserRiskAsync(userId);
                processedCount++;

                if (processedCount % 10 == 0)
                {
                    _logger.LogInformation(
                        "Processed {Count}/{Total} users", processedCount, activeUsers.Count);
                }
            }

            _logger.LogInformation("Risk score update completed for {Count} users", activeUsers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating risk scores");
            throw;
        }
    }

    /// <summary>
    /// Rule-based scoring does not require training.
    /// </summary>
    public Task<bool> TrainModelAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Rule-based scoring does not require training");
        return Task.FromResult(true);
    }

    /// <summary>
    /// Global system risk assessment (for dashboard).
    /// Considers rate limiting threats from all users.
    /// </summary>
    public async Task<double> CalculateGlobalSystemRiskAsync(CancellationToken cancellationToken = default)
    {
        double globalScore = 0;
        var riskFactors = new List<string>();

        try
        {
            var rateLimitService = _serviceProvider.GetService<IRateLimitService>();
            if (rateLimitService == null)
            {
                _logger.LogWarning("RateLimitService not available for global risk calculation");
                return 10;
            }

            // 1. DDoS detection (max 35 points)
            var ddos = rateLimitService.DetectDDoS(TimeSpan.FromMinutes(5));
            if (ddos.IsDDoSPattern)
            {
                globalScore += 35;
                riskFactors.Add($"DDoS: {ddos.UniqueIPsInvolved} IPs, {ddos.TotalRequests} requests");
            }
            else if (ddos.TotalRequests > 500)
            {
                globalScore += 15;
                riskFactors.Add($"Erh\u00f6hte Request-Rate: {ddos.TotalRequests} requests");
            }

            // 2. Global request statistics (max 25 points)
            var globalStats = rateLimitService.GetGlobalStatistics();
            if (globalStats.BlockRate > 50)
            {
                globalScore += 25;
                riskFactors.Add($"Sehr hohe Block-Rate: {globalStats.BlockRate:F1}%");
            }
            else if (globalStats.BlockRate > 30)
            {
                globalScore += 15;
                riskFactors.Add($"Hohe Block-Rate: {globalStats.BlockRate:F1}%");
            }
            else if (globalStats.BlockRate > 10)
            {
                globalScore += 8;
                riskFactors.Add($"Erh\u00f6hte Block-Rate: {globalStats.BlockRate:F1}%");
            }

            // 3. Burst attacks (max 20 points)
            var recentRequests = rateLimitService.GetRecentRequests(100);
            var suspiciousIPs = recentRequests
                .GroupBy(r => r.Identifier)
                .Count(g => g.Count() > 20);

            if (suspiciousIPs > 10)
            {
                globalScore += 20;
                riskFactors.Add($"Viele Burst Attacks: {suspiciousIPs} verd\u00e4chtige IPs");
            }
            else if (suspiciousIPs > 5)
            {
                globalScore += 12;
                riskFactors.Add($"Burst Attacks: {suspiciousIPs} verd\u00e4chtige IPs");
            }
            else if (suspiciousIPs > 0)
            {
                globalScore += 6;
                riskFactors.Add($"Burst Activity: {suspiciousIPs} verd\u00e4chtige IPs");
            }

            // 4. Brute-force attempts (max 15 points)
            var failedLogins = recentRequests.Count(r => !r.IsSuccess);
            if (failedLogins > 50)
            {
                globalScore += 15;
                riskFactors.Add($"Viele fehlgeschlagene Logins: {failedLogins}");
            }
            else if (failedLogins > 20)
            {
                globalScore += 10;
                riskFactors.Add($"Fehlgeschlagene Logins: {failedLogins}");
            }

            // 5. Active buckets (performance indicator - max 5 points)
            if (globalStats.ActiveBuckets > 1000)
            {
                globalScore += 5;
                riskFactors.Add($"Sehr viele aktive Buckets: {globalStats.ActiveBuckets}");
            }
            else if (globalStats.ActiveBuckets > 500)
            {
                globalScore += 3;
                riskFactors.Add($"Viele aktive Buckets: {globalStats.ActiveBuckets}");
            }

            if (riskFactors.Any())
            {
                _logger.LogWarning(
                    "Global System Risk: {Score:F0} - Factors: {Factors}",
                    globalScore, string.Join(" | ", riskFactors));
            }
            else
            {
                _logger.LogInformation(
                    "Global System Risk: {Score:F0} - No threats detected", globalScore);
            }

            return Math.Min(globalScore, 100);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating global system risk");
            return 20;
        }
    }



    private async Task<SecurityRiskFeatures> CollectUserFeaturesAsync(
        InventoryDbContext context,
        int userId,
        User user, CancellationToken cancellationToken = default)
    {
        var accountAge = (DateTime.UtcNow - user.CreatedAt).Days;

        var auditLogs = await context.AuditLogs
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.Timestamp)
            .Take(1000)
            .ToListAsync(cancellationToken);

        var totalLogins = auditLogs.Count(l => l.Action == "LOGIN_SUCCESS");
        var failedLogins = auditLogs.Count(l => l.Action == "LOGIN_FAILED");
        var failedLoginRatio = totalLogins > 0 ? (float)failedLogins / totalLogins : 0;

        var sensitiveActions = auditLogs.Count(l =>
            l.Action.Contains("DELETE") ||
            l.Action.Contains("EXPORT") ||
            l.Action == "USER_DELETED");

        var dataExports = auditLogs.Count(l => l.Action.Contains("EXPORT"));
        var uniqueIps = auditLogs.Select(l => l.IpAddress).Distinct().Count();
        var nightActivity = auditLogs.Count(l => l.Timestamp.Hour >= 0 && l.Timestamp.Hour < 6);
        var unusualHourActivity = auditLogs.Any() ? (float)nightActivity / auditLogs.Count : 0;
        var passwordChanges = auditLogs.Count(l => l.Action == "PASSWORD_CHANGED");
        var passwordChangeFreq = accountAge > 0 ? (float)passwordChanges / (accountAge / 30f) : 0;
        var avgSessionDuration = CalculateAverageSessionDuration(auditLogs);

        return new SecurityRiskFeatures
        {
            TotalLogins = totalLogins,
            FailedLoginRatio = failedLoginRatio,
            SensitiveActionsCount = sensitiveActions,
            AccountAge = accountAge,
            TwoFactorEnabled = user.TwoFactorEnabled,
            UnusualHourActivity = unusualHourActivity,
            IpAddressVariety = uniqueIps,
            DataExportCount = dataExports,
            PasswordChangeFrequency = passwordChangeFreq,
            AverageSessionDuration = avgSessionDuration
        };
    }



    /// <summary>
    /// Calculates risk score based on weighted rules.
    /// </summary>
    private (double score, List<RiskFactor> factors) CalculateRiskScore(
        SecurityRiskFeatures features,
        User user)
    {
        double score = 0;
        var factors = new List<RiskFactor>();

        // Rate limiting threat detection (highest priority - max 40 points)
        try
        {
            var rateLimitService = _serviceProvider.GetService<IRateLimitService>();
            if (rateLimitService != null)
            {
                // DDoS detection (20 points)
                var ddos = rateLimitService.DetectDDoS(TimeSpan.FromMinutes(5));
                if (ddos.IsDDoSPattern)
                {
                    score += 20;
                    factors.Add(new RiskFactor
                    {
                        Factor = "DDoS Angriff erkannt",
                        Impact = 20,
                        Description = $"{ddos.UniqueIPsInvolved} IPs, {ddos.TotalRequests} Requests, \u00d8 {ddos.AverageRequestsPerIP:F0} req/IP"
                    });
                }

                // Burst attack detection for this user (15 points)
                var userIdentifier = $"ip:user_{user.Id}";
                var burst = rateLimitService.DetectBurstAttack(userIdentifier);
                if (burst.IsBurstAttack)
                {
                    score += 15;
                    factors.Add(new RiskFactor
                    {
                        Factor = "Burst Attack erkannt",
                        Impact = 15,
                        Description = $"{burst.RequestsInBurst} Requests in {burst.BurstDuration.TotalSeconds:F1}s ({burst.RequestsPerSecond:F0} req/s)"
                    });
                }

                // Brute-force detection (15 points)
                var bruteForce = rateLimitService.DetectBruteForce(userIdentifier);
                if (bruteForce.IsBruteForce)
                {
                    score += 15;
                    factors.Add(new RiskFactor
                    {
                        Factor = "Brute-Force Angriff erkannt",
                        Impact = 15,
                        Description = $"{bruteForce.FailedAttempts} fehlgeschlagene Versuche auf {string.Join(", ", bruteForce.TargetedEndpoints.Take(2))}"
                    });
                }

                // High request rate (10 points)
                var globalStats = rateLimitService.GetGlobalStatistics();
                if (globalStats.BlockedRequests > 100)
                {
                    var blockRatePenalty = Math.Min(10, globalStats.BlockRate / 10);
                    score += blockRatePenalty;
                    factors.Add(new RiskFactor
                    {
                        Factor = "Hohe Block-Rate",
                        Impact = blockRatePenalty,
                        Description = $"{globalStats.BlockedRequests} von {globalStats.TotalRequests} Requests blockiert ({globalStats.BlockRate:F1}%)"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to integrate rate limiting data into risk score");
        }

        // 1. Failed login ratio (max 30 points)
        if (features.FailedLoginRatio > 0.5)
        {
            score += 30;
            factors.Add(new RiskFactor
            {
                Factor = "Sehr hohe Fehlerquote bei Logins",
                Impact = 30,
                Description = $"{features.FailedLoginRatio:P0} der Login-Versuche sind fehlgeschlagen"
            });
        }
        else if (features.FailedLoginRatio > 0.3)
        {
            score += 20;
            factors.Add(new RiskFactor
            {
                Factor = "Hohe Fehlerquote bei Logins",
                Impact = 20,
                Description = $"{features.FailedLoginRatio:P0} der Login-Versuche sind fehlgeschlagen"
            });
        }
        else if (features.FailedLoginRatio > 0.2)
        {
            score += 15;
            factors.Add(new RiskFactor
            {
                Factor = "Erh\u00f6hte Fehlerquote bei Logins",
                Impact = 15,
                Description = $"{features.FailedLoginRatio:P0} der Login-Versuche sind fehlgeschlagen"
            });
        }

        // 2. Two-factor authentication (15 points)
        if (!features.TwoFactorEnabled)
        {
            score += 15;
            factors.Add(new RiskFactor
            {
                Factor = "2FA nicht aktiviert",
                Impact = 15,
                Description = "Zwei-Faktor-Authentifizierung ist deaktiviert"
            });
        }

        // 3. Sensitive actions (max 20 points)
        if (features.SensitiveActionsCount > 20)
        {
            score += 20;
            factors.Add(new RiskFactor
            {
                Factor = "Sehr viele sensible Aktionen",
                Impact = 20,
                Description = $"{features.SensitiveActionsCount} DELETE/EXPORT Aktionen"
            });
        }
        else if (features.SensitiveActionsCount > 10)
        {
            score += 12;
            factors.Add(new RiskFactor
            {
                Factor = "Viele sensible Aktionen",
                Impact = 12,
                Description = $"{features.SensitiveActionsCount} DELETE/EXPORT Aktionen"
            });
        }
        else if (features.SensitiveActionsCount > 5)
        {
            score += 6;
            factors.Add(new RiskFactor
            {
                Factor = "Sensible Aktionen",
                Impact = 6,
                Description = $"{features.SensitiveActionsCount} DELETE/EXPORT Aktionen"
            });
        }

        // 4. Unusual hour activity (max 15 points)
        if (features.UnusualHourActivity > 0.5)
        {
            score += 15;
            factors.Add(new RiskFactor
            {
                Factor = "H\u00e4ufige Aktivit\u00e4t zu ungew\u00f6hnlichen Zeiten",
                Impact = 15,
                Description = $"{features.UnusualHourActivity:P0} der Aktivit\u00e4ten zwischen 0-6 Uhr"
            });
        }
        else if (features.UnusualHourActivity > 0.3)
        {
            score += 10;
            factors.Add(new RiskFactor
            {
                Factor = "Aktivit\u00e4t zu ungew\u00f6hnlichen Zeiten",
                Impact = 10,
                Description = $"{features.UnusualHourActivity:P0} der Aktivit\u00e4ten zwischen 0-6 Uhr"
            });
        }

        // 5. IP address variety (max 10 points)
        if (features.IpAddressVariety > 20)
        {
            score += 10;
            factors.Add(new RiskFactor
            {
                Factor = "Sehr h\u00e4ufige IP-Wechsel",
                Impact = 10,
                Description = $"{features.IpAddressVariety} verschiedene IP-Adressen"
            });
        }
        else if (features.IpAddressVariety > 10)
        {
            score += 6;
            factors.Add(new RiskFactor
            {
                Factor = "H\u00e4ufige IP-Wechsel",
                Impact = 6,
                Description = $"{features.IpAddressVariety} verschiedene IP-Adressen"
            });
        }

        // 6. Data export count (max 15 points)
        if (features.DataExportCount > 20)
        {
            score += 15;
            factors.Add(new RiskFactor
            {
                Factor = "Sehr viele Daten-Exports",
                Impact = 15,
                Description = $"{features.DataExportCount} Exports durchgef\u00fchrt"
            });
        }
        else if (features.DataExportCount > 10)
        {
            score += 10;
            factors.Add(new RiskFactor
            {
                Factor = "Viele Daten-Exports",
                Impact = 10,
                Description = $"{features.DataExportCount} Exports durchgef\u00fchrt"
            });
        }
        else if (features.DataExportCount > 5)
        {
            score += 5;
            factors.Add(new RiskFactor
            {
                Factor = "Daten-Exports",
                Impact = 5,
                Description = $"{features.DataExportCount} Exports durchgef\u00fchrt"
            });
        }

        // 7. Account age (10 points)
        if (features.AccountAge < 7)
        {
            score += 10;
            factors.Add(new RiskFactor
            {
                Factor = "Sehr neuer Account",
                Impact = 10,
                Description = $"Account nur {features.AccountAge} Tage alt"
            });
        }
        else if (features.AccountAge < 30)
        {
            score += 5;
            factors.Add(new RiskFactor
            {
                Factor = "Neuer Account",
                Impact = 5,
                Description = $"Account nur {features.AccountAge} Tage alt"
            });
        }

        // 8. Password change frequency (5 points)
        if (features.PasswordChangeFrequency < 0.1 && features.AccountAge > 90)
        {
            score += 5;
            factors.Add(new RiskFactor
            {
                Factor = "Seltene Passwort-\u00c4nderungen",
                Impact = 5,
                Description = "Passwort wird selten ge\u00e4ndert"
            });
        }

        return (Math.Min(score, 100), factors.OrderByDescending(f => f.Impact).ToList());
    }

    private RiskLevel DetermineRiskLevel(double score)
    {
        return score switch
        {
            >= CRITICAL_THRESHOLD => RiskLevel.Critical,
            >= HIGH_THRESHOLD => RiskLevel.High,
            >= MEDIUM_THRESHOLD => RiskLevel.Medium,
            _ => RiskLevel.Low
        };
    }



    private List<string> GenerateRecommendations(
        RiskLevel level,
        SecurityRiskFeatures features,
        User user)
    {
        var recommendations = new List<string>();

        if (level >= RiskLevel.Critical)
        {
            recommendations.Add("Account sofort \u00fcberpr\u00fcfen und ggf. tempor\u00e4r sperren");
            recommendations.Add("Benutzer pers\u00f6nlich kontaktieren");
            recommendations.Add("Audit-Logs detailliert analysieren");
        }

        if (!features.TwoFactorEnabled)
        {
            recommendations.Add(level >= RiskLevel.High
                ? "Zwei-Faktor-Authentifizierung SOFORT erzwingen"
                : "Zwei-Faktor-Authentifizierung aktivieren empfohlen");
        }

        if (features.FailedLoginRatio > 0.3)
        {
            recommendations.Add("Passwort-Reset erzwingen");
            recommendations.Add("Benutzer \u00fcber verd\u00e4chtige Login-Versuche informieren");
        }
        else if (features.FailedLoginRatio > 0.2)
        {
            recommendations.Add("Passwort-Reset empfehlen");
        }

        if (level >= RiskLevel.High)
        {
            recommendations.Add("Erweiterte \u00dcberwachung aktivieren");
            recommendations.Add("Sicherheits-Schulung anbieten");
        }

        if (features.SensitiveActionsCount > 15)
        {
            recommendations.Add("Berechtigungen \u00fcberpr\u00fcfen und ggf. einschr\u00e4nken");
        }
        else if (features.SensitiveActionsCount > 10)
        {
            recommendations.Add("Berechtigungen \u00fcberpr\u00fcfen");
        }

        if (user.LastPasswordChangeAt == null ||
            (DateTime.UtcNow - user.LastPasswordChangeAt.Value).Days > 90)
        {
            recommendations.Add("Passwort-\u00c4nderung erzwingen (>90 Tage alt)");
        }

        if (features.UnusualHourActivity > 0.4)
        {
            recommendations.Add("Aktivit\u00e4tsmuster zu ungew\u00f6hnlichen Zeiten \u00fcberpr\u00fcfen");
        }

        if (features.IpAddressVariety > 15)
        {
            recommendations.Add("IP-Adressen-Muster analysieren");
        }

        if (features.DataExportCount > 15)
        {
            recommendations.Add("Daten-Export-Aktivit\u00e4ten \u00fcberpr\u00fcfen");
        }

        return recommendations;
    }



    private float CalculateAverageSessionDuration(List<AuditLog> logs)
    {
        if (!logs.Any())
            return 0;

        var sessions = new List<double>();
        DateTime? sessionStart = null;

        foreach (var log in logs.OrderBy(l => l.Timestamp))
        {
            if (log.Action == "LOGIN_SUCCESS")
            {
                sessionStart = log.Timestamp;
            }
            else if (log.Action == "LOGOUT" && sessionStart.HasValue)
            {
                sessions.Add((log.Timestamp - sessionStart.Value).TotalMinutes);
                sessionStart = null;
            }
        }

        return sessions.Any() ? (float)sessions.Average() : 30;
    }



    /// <summary>
    /// Collected user features for risk scoring.
    /// </summary>
    private class SecurityRiskFeatures
    {
        public int TotalLogins { get; set; }
        public float FailedLoginRatio { get; set; }
        public int SensitiveActionsCount { get; set; }
        public int AccountAge { get; set; }
        public bool TwoFactorEnabled { get; set; }
        public float UnusualHourActivity { get; set; }
        public int IpAddressVariety { get; set; }
        public int DataExportCount { get; set; }
        public float PasswordChangeFrequency { get; set; }
        public float AverageSessionDuration { get; set; }
    }

}
