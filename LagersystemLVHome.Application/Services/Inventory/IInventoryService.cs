using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.Data.Repositories;

namespace LagersystemLVHome.Application.Services;

public interface IInventoryService
{
    // Product operations
    Task<IEnumerable<Product>> GetAllProductsAsync(CancellationToken cancellationToken = default);
    Task<Product?> GetProductByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Product?> GetProductByBarcodeAsync(string barcode, CancellationToken cancellationToken = default);
    Task<IEnumerable<Product>> GetProductsByCategoryAsync(int categoryId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Product>> SearchProductsAsync(string searchTerm, CancellationToken cancellationToken = default);
    Task<IEnumerable<Product>> GetLowStockProductsAsync(CancellationToken cancellationToken = default);
    Task<Product> CreateProductAsync(Product product, CancellationToken cancellationToken = default);
    Task<Product> UpdateProductAsync(Product product, CancellationToken cancellationToken = default);
    Task DeleteProductAsync(int id, CancellationToken cancellationToken = default);

    // Stock operations
    Task<Product?> AddStockByScanAsync(string barcode, int quantity = 1, string? notes = null, CancellationToken cancellationToken = default);
    Task<Product?> RemoveStockByScanAsync(string barcode, int quantity = 1, string? notes = null, CancellationToken cancellationToken = default);
    Task<Product?> AdjustStockAsync(int productId, int newQuantity, string? notes = null, CancellationToken cancellationToken = default);

    // Category operations
    Task<IEnumerable<Category>> GetAllCategoriesAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Category>> GetActiveCategoriesAsync(CancellationToken cancellationToken = default);
    Task<Category?> GetCategoryByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Category> CreateCategoryAsync(Category category, CancellationToken cancellationToken = default);
    Task<Category> UpdateCategoryAsync(Category category, CancellationToken cancellationToken = default);
    Task DeleteCategoryAsync(int id, CancellationToken cancellationToken = default);

    // Movement operations
    Task<IEnumerable<StockMovement>> GetRecentMovementsAsync(int count = 50, CancellationToken cancellationToken = default);
    Task<IEnumerable<StockMovement>> GetMovementsByProductAsync(int productId, CancellationToken cancellationToken = default);

    // Dashboard Stats
    Task<DashboardStats> GetDashboardStatsAsync(CancellationToken cancellationToken = default);

    // Product Storage Locations (per-location quantities)
    Task<IReadOnlyList<ProductStorageLocation>> GetProductStorageLocationsAsync(int productId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductStorageLocation>> GetActiveStorageLocationsForProductAsync(int productId, CancellationToken cancellationToken = default);
    Task ReplaceProductStorageLocationsAsync(int productId, IEnumerable<ProductStorageLocationAssignment> assignments, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts how many distinct storage locations each product is currently placed in.
    /// Used by listing pages to render a "# storage bins" badge without N+1 queries.
    /// </summary>
    Task<Dictionary<int, int>> GetStorageLocationCountsForProductsAsync(IEnumerable<int> productIds, CancellationToken cancellationToken = default);

    // Product Batches
    Task<IReadOnlyList<ProductBatch>> GetProductBatchesAsync(int productId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductBatch>> GetActiveBatchesForProductAsync(int productId, CancellationToken cancellationToken = default);
    Task ReplaceProductBatchesAsync(int productId, int warehouseId, IEnumerable<ProductBatch> batches, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a scan-based stock movement: distributes the quantity across the given
    /// storage locations, updates the product total, manages batches (FIFO on outbound,
    /// batch create/update on inbound) and writes a single StockMovement.
    /// </summary>
    Task<Result> ProcessScannerMovementAsync(ScannerMovementCommand command, CancellationToken cancellationToken = default);
}

/// <summary>Assignment of a product to a storage location with a per-location quantity.</summary>
public sealed record ProductStorageLocationAssignment(int StorageLocationId, int Quantity);

/// <summary>Input for <see cref="IInventoryService.ProcessScannerMovementAsync"/>.</summary>
public sealed record ScannerMovementCommand(
    int ProductId,
    bool IsAdd,
    int Quantity,
    string Barcode,
    int WarehouseId,
    IReadOnlyList<ProductStorageLocationAssignment> Distribution,
    bool ProcessBatch,
    int? SelectedBatchId,
    string? BatchNumber,
    DateTime? BatchExpiryDate,
    DateTime? BatchManufactureDate,
    string? BatchNotes,
    string MovementNotes);

public sealed class DashboardStats
{
    public int TotalProducts { get; set; }
    public int TotalCategories { get; set; }
    public int LowStockCount { get; set; }
    public int TotalStockValue { get; set; }
    public int TodayMovements { get; set; }
    public List<CategoryStat> CategoryStats { get; set; } = new();
}

public sealed class CategoryStat
{
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public int ProductCount { get; set; }
    public int TotalQuantity { get; set; }
}
