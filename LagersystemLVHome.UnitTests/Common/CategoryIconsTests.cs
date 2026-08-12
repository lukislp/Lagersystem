using LagersystemLVHome.Data;

namespace LagersystemLVHome.UnitTests.Common;

public class CategoryIconsTests
{
    // --- Catalogue invariants ---

    [Fact]
    public void IconsByCategory_AllKeysAreUnique()
    {
        // Duplicate keys in the collection initializer would be a compile-time
        // error, but this documents the invariant explicitly for future edits.
        CategoryIcons.IconsByCategory.Keys.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void IconsByCategory_NoCategoryHasAnEmptyIconList()
    {
        CategoryIcons.IconsByCategory.Values.Should().OnlyContain(list => list.Count > 0);
    }

    [Fact]
    public void IconsByCategory_ContainsAllgemeinFallbackCategory()
    {
        CategoryIcons.IconsByCategory.Should().ContainKey("Allgemein");
    }

    [Fact]
    public void IconsByCategory_AllIconEntriesHaveNonEmptyIconClassAndName()
    {
        var allEntries = CategoryIcons.IconsByCategory.Values.SelectMany(icons => icons);

        allEntries.Should().OnlyContain(i => !string.IsNullOrWhiteSpace(i.IconClass));
        allEntries.Should().OnlyContain(i => !string.IsNullOrWhiteSpace(i.Name));
    }

    [Fact]
    public void IconsByCategory_AllIconClassesUseBootstrapIconPrefix()
    {
        var allEntries = CategoryIcons.IconsByCategory.Values.SelectMany(icons => icons);

        allEntries.Should().OnlyContain(i => i.IconClass.StartsWith("bi-", StringComparison.Ordinal));
    }

    [Fact]
    public void PopularIcons_IsNotEmpty()
    {
        CategoryIcons.PopularIcons.Should().NotBeEmpty();
    }

    [Fact]
    public void PopularIcons_AllEntriesHaveNonEmptyIconClassAndName()
    {
        CategoryIcons.PopularIcons.Should().OnlyContain(i => !string.IsNullOrWhiteSpace(i.IconClass));
        CategoryIcons.PopularIcons.Should().OnlyContain(i => !string.IsNullOrWhiteSpace(i.Name));
    }

    // --- GetAllIcons ---

    [Fact]
    public void GetAllIcons_ReturnsDistinctIconClasses()
    {
        var result = CategoryIcons.GetAllIcons();

        result.Select(i => i.IconClass).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void GetAllIcons_IsOrderedByName()
    {
        var result = CategoryIcons.GetAllIcons();

        // Compared against the exact same LINQ OrderBy(default comparer) the
        // production code uses, rather than FluentAssertions' BeInAscendingOrder,
        // to avoid a culture/ordinal comparer mismatch between the two.
        result.Select(i => i.Name).Should().Equal(result.Select(i => i.Name).OrderBy(n => n));
    }

    [Fact]
    public void GetAllIcons_ContainsFewerOrEqualEntriesThanTotalRawIconCount()
    {
        var totalRaw = CategoryIcons.IconsByCategory.Values.Sum(list => list.Count);

        var result = CategoryIcons.GetAllIcons();

        // DistinctBy(IconClass) can only reduce (icons like "bi-cup-hot" repeat
        // across multiple categories), never grow, the raw total.
        result.Count.Should().BeLessThanOrEqualTo(totalRaw);
        result.Should().NotBeEmpty();
    }

    // --- GetIconsForCategory ---

    [Fact]
    public void GetIconsForCategory_ExactMatch_ReturnsCategoryIcons()
    {
        var result = CategoryIcons.GetIconsForCategory("Lebensmittel");

        result.Should().BeSameAs(CategoryIcons.IconsByCategory["Lebensmittel"]);
    }

    [Fact]
    public void GetIconsForCategory_CategoryNameContainingKnownKeyAsSubstring_MatchesByContains()
    {
        // The lookup does categoryName.Contains(key), not the other way round,
        // so a longer, more specific category name still resolves.
        var result = CategoryIcons.GetIconsForCategory("Bio-Lebensmittel Regal 3");

        result.Should().BeSameAs(CategoryIcons.IconsByCategory["Lebensmittel"]);
    }

    [Fact]
    public void GetIconsForCategory_IsCaseInsensitive()
    {
        var result = CategoryIcons.GetIconsForCategory("LEBENSMITTEL");

        result.Should().BeSameAs(CategoryIcons.IconsByCategory["Lebensmittel"]);
    }

    [Fact]
    public void GetIconsForCategory_UnknownCategory_FallsBackToAllgemein()
    {
        var result = CategoryIcons.GetIconsForCategory("Voellig Unbekannte Kategorie XYZ");

        result.Should().BeSameAs(CategoryIcons.IconsByCategory["Allgemein"]);
    }

    [Fact]
    public void GetIconsForCategory_EmptyString_FallsBackToAllgemein()
    {
        var result = CategoryIcons.GetIconsForCategory(string.Empty);

        result.Should().BeSameAs(CategoryIcons.IconsByCategory["Allgemein"]);
    }

    /// <summary>Regression test: a null category name used to hit
    /// categoryName.Contains(...) unguarded and throw NRE; it now falls back
    /// to "Allgemein" exactly like an empty or unmatched string.</summary>
    [Fact]
    public void GetIconsForCategory_NullCategoryName_FallsBackToAllgemein()
    {
        var result = CategoryIcons.GetIconsForCategory(null);

        result.Should().BeSameAs(CategoryIcons.IconsByCategory["Allgemein"]);
    }

    // --- GetDefaultIconForCategory ---

    [Fact]
    public void GetDefaultIconForCategory_KnownCategory_ReturnsFirstIconInList()
    {
        var expected = CategoryIcons.IconsByCategory["Lebensmittel"].First().IconClass;

        var result = CategoryIcons.GetDefaultIconForCategory("Lebensmittel");

        result.Should().Be(expected);
    }

    [Fact]
    public void GetDefaultIconForCategory_UnknownCategory_ReturnsFirstAllgemeinIcon()
    {
        var expected = CategoryIcons.IconsByCategory["Allgemein"].First().IconClass;

        var result = CategoryIcons.GetDefaultIconForCategory("Nonexistent Category");

        result.Should().Be(expected);
    }

    // --- CategoryIconInfo ---

    [Fact]
    public void CategoryIconInfo_Constructor_AssignsProperties()
    {
        var info = new CategoryIconInfo("bi-test", "Test Icon");

        info.IconClass.Should().Be("bi-test");
        info.Name.Should().Be("Test Icon");
    }
}
