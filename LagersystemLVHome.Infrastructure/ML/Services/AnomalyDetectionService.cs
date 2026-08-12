using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.Infrastructure.ML.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms;

namespace LagersystemLVHome.Infrastructure.ML.Services;

/// <summary>
/// Anomaly detection implementation using ML.NET and the factory pattern.
/// </summary>
public class AnomalyDetectionService : IAnomalyDetectionService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly ILogger<AnomalyDetectionService> _logger;
    private readonly string _modelPath;
    private readonly MLContext _mlContext;
    private ITransformer? _trainedModel;
    private PredictionEngine<AuditBehaviorInput, AnomalyDetectionOutput>? _predictionEngine;

    public bool IsModelReady => _trainedModel != null;
    public DateTime? LastTrainingDate { get; private set; }

    public AnomalyDetectionService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        ILogger<AnomalyDetectionService> logger,
        IWebHostEnvironment env)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _mlContext = new MLContext(seed: 1);
        _modelPath = Path.Combine(env.ContentRootPath, "ML", "Data", "anomaly-detection-model.zip");

        LoadModelIfExists();
    }

    public async Task<AnomalyAnalysisResult> AnalyzeUserBehaviorAsync(int userId, DateTime? from = null, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!IsModelReady)
            {
                _logger.LogWarning("Anomaly detection model not ready. Training required.");
                return new AnomalyAnalysisResult
                {
                    UserId = userId,
                    Username = "Unknown",
                    AnomalyScore = 0,
                    DetectedPatterns = new() { "Modell noch nicht trainiert" },
                    RecommendedAction = "Bitte ML-Modell trainieren"
                };
            }

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var startDate = from ?? DateTime.UtcNow.AddDays(-7);
            var user = await context.Users.FindAsync(userId);

            if (user == null)
                throw new ArgumentException($"User {userId} not found");

            var auditLogs = await context.AuditLogs
                .Where(a => a.UserId == userId && a.Timestamp >= startDate)
                .OrderBy(a => a.Timestamp)
                .ToListAsync(cancellationToken);

            _logger.LogInformation(
                "Analyzing user {UserId} ({Username}): Found {Count} logs from {FromDate}",
                userId, user.Username, auditLogs.Count, startDate);

            if (!auditLogs.Any())
            {
                _logger.LogWarning("No audit logs found for user {UserId}", userId);
                return new AnomalyAnalysisResult
                {
                    UserId = userId,
                    Username = user.Username,
                    AnomalyScore = 0,
                    DetectedPatterns = new() { "Keine Aktivit\u00e4ten im Zeitraum" },
                    RecommendedAction = "Keine Analyse m\u00f6glich - keine Daten"
                };
            }

            // Feature extraction
            var features = ExtractFeatures(auditLogs);

            // Calculate rule-based score first (for debugging)
            var ruleBasedScore = CalculateRuleBasedAnomalyScore(features, auditLogs);
            _logger.LogInformation("Rule-based score for user {UserId}: {Score}", userId, ruleBasedScore);

            // ML model prediction (if available)
            double mlScore = 0;
            try
            {
                var prediction = _predictionEngine!.Predict(features);
                mlScore = Math.Min(Math.Abs(prediction.AnomalyScore) * 100, 100);

                if (double.IsNaN(mlScore) || double.IsInfinity(mlScore))
                {
                    _logger.LogWarning(
                        "ML prediction returned invalid score (NaN/Infinity) for user {UserId}, ignoring ML score",
                        userId);
                    mlScore = 0;
                }
                else
                {
                    _logger.LogInformation("ML score for user {UserId}: {Score}", userId, mlScore);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ML prediction failed for user {UserId}, using rule-based only", userId);
                mlScore = 0;
            }

            // Use maximum of ML and rule-based score
            var finalScore = Math.Max(mlScore, ruleBasedScore);
            _logger.LogInformation(
                "Final score for user {UserId}: {Score} (ML: {ML}, Rules: {Rules})",
                userId, finalScore, mlScore, ruleBasedScore);

            var patterns = AnalyzePatterns(auditLogs);

            var riskLevel = finalScore switch
            {
                >= 80 => AnomalyRiskLevel.Critical,
                >= 60 => AnomalyRiskLevel.High,
                >= 40 => AnomalyRiskLevel.Medium,
                >= 20 => AnomalyRiskLevel.Low,
                _ => AnomalyRiskLevel.Normal
            };

            var result = new AnomalyAnalysisResult
            {
                UserId = userId,
                Username = user.Username,
                AnomalyScore = finalScore,
                IsHighRisk = finalScore >= 60,
                RiskLevel = riskLevel,
                DetectedPatterns = patterns,
                AnalyzedAt = DateTime.UtcNow
            };

            result.RecommendedAction = GetRecommendedAction(result);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing user behavior for user {UserId}", userId);
            throw;
        }
    }

    public async Task<List<AnomalyAnalysisResult>> DetectAnomaliesAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!IsModelReady)
            {
                _logger.LogWarning("Model not ready for anomaly detection");
                return new List<AnomalyAnalysisResult>();
            }

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var activeUserIds = await context.AuditLogs
                .Where(a => a.Timestamp >= from && a.Timestamp <= to && a.UserId.HasValue)
                .Select(a => a.UserId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);

            var results = new List<AnomalyAnalysisResult>();

            foreach (var userId in activeUserIds)
            {
                var result = await AnalyzeUserBehaviorAsync(userId, from);
                if (result.IsHighRisk)
                {
                    results.Add(result);
                }
            }

            return results.OrderByDescending(r => r.AnomalyScore).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting anomalies");
            throw;
        }
    }

    public async Task<bool> TrainModelAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            _logger.LogInformation("Starting anomaly detection model training...");

            // Load training data (last 180 days)
            var startDate = DateTime.UtcNow.AddDays(-180);
            var auditLogs = await context.AuditLogs
                .Where(a => a.Timestamp >= startDate && a.UserId.HasValue)
                .OrderBy(a => a.Timestamp)
                .ToListAsync(cancellationToken);

            if (auditLogs.Count < 100)
            {
                _logger.LogWarning(
                    "Not enough data for training (need at least 100 logs, got {Count})",
                    auditLogs.Count);
                return false;
            }

            // Group by user and extract features
            var trainingData = new List<AuditBehaviorInput>();
            var userGroups = auditLogs.GroupBy(a => a.UserId!.Value);

            foreach (var group in userGroups)
            {
                var userLogs = group.ToList();
                if (userLogs.Count >= 5)
                {
                    var features = ExtractFeatures(userLogs);
                    trainingData.Add(features);
                }
            }

            if (trainingData.Count < 10)
            {
                _logger.LogWarning("Not enough users for training");
                return false;
            }

            var dataView = _mlContext.Data.LoadFromEnumerable(trainingData);

            // Define pipeline with higher rank for better anomaly detection
            var pipeline = _mlContext.Transforms.Concatenate(
                "Features",
                nameof(AuditBehaviorInput.HourOfDay),
                nameof(AuditBehaviorInput.DayOfWeek),
                nameof(AuditBehaviorInput.ActionCount),
                nameof(AuditBehaviorInput.FailedLoginCount),
                nameof(AuditBehaviorInput.UniqueIpCount),
                nameof(AuditBehaviorInput.TimeSinceLastAction),
                nameof(AuditBehaviorInput.ActionDiversity),
                nameof(AuditBehaviorInput.IpChangeFrequency),
                nameof(AuditBehaviorInput.SensitiveActionCount))
                .Append(_mlContext.AnomalyDetection.Trainers.RandomizedPca(
                    featureColumnName: "Features",
                    rank: 3,
                    ensureZeroMean: true,
                    oversampling: 20));

            _trainedModel = pipeline.Fit(dataView);

            _predictionEngine = _mlContext.Model
                .CreatePredictionEngine<AuditBehaviorInput, AnomalyDetectionOutput>(_trainedModel);

            Directory.CreateDirectory(Path.GetDirectoryName(_modelPath)!);
            _mlContext.Model.Save(_trainedModel, dataView.Schema, _modelPath);

            LastTrainingDate = DateTime.UtcNow;

            _logger.LogInformation(
                "Model training completed successfully with {Count} samples", trainingData.Count);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error training anomaly detection model");
            return false;
        }
    }

    private void LoadModelIfExists()
    {
        try
        {
            if (File.Exists(_modelPath))
            {
                try
                {
                    _trainedModel = _mlContext.Model.Load(_modelPath, out var modelSchema);
                    _predictionEngine = _mlContext.Model
                        .CreatePredictionEngine<AuditBehaviorInput, AnomalyDetectionOutput>(_trainedModel);
                    LastTrainingDate = File.GetLastWriteTimeUtc(_modelPath);
                    _logger.LogInformation(
                        "Loaded existing anomaly detection model from {Date}", LastTrainingDate);
                }
                catch (ArgumentOutOfRangeException ex)
                    when (ex.Message.Contains("Features") || ex.Message.Contains("Vector"))
                {
                    _logger.LogWarning(
                        "Old model format detected. Deleting old model - please retrain.");
                    try
                    {
                        File.Delete(_modelPath);
                        _logger.LogInformation("Deleted old model file: {ModelPath}", _modelPath);
                    }
                    catch (Exception deleteEx)
                    {
                        _logger.LogWarning(deleteEx, "Could not delete old model file");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load existing model");
        }
    }

    private double CalculateRuleBasedAnomalyScore(AuditBehaviorInput features, List<AuditLog> logs)
    {
        double score = 0;

        _logger.LogInformation("Calculating rule-based anomaly score for {Count} logs", logs.Count);

        // Night activity (0-6h) - max 30 points
        var nightActivity = logs.Count(l => l.Timestamp.Hour >= 0 && l.Timestamp.Hour < 6);
        if (logs.Any())
        {
            var nightRatio = (double)nightActivity / logs.Count;
            score += nightRatio * 30;
            _logger.LogInformation(
                "Night activity: {Count}/{Total} = {Ratio:P} -> {Points} points",
                nightActivity, logs.Count, nightRatio, nightRatio * 30);
        }

        // Failed logins - max 25 points
        var failedLogins = logs.Count(l => l.Action == "LOGIN_FAILED");
        if (failedLogins > 0)
        {
            var points = Math.Min(failedLogins * 2.5, 25);
            score += points;
            _logger.LogInformation("Failed logins: {Count} -> {Points} points", failedLogins, points);
        }

        // Sensitive actions - max 25 points
        var sensitiveActions = logs.Count(l =>
            l.Action.Contains("DELETE") ||
            l.Action.Contains("EXPORT") ||
            l.Action == "2FA_DISABLED");
        if (sensitiveActions > 0)
        {
            var points = Math.Min(sensitiveActions * 2.5, 25);
            score += points;
            _logger.LogInformation(
                "Sensitive actions: {Count} -> {Points} points", sensitiveActions, points);
        }

        // IP changes - max 15 points
        var uniqueIps = logs.Select(l => l.IpAddress).Distinct().Count();
        if (uniqueIps > 5)
        {
            var points = Math.Min((uniqueIps - 5) * 2, 15);
            score += points;
            _logger.LogInformation("IP changes: {Count} IPs -> {Points} points", uniqueIps, points);
        }

        // High activity in short time - max 5 points
        if (logs.Count > 50)
        {
            score += 5;
            _logger.LogInformation("High activity: {Count} logs -> 5 points", logs.Count);
        }

        var finalScore = Math.Min(score, 100);
        _logger.LogInformation("Total rule-based score: {Score} (capped at 100)", finalScore);

        return finalScore;
    }

    private AuditBehaviorInput ExtractFeatures(List<AuditLog> logs)
    {
        var features = new AuditBehaviorInput
        {
            ActionCount = logs.Count,
            FailedLoginCount = logs.Count(l => l.Action.Contains("FAILED")),
            UniqueIpCount = logs.Select(l => l.IpAddress).Distinct().Count(),
            ActionDiversity = logs.Select(l => l.Action).Distinct().Count(),
            SensitiveActionCount = logs.Count(l =>
                l.Action.Contains("DELETE") ||
                l.Action.Contains("EXPORT") ||
                l.Action == "2FA_DISABLED")
        };

        features.HourOfDay = (float)logs.Average(l => l.Timestamp.Hour);
        features.DayOfWeek = (float)logs.Average(l => (int)l.Timestamp.DayOfWeek);

        // IP change frequency
        var ipChanges = 0;
        for (int i = 1; i < logs.Count; i++)
        {
            if (logs[i].IpAddress != logs[i - 1].IpAddress)
                ipChanges++;
        }
        features.IpChangeFrequency = logs.Count > 1 ? (float)ipChanges / logs.Count : 0;

        // Average time since last action
        var timeDiffs = new List<double>();
        for (int i = 1; i < logs.Count; i++)
        {
            var diff = (logs[i].Timestamp - logs[i - 1].Timestamp).TotalMinutes;
            timeDiffs.Add(diff);
        }
        features.TimeSinceLastAction = timeDiffs.Any() ? (float)timeDiffs.Average() : 0;

        return features;
    }

    private List<string> AnalyzePatterns(List<AuditLog> logs)
    {
        var patterns = new List<string>();

        // Unusual hours (0-6h)
        var nightActivity = logs.Count(l => l.Timestamp.Hour >= 0 && l.Timestamp.Hour < 6);
        if (nightActivity > logs.Count * 0.3)
            patterns.Add($"Hohe Nachtaktivit\u00e4t: {nightActivity} Aktionen zwischen 0-6 Uhr");

        // Many failed logins
        var failedLogins = logs.Count(l => l.Action == "LOGIN_FAILED");
        if (failedLogins > 5)
            patterns.Add($"Viele fehlgeschlagene Login-Versuche: {failedLogins}");

        // Frequent IP changes
        var uniqueIps = logs.Select(l => l.IpAddress).Distinct().Count();
        if (uniqueIps > 5)
            patterns.Add($"H\u00e4ufige IP-Wechsel: {uniqueIps} verschiedene IPs");

        // Sensitive actions
        var sensitiveActions = logs.Count(l =>
            l.Action.Contains("DELETE") ||
            l.Action.Contains("EXPORT") ||
            l.Action == "2FA_DISABLED");
        if (sensitiveActions > 0)
            patterns.Add($"Sensible Aktionen: {sensitiveActions}");

        // Bulk actions
        var massActions = logs
            .GroupBy(l => l.Timestamp.ToString("yyyy-MM-dd HH:mm"))
            .Where(g => g.Count() > 10)
            .ToList();
        if (massActions.Any())
            patterns.Add($"Massenaktionen erkannt: {massActions.Count} Zeitfenster mit >10 Aktionen");

        if (!patterns.Any())
            patterns.Add("Keine auff\u00e4lligen Muster erkannt");

        return patterns;
    }

    private string GetRecommendedAction(AnomalyAnalysisResult result)
    {
        if (result.AnomalyScore >= 80)
            return "Account sofort \u00fcberpr\u00fcfen und ggf. sperren";

        if (result.AnomalyScore >= 60)
            return "2FA erzwingen und Benutzer kontaktieren";

        if (result.AnomalyScore >= 40)
            return "Erweiterte \u00dcberwachung aktivieren";

        if (result.AnomalyScore >= 20)
            return "Gelegentlich \u00fcberpr\u00fcfen";

        return "Normales Verhalten - keine Ma\u00dfnahmen erforderlich";
    }
}
