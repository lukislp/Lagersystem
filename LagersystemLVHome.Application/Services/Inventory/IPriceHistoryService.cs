using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Price history management service.
/// </summary>
public interface IPriceHistoryService
{
    Task<ProductPrice?> GetCurrentPriceAsync(int productId, CancellationToken cancellationToken = default);

    Task<ProductPrice?> GetPriceAtDateAsync(int productId, DateTime date, CancellationToken cancellationToken = default);

    Task<List<ProductPrice>> GetPriceHistoryAsync(int productId, CancellationToken cancellationToken = default);

    Task CreateInitialPriceAsync(int productId, int warehouseId, decimal price, string currency, string? createdBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Automatically updates the price: closes the old price and creates a new one.
    /// </summary>
    Task UpdatePriceAutomaticAsync(int productId, int warehouseId, decimal oldPrice, decimal newPrice, string currency, string? updatedBy, CancellationToken cancellationToken = default);

    Task<bool> HasPriceHistoryAsync(int productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts price changes for a product.
    /// </summary>
    Task<int> GetPriceChangeCountAsync(int productId, CancellationToken cancellationToken = default);

    Task<PriceStatistics> GetPriceStatisticsAsync(int productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Monthly price change statistics.
    /// </summary>
    Task<MonthlyPriceStatistics> GetMonthlyStatisticsAsync(int productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Yearly price change statistics.
    /// </summary>
    Task<YearlyPriceStatistics> GetYearlyStatisticsAsync(int productId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Price history statistics.
/// </summary>
public sealed class PriceStatistics
{
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public decimal? AveragePrice { get; set; }
    public int TotalChanges { get; set; }
    public DateTime? FirstPriceDate { get; set; }
    public DateTime? LastPriceDate { get; set; }
    public decimal? CurrentPrice { get; set; }
    public decimal? PriceChange { get; set; }
    public double? PriceChangePercent { get; set; }
}

/// <summary>
/// Monthly price statistics model.
/// </summary>
public sealed class MonthlyPriceStatistics
{
    public int Month { get; set; }
    public int Year { get; set; }
    public decimal StartPrice { get; set; }
    public decimal EndPrice { get; set; }
    public decimal PriceChange { get; set; }
    public double PriceChangePercent { get; set; }
    public int ChangesCount { get; set; }
    public bool HasData { get; set; }

    public string MonthName => new DateTime(Year, Month, 1).ToString("MMMM yyyy");
}

/// <summary>
/// Yearly price statistics model.
/// </summary>
public sealed class YearlyPriceStatistics
{
    public int Year { get; set; }
    public decimal StartPrice { get; set; }
    public decimal EndPrice { get; set; }
    public decimal PriceChange { get; set; }
    public double PriceChangePercent { get; set; }
    public decimal MaxPrice { get; set; }
    public decimal MinPrice { get; set; }
    public int ChangesCount { get; set; }
    public bool HasData { get; set; }
}
