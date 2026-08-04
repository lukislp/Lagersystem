using LagersystemLVHome.Infrastructure.ML.Models;

namespace LagersystemLVHome.Infrastructure.ML.Services;

/// <summary>
/// Service for intelligent product categorization.
/// </summary>
public interface ICategoryPredictionService
{
    /// <summary>
    /// Suggests categories for a product.
    /// </summary>
    Task<CategorizationResult> SuggestCategoriesAsync(string productName, string? description = null, string? barcode = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Auto-categorizes products without a category.
    /// </summary>
    Task<int> AutoCategorizeProductsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Trains the categorization model.
    /// </summary>
    Task<bool> TrainModelAsync(CancellationToken cancellationToken = default);

    Task<List<string>> FindSimilarProductsAsync(string productName, int limit = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// Indicates whether the categorization system is ready (keyword system or ML model).
    /// </summary>
    bool IsModelReady { get; }

    /// <summary>
    /// Indicates whether the ML model has been trained.
    /// </summary>
    bool IsMlModelTrained { get; }
}
