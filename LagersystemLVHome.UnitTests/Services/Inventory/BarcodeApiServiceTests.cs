using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Inventory;

public class BarcodeApiServiceTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<string> RequestedUris { get; } = new();

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUris.Add(request.RequestUri!.ToString());
            return Task.FromResult(_responder(request));
        }
    }

    private static BarcodeApiService BuildSut(FakeHandler handler)
    {
        var client = new HttpClient(handler);
        return new BarcodeApiService(client, NullLogger<BarcodeApiService>.Instance);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task GetProductInfoAsync_EmptyBarcode_ReturnsNull()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = BuildSut(handler);

        var result = await sut.GetProductInfoAsync("");

        result.Should().BeNull();
        handler.RequestedUris.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProductInfoAsync_OpenFoodFactsHit_ReturnsProductInfo()
    {
        const string body = """
        {
          "status": 1,
          "product": {
            "product_name": "Bio Apfelsaft",
            "brands": "Acme",
            "categories": "Beverages, juices",
            "quantity": "1L",
            "packaging": "Glas",
            "ingredients_text": "Apfel, Wasser",
            "image_url": "https://example.com/img.jpg"
          }
        }
        """;
        var handler = new FakeHandler(_ => Json(body));
        var sut = BuildSut(handler);

        var result = await sut.GetProductInfoAsync("12345");

        result.Should().NotBeNull();
        result!.Name.Should().Be("Bio Apfelsaft");
        result.Brand.Should().Be("Acme");
        result.Category.Should().Be("Getr\u00e4nke");
        result.Ean.Should().Be("12345");
        result.Ingredients.Should().Contain("Apfel");
        result.AdditionalInfo.Should().ContainKey("Menge").WhoseValue.Should().Be("1L");
        result.AdditionalInfo.Should().ContainKey("Verpackung");
        handler.RequestedUris.Should().ContainSingle()
            .Which.Should().Contain("openfoodfacts");
    }

    [Fact]
    public async Task GetProductInfoAsync_OpenFoodFactsMiss_FallsBackToUpcItemDb()
    {
        const string offMiss = """{"status":0}""";
        const string upcHit = """
        {
          "items": [
            {
              "title": "Test Product",
              "description": "desc",
              "brand": "BrandX",
              "category": "Tools",
              "images": ["https://example.com/p.jpg"],
              "upc": "00012345",
              "ean": "00012345"
            }
          ]
        }
        """;
        var handler = new FakeHandler(req =>
            req.RequestUri!.Host.Contains("openfoodfacts") ? Json(offMiss) : Json(upcHit));
        var sut = BuildSut(handler);

        var result = await sut.GetProductInfoAsync("00012345");

        result.Should().NotBeNull();
        result!.Name.Should().Be("Test Product");
        result.Brand.Should().Be("BrandX");
        result.Category.Should().Be("Tools");
        result.Upc.Should().Be("00012345");
        handler.RequestedUris.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetProductInfoAsync_BothApisMiss_ReturnsNull()
    {
        var handler = new FakeHandler(req =>
            req.RequestUri!.Host.Contains("openfoodfacts")
                ? Json("""{"status":0}""")
                : Json("""{"items":[]}"""));
        var sut = BuildSut(handler);

        var result = await sut.GetProductInfoAsync("99999");

        result.Should().BeNull();
        handler.RequestedUris.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetProductInfoAsync_HttpErrorOnBoth_ReturnsNull()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var sut = BuildSut(handler);

        var result = await sut.GetProductInfoAsync("11111");

        result.Should().BeNull();
    }

    [Fact]
    public async Task IsServiceAvailableAsync_OkResponse_ReturnsTrue()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = BuildSut(handler);

        (await sut.IsServiceAvailableAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task IsServiceAvailableAsync_ThrowsOrError_ReturnsFalse()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("offline"));
        var sut = BuildSut(handler);

        (await sut.IsServiceAvailableAsync()).Should().BeFalse();
    }

    [Theory]
    [InlineData("dairy products", "Milchprodukte")]
    [InlineData("meat snacks", "Fleisch & Wurst")]
    [InlineData("fresh fruits", "Obst & Gem\u00fcse")]
    [InlineData("vegetables, organic", "Obst & Gem\u00fcse")]
    [InlineData("bread and bakery", "Backwaren")]
    [InlineData("snacks salty", "Snacks")]
    [InlineData("frozen pizza", "Tiefk\u00fchlprodukte")]
    [InlineData("unknown weirdness", "Lebensmittel")]
    public async Task GetProductInfoAsync_DetermineCategory_MapsCorrectly(string offCategory, string expected)
    {
        var body = $$"""
        {
          "status": 1,
          "product": { "product_name": "P", "categories": "{{offCategory}}" }
        }
        """;
        var handler = new FakeHandler(_ => Json(body));
        var sut = BuildSut(handler);

        var result = await sut.GetProductInfoAsync("1");

        result.Should().NotBeNull();
        result!.Category.Should().Be(expected);
    }
}
