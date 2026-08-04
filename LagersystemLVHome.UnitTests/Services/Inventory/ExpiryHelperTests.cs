namespace LagersystemLVHome.UnitTests.Services.Inventory;

public class ExpiryHelperTests
{
    [Theory]
    [InlineData("Lebensmittel", true)]
    [InlineData("LEBENSMITTEL", true)]
    [InlineData("Wein & Spirituosen", true)]
    [InlineData("Kosmetik & Pflege", true)]
    [InlineData("Babynahrung", true)]
    [InlineData("Tierfutter", true)]
    [InlineData("Reinigung", true)]
    [InlineData("Werkzeug", false)]
    [InlineData("Elektronik", false)]
    [InlineData("Möbel", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ShouldTrackExpiry_ByCategoryName(string? category, bool expected)
        => ExpiryHelper.ShouldTrackExpiry(category).Should().Be(expected);

    [Fact]
    public void ShouldTrackExpiry_ExplicitlyEnabled_AlwaysTrue()
    {
        ExpiryHelper.ShouldTrackExpiry("Werkzeug", explicitlyEnabled: true).Should().BeTrue();
        ExpiryHelper.ShouldTrackExpiry(null, explicitlyEnabled: true).Should().BeTrue();
    }

    [Fact]
    public void GetExpiryCategories_ReturnsNonEmptyList()
    {
        ExpiryHelper.GetExpiryCategories().Should().NotBeEmpty();
    }

    [Fact]
    public void ShouldProductTrackExpiry_UsesCategoryAndExplicitFlag()
    {
        var foodProduct = new Product
        {
            Name = "Milch",
            Category = new Category { Name = "Lebensmittel" }
        };
        ExpiryHelper.ShouldProductTrackExpiry(foodProduct).Should().BeTrue();

        var toolProduct = new Product
        {
            Name = "Hammer",
            Category = new Category { Name = "Werkzeug" }
        };
        ExpiryHelper.ShouldProductTrackExpiry(toolProduct).Should().BeFalse();

        var explicitProduct = new Product
        {
            Name = "Spezial",
            Category = new Category { Name = "Werkzeug" },
            TrackExpiryDate = true
        };
        ExpiryHelper.ShouldProductTrackExpiry(explicitProduct).Should().BeTrue();
    }
}
