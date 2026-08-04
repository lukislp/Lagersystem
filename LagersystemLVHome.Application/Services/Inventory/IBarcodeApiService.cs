namespace LagersystemLVHome.Application.Services;

public interface IBarcodeApiService
{
    Task<ProductInfo?> GetProductInfoAsync(string barcode, CancellationToken cancellationToken = default);
    Task<bool> IsServiceAvailableAsync(CancellationToken cancellationToken = default);
}

public sealed class ProductInfo
{
    public string Barcode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public decimal? Price { get; set; }
    public string? Ean { get; set; }
    public string? Upc { get; set; }
    public List<string> Ingredients { get; set; } = new();
    public Dictionary<string, string> AdditionalInfo { get; set; } = new();
}
