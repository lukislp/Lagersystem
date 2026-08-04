using LagersystemLVHome.Infrastructure.ML.Keywords;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.ML;

/// <summary>
/// <see cref="CategoryKeywordService"/> discovers all
/// <see cref="ICategoryKeywordProvider"/> implementations in the main
/// assembly via reflection at construction time. These tests pin the
/// contract without depending on the exact set of categories shipped.
/// </summary>
public class CategoryKeywordServiceTests
{
    private readonly CategoryKeywordService _sut =
        new(NullLogger<CategoryKeywordService>.Instance);

    [Fact]
    public void Constructor_DiscoversAllProvidersFromMainAssembly()
    {
        _sut.GetAllCategories().Should().NotBeEmpty(
            "the main assembly ships several ICategoryKeywordProvider implementations");
    }

    [Fact]
    public void HasCategory_WithKnownCategory_ReturnsTrue()
    {
        _sut.HasCategory("Batterien").Should().BeTrue();
    }

    [Fact]
    public void HasCategory_WithUnknownCategory_ReturnsFalse()
    {
        _sut.HasCategory("Does not exist " + Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void GetKeywordsForCategory_WithKnownCategory_ReturnsNonEmptyList()
    {
        _sut.GetKeywordsForCategory("Batterien").Should().NotBeEmpty();
    }

    [Fact]
    public void GetKeywordsForCategory_WithUnknownCategory_ReturnsEmptyList()
    {
        _sut.GetKeywordsForCategory("Unknown " + Guid.NewGuid()).Should().BeEmpty();
    }

    [Fact]
    public void GetWeightedKeywordsForCategory_WithUnknownCategory_ReturnsEmptyDictionary()
    {
        _sut.GetWeightedKeywordsForCategory("Unknown " + Guid.NewGuid()).Should().BeEmpty();
    }

    [Fact]
    public void GetTotalKeywordCount_ReturnsPositive()
    {
        _sut.GetTotalKeywordCount().Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetAllProviders_ReturnsDefensiveCopy()
    {
        var a = _sut.GetAllProviders();
        var b = _sut.GetAllProviders();

        a.Should().NotBeSameAs(b, "GetAllProviders must return a copy so callers cannot mutate internal state");
        a.Should().BeEquivalentTo(b);
    }

    [Fact]
    public void Batterien_ContainsExpectedKeywords()
    {
        var kw = _sut.GetKeywordsForCategory("Batterien");

        kw.Should().Contain("batterie");
        kw.Should().Contain("akku");
    }
}
