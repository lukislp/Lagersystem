using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace LagersystemLVHome.Application.Services;

public sealed class BarcodeApiService : IBarcodeApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BarcodeApiService> _logger;

    private const string OpenFoodFactsApi = "https://world.openfoodfacts.org/api/v0/product/";
    private const string UpcItemDbApi = "https://api.upcitemdb.com/prod/trial/lookup?upc=";

    public BarcodeApiService(HttpClient httpClient, ILogger<BarcodeApiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<ProductInfo?> GetProductInfoAsync(string barcode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            return null;

        // Try multiple API sources for better coverage
        var result = await TryOpenFoodFactsAsync(barcode);
        if (result != null) return result;

        result = await TryUpcItemDbAsync(barcode);
        if (result != null) return result;

        return null;
    }

    public async Task<bool> IsServiceAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("https://world.openfoodfacts.org");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<ProductInfo?> TryOpenFoodFactsAsync(string barcode, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Querying OpenFoodFacts for barcode: {Barcode}", barcode);

            var response = await _httpClient.GetAsync($"{OpenFoodFactsApi}{barcode}.json");
            if (!response.IsSuccessStatusCode)
                return null;

            var data = await response.Content.ReadFromJsonAsync<OpenFoodFactsResponse>();
            if (data?.Status != 1 || data.Product == null)
            {
                _logger.LogInformation("Product not found in OpenFoodFacts");
                return null;
            }

            var product = data.Product;
            var info = new ProductInfo
            {
                Barcode = barcode,
                Name = product.ProductName ?? product.GenericName ?? "Unbekanntes Produkt",
                Description = product.GenericName ?? product.ProductName ?? string.Empty,
                Brand = product.Brands ?? string.Empty,
                Category = DetermineCategory(product),
                ImageUrl = product.ImageUrl ?? product.ImageFrontUrl ?? string.Empty,
                Ean = barcode
            };

            if (!string.IsNullOrEmpty(product.Quantity))
                info.AdditionalInfo["Menge"] = product.Quantity;

            if (!string.IsNullOrEmpty(product.Packaging))
                info.AdditionalInfo["Verpackung"] = product.Packaging;

            if (product.IngredientsText != null)
            {
                info.Ingredients = product.IngredientsText
                    .Split(',')
                    .Select(i => i.Trim())
                    .Where(i => !string.IsNullOrEmpty(i))
                    .ToList();
            }

            _logger.LogInformation("Product found: {Name}", info.Name);
            return info;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error querying OpenFoodFacts");
            return null;
        }
    }

    private async Task<ProductInfo?> TryUpcItemDbAsync(string barcode, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Querying UPCItemDB for barcode: {Barcode}", barcode);

            var response = await _httpClient.GetAsync($"{UpcItemDbApi}{barcode}");
            if (!response.IsSuccessStatusCode)
                return null;

            var data = await response.Content.ReadFromJsonAsync<UpcItemDbResponse>();
            if (data?.Items == null || data.Items.Length == 0)
            {
                _logger.LogInformation("Product not found in UPCItemDB");
                return null;
            }

            var item = data.Items[0];
            var info = new ProductInfo
            {
                Barcode = barcode,
                Name = item.Title ?? "Unbekanntes Produkt",
                Description = item.Description ?? string.Empty,
                Brand = item.Brand ?? string.Empty,
                Category = item.Category ?? "Allgemein",
                ImageUrl = item.Images?.FirstOrDefault() ?? string.Empty,
                Upc = item.Upc,
                Ean = item.Ean
            };

            _logger.LogInformation("Product found: {Name}", info.Name);
            return info;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error querying UPCItemDB");
            return null;
        }
    }

    private string DetermineCategory(OpenFoodFactsProduct product)
    {
        var categories = product.Categories?.ToLower() ?? string.Empty;

        if (categories.Contains("beverages") || categories.Contains("getr\u00e4nke"))
            return "Getr\u00e4nke";
        if (categories.Contains("dairy") || categories.Contains("milch"))
            return "Milchprodukte";
        if (categories.Contains("meat") || categories.Contains("fleisch"))
            return "Fleisch & Wurst";
        if (categories.Contains("fruits") || categories.Contains("obst"))
            return "Obst & Gem\u00fcse";
        if (categories.Contains("vegetables") || categories.Contains("gem\u00fcse"))
            return "Obst & Gem\u00fcse";
        if (categories.Contains("bread") || categories.Contains("brot"))
            return "Backwaren";
        if (categories.Contains("snacks"))
            return "Snacks";
        if (categories.Contains("frozen") || categories.Contains("tiefk\u00fchl"))
            return "Tiefk\u00fchlprodukte";

        return "Lebensmittel";
    }
}

// OpenFoodFacts DTOs
public sealed class OpenFoodFactsResponse
{
    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("product")]
    public OpenFoodFactsProduct? Product { get; set; }
}

public sealed class OpenFoodFactsProduct
{
    [JsonPropertyName("product_name")]
    public string? ProductName { get; set; }

    [JsonPropertyName("generic_name")]
    public string? GenericName { get; set; }

    [JsonPropertyName("brands")]
    public string? Brands { get; set; }

    [JsonPropertyName("categories")]
    public string? Categories { get; set; }

    [JsonPropertyName("quantity")]
    public string? Quantity { get; set; }

    [JsonPropertyName("packaging")]
    public string? Packaging { get; set; }

    [JsonPropertyName("ingredients_text")]
    public string? IngredientsText { get; set; }

    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; set; }

    [JsonPropertyName("image_front_url")]
    public string? ImageFrontUrl { get; set; }
}

// UPCItemDB DTOs
public sealed class UpcItemDbResponse
{
    [JsonPropertyName("items")]
    public UpcItem[]? Items { get; set; }
}

public sealed class UpcItem
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("upc")]
    public string? Upc { get; set; }

    [JsonPropertyName("ean")]
    public string? Ean { get; set; }

    [JsonPropertyName("images")]
    public string[]? Images { get; set; }
}
