using LagersystemLVHome.Data;
using LagersystemLVHome.Infrastructure.ML.Keywords;
using LagersystemLVHome.Infrastructure.ML.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ExceptionExtensions;

namespace LagersystemLVHome.UnitTests.ML;

/// <summary>
/// <see cref="CategoryPredictionService"/> combines a real (reflection-discovered)
/// <see cref="CategoryKeywordService"/> with an optional ML.NET SDCA multiclass model.
/// Each test gets an isolated temp "ContentRootPath" so the on-disk model file
/// (<c>ML/Data/category-prediction-model.zip</c>) never leaks between tests.
/// </summary>
public class CategoryPredictionServiceTests : IDisposable
{
    private readonly List<string> _tempRoots = new();
    private readonly List<SqliteContextFactory> _sqliteFactories = new();

    public void Dispose()
    {
        foreach (var root in _tempRoots)
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
        foreach (var factory in _sqliteFactories)
        {
            factory.Dispose();
        }
        GC.SuppressFinalize(this);
    }

    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        // Prefixed with the class name: EF Core's InMemory provider keys databases by name in
        // a store shared across the whole test process, so an unqualified nameof(TestMethod)
        // can collide with an identically-named test in a different test class.
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>()
                .UseInMemoryDatabase(nameof(CategoryPredictionServiceTests) + "." + name).Options);

    // A real relational provider is needed to prove FindSimilarProductsAsync's query actually
    // translates and runs case-insensitively - InMemory can't translate this query shape at all
    // (see FindSimilarProductsAsync_UnderInMemoryProvider_QueryIsUntranslatable_ReturnsEmpty).
    private sealed class SqliteContextFactory : IDbContextFactory<InventoryDbContext>, IDisposable
    {
        private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
        private readonly DbContextOptions<InventoryDbContext> _options;

        public SqliteContextFactory()
        {
            _connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
            _connection.Open();
            _options = new DbContextOptionsBuilder<InventoryDbContext>().UseSqlite(_connection).Options;
            using var ctx = new InventoryDbContext(_options);
            ctx.Database.EnsureCreated();
        }

        public InventoryDbContext CreateDbContext() => new(_options);

        public void Dispose() => _connection.Dispose();
    }

    private SqliteContextFactory CreateSqliteFactory()
    {
        var factory = new SqliteContextFactory();
        _sqliteFactories.Add(factory);
        return factory;
    }

    private static readonly CategoryKeywordService KeywordService =
        new(NullLogger<CategoryKeywordService>.Instance);

    private string NewTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "lg-catpred-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        _tempRoots.Add(root);
        return root;
    }

    private IWebHostEnvironment EnvFor(string contentRoot)
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.ContentRootPath.Returns(contentRoot);
        return env;
    }

    private CategoryPredictionService CreateSut(
        IDbContextFactory<InventoryDbContext> factory,
        string? contentRoot = null,
        CategoryKeywordService? keywordService = null)
        => new(factory, NullLogger<CategoryPredictionService>.Instance, EnvFor(contentRoot ?? NewTempRoot()), keywordService ?? KeywordService);

    private static Warehouse MakeWarehouse(int id = 1) => new() { Id = id, Name = "WH" + id, Address = "a" };

    private static Category MakeCategory(int id, string name) => new() { Id = id, Name = name, Icon = "x", WarehouseId = 1 };

    private static Product MakeProduct(int id, string name, int categoryId, string description = "", string barcode = "") => new()
    {
        Id = id,
        Name = name,
        CategoryId = categoryId,
        WarehouseId = 1,
        Description = description,
        Barcode = barcode,
        Price = 1
    };

    private static async Task SeedCategoriesAsync(IDbContextFactory<InventoryDbContext> factory, params Category[] categories)
    {
        await using var db = factory.CreateDbContext();
        db.Warehouses.Add(MakeWarehouse());
        db.Categories.AddRange(categories);
        await db.SaveChangesAsync();
    }

    // ---------------------------------------------------------------
    // Readiness flags
    // ---------------------------------------------------------------

    [Fact]
    public void IsModelReady_TrueAssoonAsKeywordServiceIsPresent()
    {
        var sut = CreateSut(CreateFactory(nameof(IsModelReady_TrueAssoonAsKeywordServiceIsPresent)));

        sut.IsModelReady.Should().BeTrue();
    }

    [Fact]
    public void IsMlModelTrained_FalseWhenNoModelFileExists()
    {
        var sut = CreateSut(CreateFactory(nameof(IsMlModelTrained_FalseWhenNoModelFileExists)));

        sut.IsMlModelTrained.Should().BeFalse();
    }

    // ---------------------------------------------------------------
    // SuggestCategoriesAsync - keyword-only mode (no trained ML model)
    // ---------------------------------------------------------------

    [Fact]
    public async Task SuggestCategoriesAsync_KeywordMode_MatchesCategoryByName()
    {
        var factory = CreateFactory(nameof(SuggestCategoriesAsync_KeywordMode_MatchesCategoryByName));
        await SeedCategoriesAsync(factory, MakeCategory(1, "Batterien"), MakeCategory(2, "Elektronik"));
        var sut = CreateSut(factory);

        var result = await sut.SuggestCategoriesAsync("AA Batterie Mignon 4er Pack");

        result.Suggestions.Should().NotBeEmpty();
        result.BestMatch!.CategoryName.Should().Be("Batterien");
        result.BestMatch.Confidence.Should().BeInRange(0, 95);
        result.BestMatch.Reasons.Should().ContainSingle(r => r.Contains("Schlüsselwörter"));
    }

    [Fact]
    public async Task SuggestCategoriesAsync_KeywordMode_NoMatch_ReturnsNoSuggestions()
    {
        var factory = CreateFactory(nameof(SuggestCategoriesAsync_KeywordMode_NoMatch_ReturnsNoSuggestions));
        await SeedCategoriesAsync(factory, MakeCategory(1, "Batterien"));
        var sut = CreateSut(factory);

        var result = await sut.SuggestCategoriesAsync("Zzzznonexistentgibberishword12345");

        result.Suggestions.Should().BeEmpty();
        result.BestMatch.Should().BeNull();
    }

    [Fact]
    public async Task SuggestCategoriesAsync_KeywordMode_DescriptionAddsConfidenceBonus()
    {
        var factory = CreateFactory(nameof(SuggestCategoriesAsync_KeywordMode_DescriptionAddsConfidenceBonus));
        await SeedCategoriesAsync(factory, MakeCategory(1, "Batterien"));
        var sut = CreateSut(factory);

        var withoutDescription = await sut.SuggestCategoriesAsync("AAA Akku");
        var withDescription = await sut.SuggestCategoriesAsync("AAA Akku", description: "Wiederaufladbare Batterie fuer Fernbedienung");

        withDescription.BestMatch!.Confidence.Should().BeGreaterThanOrEqualTo(withoutDescription.BestMatch!.Confidence);
    }

    [Fact]
    public async Task SuggestCategoriesAsync_KeywordMode_CategoryWithoutKeywordProvider_IsSkipped()
    {
        var factory = CreateFactory(nameof(SuggestCategoriesAsync_KeywordMode_CategoryWithoutKeywordProvider_IsSkipped));
        await SeedCategoriesAsync(factory,
            MakeCategory(1, "Batterien"),
            MakeCategory(2, "Kategorie Ohne Provider " + Guid.NewGuid()));
        var sut = CreateSut(factory);

        var result = await sut.SuggestCategoriesAsync("AA Batterie");

        result.Suggestions.Should().OnlyContain(s => s.CategoryName == "Batterien");
    }

    [Fact]
    public async Task SuggestCategoriesAsync_KeywordMode_TopFiveOrderedByConfidenceDescending()
    {
        var factory = CreateFactory(nameof(SuggestCategoriesAsync_KeywordMode_TopFiveOrderedByConfidenceDescending));
        await SeedCategoriesAsync(factory,
            MakeCategory(1, "Batterien"),
            MakeCategory(2, "Elektronik"),
            MakeCategory(3, "Werkzeug"),
            MakeCategory(4, "Spielzeug"),
            MakeCategory(5, "Garten"),
            MakeCategory(6, "Haushalt"));
        var sut = CreateSut(factory);

        // A grab-bag of keywords spanning several categories.
        var result = await sut.SuggestCategoriesAsync(
            "Batterie Akku Laptop Computer Hammer Schraubendreher Spielzeugauto Rasenmaeher Putzeimer");

        result.Suggestions.Should().HaveCountLessThanOrEqualTo(5);
        result.Suggestions.Select(s => s.Confidence).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task SuggestCategoriesAsync_KeywordMode_ManyWeakMatches_AppliesSpamPenalty()
    {
        // CalculateMatchQuality scores each match on keyword length, whole-word-ness, leading
        // position, and text coverage. "3v","9v","aa","wh","cr2","lr1" are all real Batteries
        // keywords that are short (length 2-3, so a low base lengthScore) and, embedded here as
        // non-whole-word substrings inside one long token with no keyword at the very start,
        // earn none of the bonus points either - six matches with an average quality of ~0.17.
        // That is both >5 matches and <0.3 average quality, so the spam-detection penalty
        // (confidence *= 0.7) in GetRuleBasedSuggestionsAsync fires. This asserts the penalized
        // confidence is measurably lower than what the same match *count* alone would imply.
        var factory = CreateFactory(nameof(SuggestCategoriesAsync_KeywordMode_ManyWeakMatches_AppliesSpamPenalty));
        await SeedCategoriesAsync(factory, MakeCategory(1, "Batterien"));
        var sut = CreateSut(factory);

        var result = await sut.SuggestCategoriesAsync("x3vxx9vxxaaxxwhxxcr2xxlr1x");

        result.BestMatch.Should().NotBeNull();
        result.BestMatch!.CategoryName.Should().Be("Batterien");
        // Without the 0.7x penalty this would be capped at 95; a mid-30s value confirms the
        // penalty (not just the normal 95-cap) is what shaped this confidence.
        result.BestMatch.Confidence.Should().BeLessThan(50);
    }

    [Fact]
    public async Task SuggestCategoriesAsync_DbFailure_ReturnsEmptyResult()
    {
        var factory = Substitute.For<IDbContextFactory<InventoryDbContext>>();
        factory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .ThrowsAsyncForAnyArgs(new InvalidOperationException("db down"));
        var sut = CreateSut(factory);

        var result = await sut.SuggestCategoriesAsync("AA Batterie");

        result.Suggestions.Should().BeEmpty();
        result.ProductName.Should().Be("AA Batterie");
    }

    // ---------------------------------------------------------------
    // TrainModelAsync / hybrid ML+keyword mode
    // ---------------------------------------------------------------

    [Fact]
    public async Task TrainModelAsync_FewerThanTenCategorizedProducts_ReturnsFalse()
    {
        var factory = CreateFactory(nameof(TrainModelAsync_FewerThanTenCategorizedProducts_ReturnsFalse));
        await SeedCategoriesAsync(factory, MakeCategory(1, "Batterien"));
        await using (var db = factory.CreateDbContext())
        {
            for (int i = 1; i <= 9; i++)
                db.Products.Add(MakeProduct(i, $"Batterie {i}", 1));
            await db.SaveChangesAsync();
        }
        var sut = CreateSut(factory);

        var result = await sut.TrainModelAsync();

        result.Should().BeFalse();
        sut.IsMlModelTrained.Should().BeFalse();
    }

    [Fact]
    public async Task TrainModelAsync_EnoughCategorizedProducts_TrainsAndPersistsModel()
    {
        var contentRoot = NewTempRoot();
        var factory = CreateFactory(nameof(TrainModelAsync_EnoughCategorizedProducts_TrainsAndPersistsModel));
        await SeedCategoriesAsync(factory, MakeCategory(1, "Batterien"), MakeCategory(2, "Elektronik"));
        await using (var db = factory.CreateDbContext())
        {
            var battNames = new[] { "AA Batterie Mignon", "AAA Batterie Micro", "9V Blockbatterie", "Akku Wiederaufladbar", "Knopfzelle CR2032", "Lithium Batterie AA" };
            var elecNames = new[] { "Laptop Notebook 15 Zoll", "USB Kabel Ladekabel", "Bluetooth Kopfhoerer", "HDMI Adapter", "Wireless Maus" };
            var id = 1;
            foreach (var n in battNames) db.Products.Add(MakeProduct(id++, n, 1, description: "Batterie Zubehoer"));
            foreach (var n in elecNames) db.Products.Add(MakeProduct(id++, n, 2, description: "Elektronik Zubehoer"));
            await db.SaveChangesAsync();
        }
        var sut = CreateSut(factory, contentRoot);

        var result = await sut.TrainModelAsync();

        result.Should().BeTrue();
        sut.IsMlModelTrained.Should().BeTrue();
        File.Exists(Path.Combine(contentRoot, "ML", "Data", "category-prediction-model.zip")).Should().BeTrue();
    }

    [Fact]
    public async Task TrainModelAsync_DbFailure_ReturnsFalse()
    {
        var factory = Substitute.For<IDbContextFactory<InventoryDbContext>>();
        factory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .ThrowsAsyncForAnyArgs(new InvalidOperationException("db down"));
        var sut = CreateSut(factory);

        var result = await sut.TrainModelAsync();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SuggestCategoriesAsync_ImmediatelyAfterTrainingOnSameInstance_UsesGenuineMlPrediction()
    {
        // Regression test: CategoryPredictionInput used to lack a "Label" property, so
        // building a PredictionEngine<CategoryPredictionInput, ...> from the trained
        // pipeline (whose first step, MapValueToKey("Label"), requires that input column)
        // always threw ArgumentOutOfRangeException("Could not find input column 'Label'") -
        // both right after training and after a save/reload round trip. CategoryPredictionInput
        // now has an (unused-at-prediction-time) Label property to satisfy that schema
        // requirement, so the ML model now genuinely participates instead of the service
        // silently and permanently falling back to keyword-only suggestions.
        var contentRoot = NewTempRoot();
        var factory = CreateFactory(nameof(SuggestCategoriesAsync_ImmediatelyAfterTrainingOnSameInstance_UsesGenuineMlPrediction));
        await SeedCategoriesAsync(factory, MakeCategory(1, "Batterien"), MakeCategory(2, "Elektronik"));
        await using (var db = factory.CreateDbContext())
        {
            var battNames = new[] { "AA Batterie Mignon", "AAA Batterie Micro", "9V Blockbatterie", "Akku Wiederaufladbar", "Knopfzelle CR2032", "Lithium Batterie AA" };
            var elecNames = new[] { "Laptop Notebook 15 Zoll", "USB Kabel Ladekabel", "Bluetooth Kopfhoerer", "HDMI Adapter", "Wireless Maus" };
            var id = 1;
            foreach (var n in battNames) db.Products.Add(MakeProduct(id++, n, 1, description: "Batterie Zubehoer"));
            foreach (var n in elecNames) db.Products.Add(MakeProduct(id++, n, 2, description: "Elektronik Zubehoer"));
            await db.SaveChangesAsync();
        }
        var sut = CreateSut(factory, contentRoot);
        (await sut.TrainModelAsync()).Should().BeTrue();

        var result = await sut.SuggestCategoriesAsync("AA Batterie Akku Mignon", description: "Batterie Zubehoer");

        sut.IsMlModelTrained.Should().BeTrue();
        result.Suggestions.Should().NotBeEmpty();
        result.BestMatch!.CategoryName.Should().Be("Batterien");
        result.BestMatch.Reasons.Should().Contain(r => r.Contains("ML-Modell"), "the ML prediction engine now builds successfully and genuinely contributes");
    }

    [Fact]
    public async Task Constructor_ReloadingTrainedModel_SucceedsAndModelFileSurvivesRestart()
    {
        // Regression test for the second, more serious consequence of the same "Label"
        // schema mismatch: LoadModelIfExists used to hit the identical
        // ArgumentOutOfRangeException on every reload of a model trained by this exact
        // pipeline (not just legacy ones) and its handler deleted the just-trained model
        // file - so a trained model never survived a single app restart. Reload now
        // succeeds outright, the file is left in place, and the reloaded instance can
        // genuinely predict.
        var contentRoot = NewTempRoot();
        var factory = CreateFactory(nameof(Constructor_ReloadingTrainedModel_SucceedsAndModelFileSurvivesRestart));
        await SeedCategoriesAsync(factory, MakeCategory(1, "Batterien"), MakeCategory(2, "Elektronik"));
        await using (var db = factory.CreateDbContext())
        {
            var battNames = new[] { "AA Batterie Mignon", "AAA Batterie Micro", "9V Blockbatterie", "Akku Wiederaufladbar", "Knopfzelle CR2032", "Lithium Batterie AA" };
            var elecNames = new[] { "Laptop Notebook 15 Zoll", "USB Kabel Ladekabel", "Bluetooth Kopfhoerer", "HDMI Adapter", "Wireless Maus" };
            var id = 1;
            foreach (var n in battNames) db.Products.Add(MakeProduct(id++, n, 1, description: "Batterie Zubehoer"));
            foreach (var n in elecNames) db.Products.Add(MakeProduct(id++, n, 2, description: "Elektronik Zubehoer"));
            await db.SaveChangesAsync();
        }
        var trainer = CreateSut(factory, contentRoot);
        (await trainer.TrainModelAsync()).Should().BeTrue();
        var modelPath = Path.Combine(contentRoot, "ML", "Data", "category-prediction-model.zip");
        File.Exists(modelPath).Should().BeTrue("training just saved it");

        var reloaded = CreateSut(factory, contentRoot); // constructor -> LoadModelIfExists

        reloaded.IsMlModelTrained.Should().BeTrue();
        File.Exists(modelPath).Should().BeTrue("a successful reload must not delete the model file");

        var result = await reloaded.SuggestCategoriesAsync("AA Batterie Akku Mignon", description: "Batterie Zubehoer");
        result.BestMatch!.CategoryName.Should().Be("Batterien");
        result.BestMatch.Reasons.Should().Contain(r => r.Contains("ML-Modell"), "the reloaded instance's prediction engine must genuinely work, not just report IsMlModelTrained = true");
    }

    [Fact]
    public async Task Constructor_ReloadsPreviouslyTrainedModelFromDisk()
    {
        var contentRoot = NewTempRoot();
        var factory = CreateFactory(nameof(Constructor_ReloadsPreviouslyTrainedModelFromDisk));
        await SeedCategoriesAsync(factory, MakeCategory(1, "Batterien"), MakeCategory(2, "Elektronik"));
        await using (var db = factory.CreateDbContext())
        {
            var battNames = new[] { "AA Batterie Mignon", "AAA Batterie Micro", "9V Blockbatterie", "Akku Wiederaufladbar", "Knopfzelle CR2032", "Lithium Batterie AA" };
            var elecNames = new[] { "Laptop Notebook 15 Zoll", "USB Kabel Ladekabel", "Bluetooth Kopfhoerer", "HDMI Adapter", "Wireless Maus" };
            var id = 1;
            // Description must be non-empty for every row: ML.NET's text featurizer throws
            // an ArgumentOutOfRangeException ("Schema mismatch ... DescriptionFeaturized")
            // when a text column is empty for all training rows (see report).
            foreach (var n in battNames) db.Products.Add(MakeProduct(id++, n, 1, description: "Batterie Zubehoer"));
            foreach (var n in elecNames) db.Products.Add(MakeProduct(id++, n, 2, description: "Elektronik Zubehoer"));
            await db.SaveChangesAsync();
        }
        var trainer = CreateSut(factory, contentRoot);
        (await trainer.TrainModelAsync()).Should().BeTrue();

        var reloaded = CreateSut(factory, contentRoot);

        reloaded.IsMlModelTrained.Should().BeTrue();
    }

    [Fact]
    public void Constructor_CorruptModelFile_IsSwallowedAndModelStaysUntrained()
    {
        var contentRoot = NewTempRoot();
        var modelDir = Path.Combine(contentRoot, "ML", "Data");
        Directory.CreateDirectory(modelDir);
        File.WriteAllBytes(Path.Combine(modelDir, "category-prediction-model.zip"), new byte[] { 1, 2, 3, 4, 5 });
        var factory = CreateFactory(nameof(Constructor_CorruptModelFile_IsSwallowedAndModelStaysUntrained));
        CategoryPredictionService? sut = null;

        var act = () => sut = CreateSut(factory, contentRoot);

        act.Should().NotThrow();
        sut!.IsMlModelTrained.Should().BeFalse();
    }

    // ---------------------------------------------------------------
    // AutoCategorizeProductsAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task AutoCategorizeProductsAsync_NoUncategorizedProducts_ReturnsZero()
    {
        // Product.CategoryId is a non-nullable int, so the production query
        // `p.CategoryId == null` can never match any row (see report). This test
        // pins the resulting (always-zero) behaviour rather than the intent.
        var factory = CreateFactory(nameof(AutoCategorizeProductsAsync_NoUncategorizedProducts_ReturnsZero));
        await SeedCategoriesAsync(factory, MakeCategory(1, "Batterien"));
        await using (var db = factory.CreateDbContext())
        {
            db.Products.Add(MakeProduct(1, "AA Batterie", 1));
            await db.SaveChangesAsync();
        }
        var sut = CreateSut(factory);

        var count = await sut.AutoCategorizeProductsAsync();

        count.Should().Be(0);
    }

    [Fact]
    public async Task AutoCategorizeProductsAsync_DbFailure_Rethrows()
    {
        var factory = Substitute.For<IDbContextFactory<InventoryDbContext>>();
        factory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .ThrowsAsyncForAnyArgs(new InvalidOperationException("db down"));
        var sut = CreateSut(factory);

        var act = async () => await sut.AutoCategorizeProductsAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ---------------------------------------------------------------
    // FindSimilarProductsAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task FindSimilarProductsAsync_UnderInMemoryProvider_QueryIsUntranslatable_ReturnsEmpty()
    {
        // FindSimilarProductsAsync builds `words.Any(w => p.Name.Contains(w))` where `words`
        // is a captured local List<string>. EF Core's InMemory provider cannot translate that
        // shape (InvalidOperationException: "could not be translated") - relational providers
        // may handle it differently, but under InMemory this always lands in the method's own
        // catch block and yields an empty list, regardless of matching data. This test pins
        // that (InMemory-specific) behaviour; see report for the caveat.
        var factory = CreateFactory(nameof(FindSimilarProductsAsync_UnderInMemoryProvider_QueryIsUntranslatable_ReturnsEmpty));
        await SeedCategoriesAsync(factory, MakeCategory(1, "Batterien"));
        await using (var db = factory.CreateDbContext())
        {
            db.Products.Add(MakeProduct(1, "duracell batterie aa mignon", 1));
            await db.SaveChangesAsync();
        }
        var sut = CreateSut(factory);

        var result = await sut.FindSimilarProductsAsync("Duracell Batterie");

        result.Should().BeEmpty();
    }

    /// <summary>Regression test: `words` (extracted from the query) are always lowercased,
    /// but the production code used to compare them against p.Name (not lowercased), so a
    /// capitalized product name like "Batterie" never matched the lowercase keyword
    /// "batterie". Needs a real relational provider (SQLite) since InMemory can't translate
    /// this query shape at all regardless of case, see the test above.</summary>
    [Fact]
    public async Task FindSimilarProductsAsync_UnderSqlite_MatchesCaseInsensitively()
    {
        using var factory = CreateSqliteFactory();
        await SeedCategoriesAsync(factory, MakeCategory(1, "Batterien"));
        await using (var db = factory.CreateDbContext())
        {
            db.Products.Add(MakeProduct(1, "Duracell Batterie AA Mignon", 1));
            db.Products.Add(MakeProduct(2, "USB Kabel", 1));
            await db.SaveChangesAsync();
        }
        var sut = CreateSut(factory);

        var result = await sut.FindSimilarProductsAsync("Duracell Batterie");

        result.Should().ContainSingle().Which.Should().Be("Duracell Batterie AA Mignon");
    }

    [Fact]
    public async Task FindSimilarProductsAsync_DbContextCreationFailure_ReturnsEmptyList()
    {
        var factory = Substitute.For<IDbContextFactory<InventoryDbContext>>();
        factory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .ThrowsAsyncForAnyArgs(new InvalidOperationException("db down"));
        var sut = CreateSut(factory);

        var result = await sut.FindSimilarProductsAsync("Batterie");

        result.Should().BeEmpty();
    }
}
