using System.Net;
using System.Net.Http;
using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Integration;

public class OllamaServiceTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<string> RequestUris { get; } = new();
        public List<string> RequestBodies { get; } = new();
        public int CallCount { get; private set; }

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUris.Add(request.RequestUri?.ToString() ?? string.Empty);
            RequestBodies.Add(request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken) : string.Empty);
            return _responder(request);
        }
    }

    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static OllamaService Build(
        FakeHandler handler,
        IInventoryService? inventoryService = null,
        IDbContextFactory<InventoryDbContext>? contextFactory = null,
        string baseUrl = "http://localhost:11434")
    {
        var client = new HttpClient(handler);
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient("Ollama").Returns(client);
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(client);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OllamaSettings:BaseUrl"] = baseUrl
            })
            .Build();

        return new OllamaService(
            httpClientFactory,
            NullLogger<OllamaService>.Instance,
            inventoryService ?? Substitute.For<IInventoryService>(),
            contextFactory ?? CreateFactory(Guid.NewGuid().ToString()),
            configuration);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private const string ChatResponseJson = """{"message":{"role":"assistant","content":"Hallo, wie kann ich helfen?"},"done":true}""";

    private static async Task<IEnumerable<Product>> LoadAllProductsAsync(IDbContextFactory<InventoryDbContext> factory)
    {
        await using var db = factory.CreateDbContext();
        return await db.Products.Include(p => p.Category).AsNoTracking().ToListAsync();
    }

    private static async Task<IEnumerable<Category>> LoadAllCategoriesAsync(IDbContextFactory<InventoryDbContext> factory)
    {
        await using var db = factory.CreateDbContext();
        return await db.Categories.AsNoTracking().ToListAsync();
    }

    private static async Task<IEnumerable<Product>> LoadLowStockAsync(IDbContextFactory<InventoryDbContext> factory)
    {
        await using var db = factory.CreateDbContext();
        return await db.Products.Include(p => p.Category)
            .Where(p => p.Quantity <= p.MinQuantity)
            .AsNoTracking()
            .ToListAsync();
    }

    private static IInventoryService BuildInventoryServiceBackedBy(IDbContextFactory<InventoryDbContext> factory)
    {
        var inventoryService = Substitute.For<IInventoryService>();
        inventoryService.GetAllProductsAsync(Arg.Any<CancellationToken>()).Returns(_ => LoadAllProductsAsync(factory));
        inventoryService.GetAllCategoriesAsync(Arg.Any<CancellationToken>()).Returns(_ => LoadAllCategoriesAsync(factory));
        inventoryService.GetLowStockProductsAsync(Arg.Any<CancellationToken>()).Returns(_ => LoadLowStockAsync(factory));
        return inventoryService;
    }

    // --- ChatAsync / ChatWithHistoryAsync ---

    [Fact]
    public async Task ChatAsync_WithoutSystemPrompt_SendsUserMessageOnly()
    {
        var handler = new FakeHandler(_ => Json(ChatResponseJson));
        var sut = Build(handler);

        var result = await sut.ChatAsync("Hallo");

        result.Should().Be("Hallo, wie kann ich helfen?");
        handler.RequestBodies[0].Should().Contain("\"role\":\"user\"").And.NotContain("\"role\":\"system\"");
    }

    [Fact]
    public async Task ChatAsync_WithSystemPrompt_IncludesSystemMessageFirst()
    {
        var handler = new FakeHandler(_ => Json(ChatResponseJson));
        var sut = Build(handler);

        var result = await sut.ChatAsync("Hallo", systemPrompt: "Du bist hilfreich");

        result.Should().Be("Hallo, wie kann ich helfen?");
        handler.RequestBodies[0].Should().Contain("\"role\":\"system\"").And.Contain("Du bist hilfreich");
    }

    [Fact]
    public async Task ChatAsync_PostsToChatEndpointOnConfiguredBaseUrl()
    {
        var handler = new FakeHandler(_ => Json(ChatResponseJson));
        var sut = Build(handler, baseUrl: "http://ollama-host:11434");

        await sut.ChatAsync("Hallo");

        handler.RequestUris[0].Should().Be("http://ollama-host:11434/api/chat");
    }

    [Fact]
    public async Task ChatAsync_NonSuccessStatusCode_ReturnsErrorWithStatusCode()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var sut = Build(handler);

        var result = await sut.ChatAsync("Hallo");

        result.Should().Be("Error: Ollama API returned ServiceUnavailable");
    }

    [Fact]
    public async Task ChatAsync_NullResponseBody_ReturnsNoResponseFallback()
    {
        var handler = new FakeHandler(_ => Json("null"));
        var sut = Build(handler);

        var result = await sut.ChatAsync("Hallo");

        result.Should().Be("No response from Ollama");
    }

    [Fact]
    public async Task ChatAsync_HttpRequestException_ReturnsConnectionErrorMessage()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("connection refused"));
        var sut = Build(handler, baseUrl: "http://localhost:11434");

        var result = await sut.ChatAsync("Hallo");

        result.Should().Contain("Cannot connect to Ollama").And.Contain("http://localhost:11434");
    }

    [Fact]
    public async Task ChatAsync_MalformedJsonResponse_ReturnsGenericErrorMessage()
    {
        var handler = new FakeHandler(_ => Json("{not valid json"));
        var sut = Build(handler);

        var result = await sut.ChatAsync("Hallo");

        result.Should().StartWith("Error:");
    }

    [Fact]
    public async Task ChatWithHistoryAsync_SerializesAllMessagesWithSpecifiedModel()
    {
        var handler = new FakeHandler(_ => Json(ChatResponseJson));
        var sut = Build(handler);
        var history = new List<ChatMessage>
        {
            new() { Role = "user", Content = "Erste Frage" },
            new() { Role = "assistant", Content = "Erste Antwort" },
            new() { Role = "user", Content = "Zweite Frage" }
        };

        var result = await sut.ChatWithHistoryAsync(history, model: "custom-model");

        result.Should().Be("Hallo, wie kann ich helfen?");
        handler.RequestBodies[0].Should()
            .Contain("\"model\":\"custom-model\"")
            .And.Contain("Erste Frage")
            .And.Contain("Erste Antwort")
            .And.Contain("Zweite Frage");
    }

    // --- AskInventoryQuestionAsync ---

    [Fact]
    public async Task AskInventoryQuestionAsync_WithFullInventory_BuildsContextAndReturnsAnswer()
    {
        var factory = CreateFactory(nameof(AskInventoryQuestionAsync_WithFullInventory_BuildsContextAndReturnsAnswer));
        await using (var db = factory.CreateDbContext())
        {
            db.Categories.Add(new Category { Id = 1, Name = "Lebensmittel" });
            db.Products.AddRange(
                new Product { Id = 1, Name = "Milch", Quantity = 2, MinQuantity = 5, Price = 1.5m, CategoryId = 1, WarehouseId = 1, Barcode = "111" },
                new Product { Id = 2, Name = "Butter", Quantity = 20, MinQuantity = 5, Price = 2.5m, CategoryId = 1, WarehouseId = 1, Barcode = "222" },
                new Product { Id = 3, Name = "Mehl", Quantity = 10, MinQuantity = 5, Price = 0.9m, CategoryId = 1, WarehouseId = 1, Barcode = "333" });
            db.StorageLocations.Add(new StorageLocation { Id = 1, Code = "A1", Name = "Regal 1", Room = "Keller", WarehouseId = 1 });
            db.ProductStorageLocations.Add(new ProductStorageLocation { ProductId = 1, StorageLocationId = 1, Quantity = 2 });
            db.ProductBatches.AddRange(
                new ProductBatch { ProductId = 1, BatchNumber = "B-EXPIRED", Quantity = 2, ExpiryDate = DateTime.UtcNow.AddDays(-2), WarehouseId = 1 },
                new ProductBatch { ProductId = 2, BatchNumber = "B-SOON", Quantity = 5, ExpiryDate = DateTime.UtcNow.AddDays(3), WarehouseId = 1 },
                new ProductBatch { ProductId = 2, BatchNumber = "B-MONTH", Quantity = 5, ExpiryDate = DateTime.UtcNow.AddDays(20), WarehouseId = 1 },
                new ProductBatch { ProductId = 3, BatchNumber = "B-NOEXPIRY", Quantity = 3, ExpiryDate = null, WarehouseId = 1 });
            await db.SaveChangesAsync();
        }

        var handler = new FakeHandler(_ => Json(ChatResponseJson));
        var sut = Build(handler, BuildInventoryServiceBackedBy(factory), factory);

        var result = await sut.AskInventoryQuestionAsync("Wo ist die Milch?");

        result.Should().Be("Hallo, wie kann ich helfen?");
        var body = handler.RequestBodies[0];
        body.Should().Contain("Milch");
        body.Should().Contain("KRITISCH");
        body.Should().Contain("Regal 1");
        body.Should().Contain("Keller");
        body.Should().Contain("B-EXPIRED");
        body.Should().Contain("B-SOON");
        body.Should().Contain("B-MONTH");
        body.Should().Contain("B-NOEXPIRY");
        body.Should().Contain("Kein MHD");
        body.Should().Contain("Kein Lagerplatz zugewiesen");
    }

    [Fact]
    public async Task AskInventoryQuestionAsync_EmptyInventory_UsesFallbackMessages()
    {
        var factory = CreateFactory(nameof(AskInventoryQuestionAsync_EmptyInventory_UsesFallbackMessages));
        var handler = new FakeHandler(_ => Json(ChatResponseJson));
        var sut = Build(handler, BuildInventoryServiceBackedBy(factory), factory);

        var result = await sut.AskInventoryQuestionAsync("Was gibt es im Lager?");

        result.Should().Be("Hallo, wie kann ich helfen?");
        // The request body is JSON, so System.Text.Json's default encoder escapes
        // umlauts as \uXXXX sequences - assert on the surrounding ASCII text
        // rather than the accented characters themselves.
        var body = handler.RequestBodies[0];
        body.Should().Contain("Keine abgelaufenen Chargen");
        body.Should().Contain("Keine Chargen laufen in den");
        body.Should().Contain("Monat ab");
        body.Should().Contain("Keine kritischen Best");
    }

    // --- Simple prompt-building passthroughs ---

    [Theory]
    [InlineData(null)]
    [InlineData("Elektronik")]
    public async Task GenerateProductDescriptionAsync_ReturnsChatResult(string? category)
    {
        var handler = new FakeHandler(_ => Json(ChatResponseJson));
        var sut = Build(handler);

        var result = await sut.GenerateProductDescriptionAsync("Toaster", category);

        result.Should().Be("Hallo, wie kann ich helfen?");
        handler.RequestBodies[0].Should().Contain("Toaster");
        if (category != null)
        {
            handler.RequestBodies[0].Should().Contain(category);
        }
    }

    [Fact]
    public async Task SuggestOptimalStorageAsync_ReturnsChatResult()
    {
        var handler = new FakeHandler(_ => Json(ChatResponseJson));
        var sut = Build(handler);

        var result = await sut.SuggestOptimalStorageAsync("Toaster", "Elektronik");

        result.Should().Be("Hallo, wie kann ich helfen?");
        handler.RequestBodies[0].Should().Contain("Toaster").And.Contain("Elektronik");
    }

    [Fact]
    public async Task AnalyzeInventoryTrendsAsync_IncludesRecentMovementCount()
    {
        var handler = new FakeHandler(_ => Json(ChatResponseJson));
        var inventoryService = Substitute.For<IInventoryService>();
        inventoryService.GetRecentMovementsAsync(100, Arg.Any<CancellationToken>())
            .Returns(new List<StockMovement> { new(), new(), new() });
        var sut = Build(handler, inventoryService);

        var result = await sut.AnalyzeInventoryTrendsAsync();

        result.Should().Be("Hallo, wie kann ich helfen?");
        handler.RequestBodies[0].Should().Contain("3");
    }

    [Fact]
    public async Task PredictReorderNeedsAsync_IncludesLowStockCount()
    {
        var handler = new FakeHandler(_ => Json(ChatResponseJson));
        var inventoryService = Substitute.For<IInventoryService>();
        inventoryService.GetLowStockProductsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Product> { new(), new() });
        var sut = Build(handler, inventoryService);

        var result = await sut.PredictReorderNeedsAsync();

        result.Should().Be("Hallo, wie kann ich helfen?");
        handler.RequestBodies[0].Should().Contain("2");
    }

    [Fact]
    public async Task ConvertToSqlQueryAsync_IncludesOriginalQueryInPrompt()
    {
        var handler = new FakeHandler(_ => Json(ChatResponseJson));
        var sut = Build(handler);

        var result = await sut.ConvertToSqlQueryAsync("Zeige alle Produkte mit niedrigem Bestand");

        result.Should().Be("Hallo, wie kann ich helfen?");
        handler.RequestBodies[0].Should().Contain("Zeige alle Produkte mit niedrigem Bestand");
    }

    // --- GetAvailableModelsAsync ---

    [Fact]
    public async Task GetAvailableModelsAsync_Success_ReturnsModels()
    {
        const string tagsJson = """
        {"models":[{"name":"llama3.2","model":"llama3.2:latest","modified_at":"2024-01-01T00:00:00Z","size":123,"digest":"abc"}]}
        """;
        var handler = new FakeHandler(_ => Json(tagsJson));
        var sut = Build(handler);

        var result = await sut.GetAvailableModelsAsync();

        result.Should().ContainSingle().Which.Name.Should().Be("llama3.2");
    }

    [Fact]
    public async Task GetAvailableModelsAsync_NonSuccessStatus_ReturnsEmptyList()
    {
        var sut = Build(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        (await sut.GetAvailableModelsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task GetAvailableModelsAsync_EmptyModelsArray_ReturnsEmptyList()
    {
        var sut = Build(new FakeHandler(_ => Json("""{"models":[]}""")));

        (await sut.GetAvailableModelsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task GetAvailableModelsAsync_ThrowsException_ReturnsEmptyList()
    {
        var sut = Build(new FakeHandler(_ => throw new HttpRequestException("down")));

        (await sut.GetAvailableModelsAsync()).Should().BeEmpty();
    }

    // --- PullModelAsync ---

    [Fact]
    public async Task PullModelAsync_Success_ReturnsTrue()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = Build(handler);

        var result = await sut.PullModelAsync("llama3.2");

        result.Should().BeTrue();
        handler.RequestBodies[0].Should().Contain("llama3.2");
    }

    [Fact]
    public async Task PullModelAsync_NonSuccessStatus_ReturnsFalse()
    {
        var sut = Build(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        (await sut.PullModelAsync("missing-model")).Should().BeFalse();
    }

    [Fact]
    public async Task PullModelAsync_ThrowsException_ReturnsFalse()
    {
        var sut = Build(new FakeHandler(_ => throw new HttpRequestException("down")));

        (await sut.PullModelAsync("llama3.2")).Should().BeFalse();
    }

    // --- IsModelAvailableAsync ---

    [Fact]
    public async Task IsModelAvailableAsync_ModelPresent_ReturnsTrue()
    {
        const string tagsJson = """{"models":[{"name":"Llama3.2:latest"}]}""";
        var sut = Build(new FakeHandler(_ => Json(tagsJson)));

        (await sut.IsModelAvailableAsync("llama3.2")).Should().BeTrue();
    }

    [Fact]
    public async Task IsModelAvailableAsync_ModelAbsent_ReturnsFalse()
    {
        const string tagsJson = """{"models":[{"name":"mistral"}]}""";
        var sut = Build(new FakeHandler(_ => Json(tagsJson)));

        (await sut.IsModelAvailableAsync("llama3.2")).Should().BeFalse();
    }

    // --- GetModelInfoAsync ---

    [Fact]
    public async Task GetModelInfoAsync_Success_ReturnsParsedInfo()
    {
        const string infoJson = """{"name":"llama3.2","template":"tmpl","parameters":"p","size":42}""";
        var handler = new FakeHandler(_ => Json(infoJson));
        var sut = Build(handler);

        var result = await sut.GetModelInfoAsync("llama3.2");

        result.Name.Should().Be("llama3.2");
        result.Template.Should().Be("tmpl");
        result.Size.Should().Be(42);
        handler.RequestBodies[0].Should().Contain("llama3.2");
    }

    [Fact]
    public async Task GetModelInfoAsync_NonSuccessStatus_ReturnsNameOnlyFallback()
    {
        var sut = Build(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        var result = await sut.GetModelInfoAsync("missing-model");

        result.Name.Should().Be("missing-model");
        result.Template.Should().BeEmpty();
    }

    [Fact]
    public async Task GetModelInfoAsync_ThrowsException_ReturnsNameOnlyFallback()
    {
        var sut = Build(new FakeHandler(_ => throw new HttpRequestException("down")));

        var result = await sut.GetModelInfoAsync("broken-model");

        result.Name.Should().Be("broken-model");
    }

    // --- IsOllamaRunningAsync ---

    [Fact]
    public async Task IsOllamaRunningAsync_SuccessStatus_ReturnsTrue()
    {
        var sut = Build(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        (await sut.IsOllamaRunningAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task IsOllamaRunningAsync_NonSuccessStatus_ReturnsFalse()
    {
        var sut = Build(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        (await sut.IsOllamaRunningAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task IsOllamaRunningAsync_ThrowsException_ReturnsFalse()
    {
        var sut = Build(new FakeHandler(_ => throw new HttpRequestException("down")));

        (await sut.IsOllamaRunningAsync()).Should().BeFalse();
    }

    // --- GetStatusAsync ---

    [Fact]
    public async Task GetStatusAsync_Running_ReturnsVersionAndModels()
    {
        var handler = new FakeHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.Contains("/api/version"))
            {
                return Json("""{"version":"0.5.1"}""");
            }
            return Json("""{"models":[{"name":"llama3.2"}]}""");
        });
        var sut = Build(handler);

        var status = await sut.GetStatusAsync();

        status.IsRunning.Should().BeTrue();
        status.Version.Should().Be("0.5.1");
        status.AvailableModels.Should().ContainSingle().Which.Should().Be("llama3.2");
    }

    [Fact]
    public async Task GetStatusAsync_NotRunning_DoesNotFetchModels()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var sut = Build(handler);

        var status = await sut.GetStatusAsync();

        status.IsRunning.Should().BeFalse();
        status.AvailableModels.Should().BeEmpty();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetStatusAsync_ThrowsException_ReturnsErrorStatus()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("network unreachable"));
        var sut = Build(handler);

        var status = await sut.GetStatusAsync();

        status.IsRunning.Should().BeFalse();
        status.Error.Should().Contain("network unreachable");
    }
}
