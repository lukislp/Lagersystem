using Microsoft.ML.Data;

namespace LagersystemLVHome.Infrastructure.ML.Models;

/// <summary>
/// Input for intelligent product categorization.
/// </summary>
public class CategoryPredictionInput
{
    [LoadColumn(0)]
    public string ProductName { get; set; } = string.Empty;

    [LoadColumn(1)]
    public string? Description { get; set; }

    [LoadColumn(2)]
    public string? Barcode { get; set; }

    [LoadColumn(3)]
    public string? Manufacturer { get; set; }
}

/// <summary>
/// Output of the categorization.
/// </summary>
public class CategoryPredictionOutput
{
    [ColumnName("PredictedLabel")]
    public string CategoryName { get; set; } = string.Empty;

    [ColumnName("Score")]
    public float[] Probabilities { get; set; } = Array.Empty<float>();
}

/// <summary>
/// Category suggestion with confidence score.
/// </summary>
public class CategorySuggestion
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string CategoryIcon { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public List<string> Reasons { get; set; } = new();
}

/// <summary>
/// Result of the categorization.
/// </summary>
public class CategorizationResult
{
    public string ProductName { get; set; } = string.Empty;
    public List<CategorySuggestion> Suggestions { get; set; } = new();
    public CategorySuggestion? BestMatch => Suggestions.FirstOrDefault();
    public DateTime PredictedAt { get; set; } = DateTime.UtcNow;
}
