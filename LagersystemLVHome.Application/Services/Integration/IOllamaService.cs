namespace LagersystemLVHome.Application.Services;

public interface IOllamaService
{
    // Chat Completion
    Task<string> ChatAsync(string prompt, string model = "llama3.2", string? systemPrompt = null, CancellationToken cancellationToken = default);
    Task<string> ChatWithHistoryAsync(List<ChatMessage> messages, string model = "llama3.2", CancellationToken cancellationToken = default);

    // Specialized Inventory Queries
    Task<string> AskInventoryQuestionAsync(string question, CancellationToken cancellationToken = default);
    Task<string> GenerateProductDescriptionAsync(string productName, string? category = null, CancellationToken cancellationToken = default);
    Task<string> SuggestOptimalStorageAsync(string productName, string category, CancellationToken cancellationToken = default);
    Task<string> AnalyzeInventoryTrendsAsync(CancellationToken cancellationToken = default);
    Task<string> PredictReorderNeedsAsync(CancellationToken cancellationToken = default);

    // Natural Language to SQL
    Task<string> ConvertToSqlQueryAsync(string naturalLanguageQuery, CancellationToken cancellationToken = default);

    // Models Management
    Task<List<OllamaModel>> GetAvailableModelsAsync(CancellationToken cancellationToken = default);
    Task<bool> PullModelAsync(string modelName, CancellationToken cancellationToken = default);
    Task<bool> IsModelAvailableAsync(string modelName, CancellationToken cancellationToken = default);
    Task<OllamaModelInfo> GetModelInfoAsync(string modelName, CancellationToken cancellationToken = default);

    // Health Check
    Task<bool> IsOllamaRunningAsync(CancellationToken cancellationToken = default);
    Task<OllamaStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

public sealed class ChatMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
}

public sealed class OllamaModel
{
    public string Name { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("modified_at")]
    public DateTime ModifiedAt { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("size")]
    public long SizeBytes { get; set; }

    public string Digest { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonIgnore]
    public string Size => FormatSize(SizeBytes);

    private static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

public sealed class OllamaModelInfo
{
    public string Name { get; set; } = string.Empty;
    public string Template { get; set; } = string.Empty;
    public string Parameters { get; set; } = string.Empty;
    public long Size { get; set; }
}

public sealed class OllamaStatus
{
    public bool IsRunning { get; set; }
    public string Version { get; set; } = string.Empty;
    public List<string> AvailableModels { get; set; } = new();
    public string Error { get; set; } = string.Empty;
}

public sealed class OllamaChatRequest
{
    public string Model { get; set; } = string.Empty;
    public List<ChatMessage> Messages { get; set; } = new();
    public bool Stream { get; set; } = false;
}

public sealed class OllamaChatResponse
{
    public ChatMessage Message { get; set; } = new();
    public bool Done { get; set; }
    public long TotalDuration { get; set; }
    public long LoadDuration { get; set; }
    public long PromptEvalCount { get; set; }
    public long EvalCount { get; set; }
}
