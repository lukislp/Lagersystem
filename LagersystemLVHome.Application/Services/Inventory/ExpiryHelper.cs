namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Helper service for expiry/best-before-date logic.
/// </summary>
public static class ExpiryHelper
{
    /// <summary>
    /// Categories that can have a best-before date.
    /// </summary>
    private static readonly string[] CategoriesWithExpiry =
    [
        // Food and beverages
        "lebensmittel", "food",
        "wein & spirituosen", "wein", "spirituosen", "beverages", "alcohol",
        "kaffee & tee", "coffee", "tea",

        // Health and medicine
        "gesundheit", "health", "medizin", "medicine",
        "nahrungsergänzung", "supplements",
        "drogerie", "drugstore",

        // Cosmetics and personal care
        "kosmetik", "cosmetics", "pflege", "care",
        "shampoo", "duschgel", "creme",

        // Pet food and supplies
        "haustiere", "pets", "tierfutter", "pet food",
        "tiermedizin & pflege", "veterinary",

        // Baby and child
        "baby & kind", "baby", "babynahrung", "baby food",

        // Chemicals and cleaning (limited shelf life)
        "reinigung", "cleaning", "chemie", "chemicals"
    ];

    /// <param name="categoryName">Category name.</param>
    /// <param name="explicitlyEnabled">Whether TrackExpiryDate was explicitly enabled.</param>
    /// <returns>True if expiry tracking should be active.</returns>
    public static bool ShouldTrackExpiry(string? categoryName, bool explicitlyEnabled = false)
    {
        if (explicitlyEnabled) return true;
        if (string.IsNullOrWhiteSpace(categoryName)) return false;

        var categoryNameLower = categoryName.ToLower();
        return CategoriesWithExpiry.Any(cat => categoryNameLower.Contains(cat));
    }

    public static IEnumerable<string> GetExpiryCategories()
    {
        return CategoriesWithExpiry.AsEnumerable();
    }

    public static bool ShouldProductTrackExpiry(LagersystemLVHome.Domain.Models.Product product)
    {
        return ShouldTrackExpiry(product.Category?.Name, product.TrackExpiryDate);
    }
}
