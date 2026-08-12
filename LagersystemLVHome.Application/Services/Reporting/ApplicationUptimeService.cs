using System.Diagnostics;

namespace LagersystemLVHome.Application.Services;

public sealed class ApplicationUptimeService : IApplicationUptimeService
{
    private static readonly string UptimeFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LagerSystem",
        "uptime.json"
    );

    private readonly ILogger<ApplicationUptimeService> _logger;
    private UptimeData _uptimeData;

    public ApplicationUptimeService(ILogger<ApplicationUptimeService> logger)
    {
        _logger = logger;
        _uptimeData = LoadOrCreateUptimeData();

        _logger.LogInformation("Application Uptime Service initialized");
        _logger.LogInformation("Application Start: {Start}", _uptimeData.ApplicationStartTime);
        _logger.LogInformation("Recycles: {Count}, Last: {Last}",
            _uptimeData.RecycleCount,
            _uptimeData.LastRecycleTime);
    }

    public DateTime ApplicationStartTime => _uptimeData.ApplicationStartTime;

    public TimeSpan ApplicationUptime => DateTime.UtcNow - _uptimeData.ApplicationStartTime;

    public TimeSpan ProcessUptime => DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime();

    public DateTime LastRecycleTime => _uptimeData.LastRecycleTime;

    public int RecycleCount => _uptimeData.RecycleCount;

    private UptimeData LoadOrCreateUptimeData()
    {
        try
        {
            var directory = Path.GetDirectoryName(UptimeFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(UptimeFilePath))
            {
                var json = File.ReadAllText(UptimeFilePath);
                var data = System.Text.Json.JsonSerializer.Deserialize<UptimeData>(json);

                if (data != null)
                {
                    data.RecycleCount++;
                    data.LastRecycleTime = DateTime.UtcNow;

                    SaveUptimeData(data);

                    _logger.LogInformation("IIS Recycle detected. Total recycles: {Count}", data.RecycleCount);
                    return data;
                }
            }

            // First initialization
            var newData = new UptimeData
            {
                ApplicationStartTime = DateTime.UtcNow,
                LastRecycleTime = DateTime.UtcNow,
                RecycleCount = 0
            };

            SaveUptimeData(newData);

            _logger.LogInformation("First application start - Uptime tracking initialized");
            return newData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading uptime data - using in-memory fallback");

            return new UptimeData
            {
                ApplicationStartTime = DateTime.UtcNow,
                LastRecycleTime = DateTime.UtcNow,
                RecycleCount = 0
            };
        }
    }

    private void SaveUptimeData(UptimeData data)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(UptimeFilePath, json);
            _logger.LogDebug("Uptime data saved to {Path}", UptimeFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving uptime data");
        }
    }

    private class UptimeData
    {
        public DateTime ApplicationStartTime { get; set; }
        public DateTime LastRecycleTime { get; set; }
        public int RecycleCount { get; set; }
    }
}
