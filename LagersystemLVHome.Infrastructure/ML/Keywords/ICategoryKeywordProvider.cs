namespace LagersystemLVHome.Infrastructure.ML.Keywords;

/// <summary>
/// Interface for keyword providers per category.
/// </summary>
public interface ICategoryKeywordProvider
{
    /// <summary>
    /// Name of the category.
    /// </summary>
    string CategoryName { get; }

    /// <summary>
    /// List of all keywords for this category.
    /// </summary>
    List<string> GetKeywords();

    /// <summary>
    /// Weighted keywords (optional) for improved matching accuracy.
    /// </summary>
    Dictionary<string, double> GetWeightedKeywords() => new();
}
