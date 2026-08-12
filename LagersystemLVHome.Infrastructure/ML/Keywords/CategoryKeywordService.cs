using System.Reflection;

namespace LagersystemLVHome.Infrastructure.ML.Keywords;

/// <summary>
/// Central service for category keywords.
/// Automatically loads all ICategoryKeywordProvider implementations via reflection.
/// </summary>
public class CategoryKeywordService
{
    private readonly Dictionary<string, ICategoryKeywordProvider> _providers;
    private readonly ILogger<CategoryKeywordService> _logger;

    public CategoryKeywordService(ILogger<CategoryKeywordService> logger)
    {
        _logger = logger;
        _providers = new Dictionary<string, ICategoryKeywordProvider>();
        LoadAllProviders();
    }

    private void LoadAllProviders()
    {
        try
        {
            var providerType = typeof(ICategoryKeywordProvider);
            var providers = Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && providerType.IsAssignableFrom(t))
                .Select(t => Activator.CreateInstance(t) as ICategoryKeywordProvider)
                .Where(p => p != null)
                .Cast<ICategoryKeywordProvider>();

            foreach (var provider in providers)
            {
                _providers[provider.CategoryName] = provider;
                _logger.LogInformation(
                    "Loaded keyword provider for category: {Category} with {Count} keywords",
                    provider.CategoryName, provider.GetKeywords().Count);
            }

            _logger.LogInformation("Loaded {Count} keyword providers", _providers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading keyword providers");
        }
    }

    public List<string> GetKeywordsForCategory(string categoryName)
    {
        if (_providers.TryGetValue(categoryName, out var provider))
        {
            return provider.GetKeywords();
        }

        _logger.LogWarning("No keyword provider found for category: {Category}", categoryName);
        return [];
    }

    public Dictionary<string, double> GetWeightedKeywordsForCategory(string categoryName)
    {
        if (_providers.TryGetValue(categoryName, out var provider))
        {
            return provider.GetWeightedKeywords();
        }

        return new Dictionary<string, double>();
    }

    public List<string> GetAllCategories()
    {
        return _providers.Keys.ToList();
    }

    public int GetTotalKeywordCount()
    {
        return _providers.Values.Sum(p => p.GetKeywords().Count);
    }

    public bool HasCategory(string categoryName)
    {
        return _providers.ContainsKey(categoryName);
    }

    public Dictionary<string, ICategoryKeywordProvider> GetAllProviders()
    {
        return new Dictionary<string, ICategoryKeywordProvider>(_providers);
    }
}
