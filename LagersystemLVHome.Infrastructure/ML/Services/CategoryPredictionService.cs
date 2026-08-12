using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.Infrastructure.ML.Models;
using LagersystemLVHome.Infrastructure.ML.Keywords;
using Microsoft.EntityFrameworkCore;
using Microsoft.ML;
using Microsoft.ML.Data;
using System.Text.RegularExpressions;

namespace LagersystemLVHome.Infrastructure.ML.Services;

/// <summary>
/// Intelligent categorization implementation using a hybrid ML + keyword approach.
/// </summary>
public class CategoryPredictionService : ICategoryPredictionService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly ILogger<CategoryPredictionService> _logger;
    private readonly CategoryKeywordService _keywordService;
    private readonly string _modelPath;
    private readonly MLContext _mlContext;
    private ITransformer? _trainedModel;
    private PredictionEngine<CategoryPredictionInput, CategoryPredictionOutput>? _predictionEngine;

    /// <summary>
    /// Ready when the keyword system is loaded. ML model is optional.
    /// </summary>
    public bool IsModelReady => _keywordService != null;

    /// <summary>
    /// Indicates whether the ML model has been trained.
    /// </summary>
    public bool IsMlModelTrained => _trainedModel != null;

    public CategoryPredictionService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        ILogger<CategoryPredictionService> logger,
        IWebHostEnvironment env,
        CategoryKeywordService keywordService)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _keywordService = keywordService;
        _mlContext = new MLContext(seed: 1);
        _modelPath = Path.Combine(env.ContentRootPath, "ML", "Data", "category-prediction-model.zip");

        LoadModelIfExists();
    }

    public async Task<CategorizationResult> SuggestCategoriesAsync(
        string productName,
        string? description = null,
        string? barcode = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = new CategorizationResult
            {
                ProductName = productName
            };

            if (!IsMlModelTrained)
            {
                // Keyword-only mode (no ML model available)
                _logger.LogDebug("Using keyword-based categorization (no ML model trained yet)");
                result.Suggestions = await GetRuleBasedSuggestionsAsync(productName, description);
                return result;
            }

            // Hybrid approach: ML + keywords combined
            _logger.LogDebug("Using hybrid approach: ML model + keyword-based categorization");

            if (_predictionEngine == null && _trainedModel != null)
            {
                try
                {
                    _predictionEngine = _mlContext.Model
                        .CreatePredictionEngine<CategoryPredictionInput, CategoryPredictionOutput>(_trainedModel);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not create prediction engine, falling back to keyword-based");
                    result.Suggestions = await GetRuleBasedSuggestionsAsync(productName, description);
                    return result;
                }
            }

            var input = new CategoryPredictionInput
            {
                ProductName = productName,
                Description = description ?? "",
                Barcode = barcode ?? "",
                Manufacturer = ExtractManufacturer(productName)
            };

            var prediction = _predictionEngine!.Predict(input);

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var categories = await context.Categories.ToListAsync(cancellationToken);
            var mlSuggestions = new List<CategorySuggestion>();

            for (int i = 0; i < Math.Min(prediction.Probabilities.Length, 5); i++)
            {
                var probability = prediction.Probabilities[i];
                if (probability > 0.1)
                {
                    var categoryName = i == 0
                        ? prediction.CategoryName
                        : GetCategoryNameByIndex(categories, i);
                    var category = categories.FirstOrDefault(c => c.Name == categoryName);

                    if (category != null)
                    {
                        mlSuggestions.Add(new CategorySuggestion
                        {
                            CategoryId = category.Id,
                            CategoryName = category.Name,
                            CategoryIcon = category.Icon,
                            Confidence = probability * 100,
                            Reasons = new List<string> { "ML-Modell Vorhersage" }
                        });
                    }
                }
            }

            // Hybrid: also get keyword suggestions
            var keywordSuggestions = await GetRuleBasedSuggestionsAsync(productName, description);

            // Combine ML + keywords (weighted average)
            var combinedSuggestions = new Dictionary<int, CategorySuggestion>();

            // Add ML suggestions (weight: 0.6)
            foreach (var mlSugg in mlSuggestions)
            {
                if (!combinedSuggestions.ContainsKey(mlSugg.CategoryId))
                {
                    combinedSuggestions[mlSugg.CategoryId] = new CategorySuggestion
                    {
                        CategoryId = mlSugg.CategoryId,
                        CategoryName = mlSugg.CategoryName,
                        CategoryIcon = mlSugg.CategoryIcon,
                        Confidence = mlSugg.Confidence * 0.6,
                        Reasons = new List<string> { "ML-Modell" }
                    };
                }
                else
                {
                    combinedSuggestions[mlSugg.CategoryId].Confidence += mlSugg.Confidence * 0.6;
                }
            }

            // Add keyword suggestions (weight: 0.4)
            foreach (var kwSugg in keywordSuggestions)
            {
                if (!combinedSuggestions.ContainsKey(kwSugg.CategoryId))
                {
                    combinedSuggestions[kwSugg.CategoryId] = new CategorySuggestion
                    {
                        CategoryId = kwSugg.CategoryId,
                        CategoryName = kwSugg.CategoryName,
                        CategoryIcon = kwSugg.CategoryIcon,
                        Confidence = kwSugg.Confidence * 0.4,
                        Reasons = kwSugg.Reasons
                    };
                }
                else
                {
                    combinedSuggestions[kwSugg.CategoryId].Confidence += kwSugg.Confidence * 0.4;
                    combinedSuggestions[kwSugg.CategoryId].Reasons.AddRange(kwSugg.Reasons);
                }
            }

            result.Suggestions = combinedSuggestions.Values
                .OrderByDescending(s => s.Confidence)
                .Take(5)
                .Select(s => new CategorySuggestion
                {
                    CategoryId = s.CategoryId,
                    CategoryName = s.CategoryName,
                    CategoryIcon = s.CategoryIcon,
                    Confidence = Math.Min(s.Confidence, 95),
                    Reasons = s.Reasons.Distinct().ToList()
                })
                .ToList();

            _logger.LogInformation(
                "Hybrid categorization for '{ProductName}': {TopCategory} ({Confidence:F1}% - ML+Keywords)",
                productName,
                result.Suggestions.FirstOrDefault()?.CategoryName ?? "None",
                result.Suggestions.FirstOrDefault()?.Confidence ?? 0);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error suggesting categories for product {ProductName}", productName);
            return new CategorizationResult
            {
                ProductName = productName,
                Suggestions = new List<CategorySuggestion>()
            };
        }
    }

    public async Task<int> AutoCategorizeProductsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var uncategorizedProducts = await context.Products
                .Where(p => p.CategoryId == null)
                .ToListAsync(cancellationToken);

            var categorizedCount = 0;

            foreach (var product in uncategorizedProducts)
            {
                var result = await SuggestCategoriesAsync(product.Name, product.Description, product.Barcode);

                if (result.BestMatch != null && result.BestMatch.Confidence >= 70)
                {
                    product.CategoryId = result.BestMatch.CategoryId;
                    categorizedCount++;
                }
            }

            if (categorizedCount > 0)
            {
                await context.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Auto-categorized {Count} products", categorizedCount);
            }

            return categorizedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error auto-categorizing products");
            throw;
        }
    }

    public async Task<bool> TrainModelAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            _logger.LogInformation("Starting category prediction model training...");

            var products = await context.Products
                .Include(p => p.Category)
                .Where(p => p.CategoryId != null && p.Category != null)
                .ToListAsync(cancellationToken);

            // Minimum 10 products required; keywords serve as fallback
            if (products.Count < 10)
            {
                _logger.LogWarning(
                    "Not enough categorized products for training (need at least 10, got {Count})",
                    products.Count);
                _logger.LogInformation(
                    "Keyword system will be used as primary system (2500+ keywords, 33 categories)");
                return false;
            }

            var trainingData = products.Select(p => new CategoryTrainingData
            {
                ProductName = p.Name,
                Description = p.Description ?? "",
                Barcode = p.Barcode ?? "",
                Manufacturer = ExtractManufacturer(p.Name),
                Label = p.Category!.Name
            }).ToList();

            var dataView = _mlContext.Data.LoadFromEnumerable(trainingData);

            // Define pipeline with text featurization
            var pipeline = _mlContext.Transforms.Conversion.MapValueToKey("Label")
                .Append(_mlContext.Transforms.Text.FeaturizeText("ProductNameFeaturized", "ProductName"))
                .Append(_mlContext.Transforms.Text.FeaturizeText("DescriptionFeaturized", "Description"))
                .Append(_mlContext.Transforms.Text.FeaturizeText("ManufacturerFeaturized", "Manufacturer"))
                .Append(_mlContext.Transforms.Concatenate(
                    "Features",
                    "ProductNameFeaturized",
                    "DescriptionFeaturized",
                    "ManufacturerFeaturized"))
                .Append(_mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy())
                .Append(_mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

            _trainedModel = pipeline.Fit(dataView);

            Directory.CreateDirectory(Path.GetDirectoryName(_modelPath)!);
            _mlContext.Model.Save(_trainedModel, dataView.Schema, _modelPath);

            _logger.LogInformation("Model saved to {ModelPath}", _modelPath);

            // Reset prediction engine; it will be recreated on first predict call
            _predictionEngine = null;

            _logger.LogInformation(
                "Category prediction model training completed with {Count} samples", products.Count);
            _logger.LogInformation(
                "ML model supplements the keyword system (hybrid approach for best results)");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error training category prediction model");
            return false;
        }
    }

    public async Task<List<string>> FindSimilarProductsAsync(string productName, int limit = 10, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var words = ExtractKeywords(productName);

            // words are already lowercased by ExtractKeywords - match p.Name case-insensitively
            // too, or a product name like "Batterie" would never match the keyword "batterie".
            var products = await context.Products
                .Where(p => words.Any(w => p.Name.ToLower().Contains(w)))
                .Select(p => p.Name)
                .Take(limit)
                .ToListAsync(cancellationToken);

            return products;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding similar products");
            return new List<string>();
        }
    }

    private void LoadModelIfExists()
    {
        try
        {
            if (File.Exists(_modelPath))
            {
                _trainedModel = _mlContext.Model.Load(_modelPath, out var modelSchema);
                _predictionEngine = _mlContext.Model
                    .CreatePredictionEngine<CategoryPredictionInput, CategoryPredictionOutput>(_trainedModel);
                _logger.LogInformation("Loaded existing category prediction model");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load existing model");
        }
    }

    private async Task<List<CategorySuggestion>> GetRuleBasedSuggestionsAsync(
        string productName, string? description, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var categories = await context.Categories.ToListAsync(cancellationToken);
        var suggestions = new List<CategorySuggestion>();
        var nameLower = productName.ToLower();
        var descLower = description?.ToLower() ?? "";

        _logger.LogInformation(
            "Processing rule-based suggestions for product: {ProductName}", productName);

        foreach (var category in categories)
        {
            var keywords = _keywordService.GetKeywordsForCategory(category.Name);
            var weightedKeywords = _keywordService.GetWeightedKeywordsForCategory(category.Name);

            if (keywords.Count == 0)
            {
                _logger.LogWarning("No keywords found for category: {Category}", category.Name);
                continue;
            }

            // Calculate quality-weighted matches
            var matchScore = 0.0;
            var matchedKeywords = new List<(string keyword, double quality)>();

            foreach (var keyword in keywords)
            {
                var keywordLower = keyword.ToLower();
                bool foundInName = nameLower.Contains(keywordLower);
                bool foundInDesc = descLower.Contains(keywordLower);

                if (foundInName || foundInDesc)
                {
                    var baseWeight = weightedKeywords.ContainsKey(keyword)
                        ? weightedKeywords[keyword]
                        : 1.0;

                    var qualityScore = CalculateMatchQuality(
                        keyword: keywordLower,
                        text: foundInName ? nameLower : descLower,
                        textLength: foundInName ? nameLower.Length : descLower.Length);

                    var totalScore = baseWeight * qualityScore * 10.0;

                    // Bonus for name match (more important than description)
                    if (foundInName)
                    {
                        totalScore *= 1.5;
                    }

                    matchScore += totalScore;
                    matchedKeywords.Add((keyword, qualityScore));

                    _logger.LogDebug(
                        "Keyword '{Keyword}' matched in {Location} for category {Category}: " +
                        "BaseWeight={BaseWeight}, Quality={Quality:F2}, TotalScore={TotalScore:F2}",
                        keyword, foundInName ? "Name" : "Description", category.Name,
                        baseWeight, qualityScore, totalScore);
                }
            }

            if (matchScore > 0)
            {
                // Sort matches by quality
                var bestMatches = matchedKeywords
                    .OrderByDescending(m => m.quality)
                    .Take(3)
                    .Select(m => m.keyword)
                    .ToList();

                var confidence = Math.Min(30.0 + matchScore, 95.0);

                // Penalty for too many weak matches (spam detection)
                if (matchedKeywords.Count > 5)
                {
                    var avgQuality = matchedKeywords.Average(m => m.quality);
                    if (avgQuality < 0.3)
                    {
                        confidence *= 0.7;
                        _logger.LogDebug(
                            "Category {Category}: Penalty applied for {Count} weak matches (avgQuality={AvgQuality:F2})",
                            category.Name, matchedKeywords.Count, avgQuality);
                    }
                }

                if (!string.IsNullOrWhiteSpace(description))
                {
                    confidence += 5.0;
                }

                confidence = Math.Min(confidence, 95.0);

                _logger.LogDebug(
                    "Category {Category}: {MatchCount} matches, score: {Score:F2}, confidence: {Confidence:F1}%",
                    category.Name, matchedKeywords.Count, matchScore, confidence);

                suggestions.Add(new CategorySuggestion
                {
                    CategoryId = category.Id,
                    CategoryName = category.Name,
                    CategoryIcon = category.Icon,
                    Confidence = confidence,
                    Reasons = new List<string>
                    {
                        $"{matchedKeywords.Count} Schl\u00fcsselw\u00f6rter: {string.Join(", ", bestMatches)}"
                    }
                });
            }
        }

        var topSuggestions = suggestions.OrderByDescending(s => s.Confidence).Take(5).ToList();

        _logger.LogInformation(
            "Found {Count} suggestions for product '{ProductName}': {TopCategory} ({Confidence:F1}%)",
            topSuggestions.Count,
            productName,
            topSuggestions.FirstOrDefault()?.CategoryName ?? "None",
            topSuggestions.FirstOrDefault()?.Confidence ?? 0);

        return topSuggestions;
    }

    /// <summary>
    /// Calculates the match quality of a keyword.
    /// </summary>
    private double CalculateMatchQuality(string keyword, string text, int textLength)
    {
        var lengthScore = keyword.Length switch
        {
            <= 2 => 0.1,
            3 => 0.3,
            4 => 0.5,
            5 => 0.7,
            6 => 0.9,
            >= 7 => 1.2
        };

        // Whole-word boundary check
        var isWholeWord = IsWholeWord(keyword, text);
        var wholeWordBonus = isWholeWord ? 0.5 : 0.0;

        // Position check: beginning of text is more important
        var startsWithKeyword = text.StartsWith(keyword);
        var positionBonus = startsWithKeyword ? 0.3 : 0.0;

        // Relative coverage of the text
        var relativeLength = (double)keyword.Length / textLength;
        var coverageBonus = relativeLength > 0.5 ? 0.2 : 0.0;

        var totalQuality = lengthScore + wholeWordBonus + positionBonus + coverageBonus;

        return Math.Min(totalQuality, 2.0);
    }

    /// <summary>
    /// Checks whether a keyword is a whole word (not part of another word).
    /// </summary>
    private bool IsWholeWord(string keyword, string text)
    {
        var index = text.IndexOf(keyword, StringComparison.Ordinal);
        if (index == -1) return false;

        var charBefore = index > 0 ? text[index - 1] : ' ';
        var isWordBoundaryBefore = !char.IsLetterOrDigit(charBefore);

        var endIndex = index + keyword.Length;
        var charAfter = endIndex < text.Length ? text[endIndex] : ' ';
        var isWordBoundaryAfter = !char.IsLetterOrDigit(charAfter);

        return isWordBoundaryBefore && isWordBoundaryAfter;
    }

    private List<string> ExtractKeywords(string text)
    {
        var stopWords = new HashSet<string>
        {
            "der", "die", "das", "und", "oder", "ein", "eine", "mit", "f\u00fcr", "von"
        };

        var words = Regex.Split(text.ToLower(), @"\W+")
            .Where(w => w.Length > 3 && !stopWords.Contains(w))
            .ToList();

        return words;
    }

    private string? ExtractManufacturer(string productName)
    {
        var words = productName.Split(' ');
        return words.Length > 0 ? words[0] : null;
    }

    private string GetCategoryNameByIndex(List<Category> categories, int index)
    {
        if (index >= 0 && index < categories.Count)
            return categories[index].Name;
        return "Sonstiges";
    }

    /// <summary>
    /// Helper class for training with label.
    /// </summary>
    private class CategoryTrainingData
    {
        public string ProductName { get; set; } = "";
        public string Description { get; set; } = "";
        public string Barcode { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public string Label { get; set; } = "";
    }
}
