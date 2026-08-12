using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace LagersystemLVHome.Application.Services;

public sealed class InventoryService : IInventoryService
{
    private readonly IProductRepository _productRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IStockMovementRepository _stockMovementRepository;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly IPriceHistoryService _priceHistoryService;
    private readonly IAuditService _auditService;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(
        IProductRepository productRepository,
        ICategoryRepository categoryRepository,
        IStockMovementRepository stockMovementRepository,
        IHttpContextAccessor httpContextAccessor,
        IDbContextFactory<InventoryDbContext> contextFactory,
        IPriceHistoryService priceHistoryService,
        IAuditService auditService,
        ILogger<InventoryService> logger)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _stockMovementRepository = stockMovementRepository;
        _httpContextAccessor = httpContextAccessor;
        _contextFactory = contextFactory;
        _priceHistoryService = priceHistoryService;
        _auditService = auditService;
        _logger = logger;
    }

    private int GetWarehouseId()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            return 1;

        var warehouseIdClaim = user.FindFirst("WarehouseId");
        if (warehouseIdClaim != null && int.TryParse(warehouseIdClaim.Value, out var warehouseId))
            return warehouseId;

        return 1;
    }

    public async Task<IEnumerable<Product>> GetAllProductsAsync(CancellationToken cancellationToken = default)
    {
        return await _productRepository.GetAllAsync(GetWarehouseId());
    }

    public async Task<Product?> GetProductByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _productRepository.GetByIdAsync(id, GetWarehouseId());
    }

    public async Task<Product?> GetProductByBarcodeAsync(string barcode, CancellationToken cancellationToken = default)
    {
        return await _productRepository.GetByBarcodeAsync(barcode, GetWarehouseId());
    }

    public async Task<IEnumerable<Product>> GetProductsByCategoryAsync(int categoryId, CancellationToken cancellationToken = default)
    {
        return await _productRepository.GetByCategoryAsync(categoryId, GetWarehouseId());
    }

    public async Task<IEnumerable<Product>> GetLowStockProductsAsync(CancellationToken cancellationToken = default)
    {
        return await _productRepository.GetLowStockAsync(GetWarehouseId());
    }

    public async Task<Product> CreateProductAsync(Product product, CancellationToken cancellationToken = default)
    {
        product.WarehouseId = GetWarehouseId();

        var createdProduct = await _productRepository.CreateAsync(product);

        // Create initial price entry
        try
        {
            var username = GetCurrentUsername();
            await _priceHistoryService.CreateInitialPriceAsync(
                productId: createdProduct.Id,
                warehouseId: createdProduct.WarehouseId,
                price: createdProduct.Price,
                currency: "EUR",
                createdBy: username
            );

            _logger.LogInformation("Initial price {Price} created for product {ProductId} ({ProductName})",
                createdProduct.Price, createdProduct.Id, createdProduct.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create initial price for product {ProductId}", createdProduct.Id);
            // Don't throw - product was already created
        }

        await _auditService.LogProductCreatedAsync(createdProduct.Id, createdProduct.Name);

        return createdProduct;
    }

    public async Task<Product> UpdateProductAsync(Product product, CancellationToken cancellationToken = default)
    {
        var existingProduct = await _productRepository.GetByIdAsync(product.Id, GetWarehouseId());

        if (existingProduct == null)
        {
            throw new InvalidOperationException($"Produkt mit ID {product.Id} nicht gefunden");
        }

        var priceChanged = existingProduct.Price != product.Price;
        var oldPrice = existingProduct.Price;
        var newPrice = product.Price;

        var updatedProduct = await _productRepository.UpdateAsync(product);

        // Automatic price history tracking
        if (priceChanged)
        {
            try
            {
                var username = GetCurrentUsername();
                await _priceHistoryService.UpdatePriceAutomaticAsync(
                    productId: product.Id,
                    warehouseId: product.WarehouseId,
                    oldPrice: oldPrice,
                    newPrice: newPrice,
                    currency: "EUR",
                    updatedBy: username
                );

                _logger.LogInformation("Price updated for product {ProductId} ({ProductName}): {OldPrice} -> {NewPrice}",
                    product.Id, product.Name, oldPrice, newPrice);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update price history for product {ProductId}", product.Id);
                // Don't throw - product was already updated
            }
        }

        await _auditService.LogProductUpdatedAsync(product.Id, product.Name, new { PriceChanged = priceChanged });

        return updatedProduct;
    }

    public async Task DeleteProductAsync(int id, CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetByIdAsync(id, GetWarehouseId());
        await _productRepository.DeleteAsync(id);

        await _auditService.LogProductDeletedAsync(id, product?.Name ?? $"Product#{id}");
    }

    public async Task<IEnumerable<Product>> SearchProductsAsync(string searchTerm, CancellationToken cancellationToken = default)
    {
        return await _productRepository.SearchAsync(searchTerm, GetWarehouseId());
    }

    public async Task<IEnumerable<Category>> GetAllCategoriesAsync(CancellationToken cancellationToken = default)
    {
        return await _categoryRepository.GetAllAsync(GetWarehouseId());
    }

    public async Task<IEnumerable<Category>> GetActiveCategoriesAsync(CancellationToken cancellationToken = default)
    {
        return await _categoryRepository.GetActiveAsync(GetWarehouseId());
    }

    public async Task<Category?> GetCategoryByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _categoryRepository.GetByIdAsync(id, GetWarehouseId());
    }

    public async Task<Category> CreateCategoryAsync(Category category, CancellationToken cancellationToken = default)
    {
        category.WarehouseId = GetWarehouseId();
        var created = await _categoryRepository.CreateAsync(category);
        await _auditService.LogCategoryCreatedAsync(created.Id, created.Name);
        return created;
    }

    public async Task<Category> UpdateCategoryAsync(Category category, CancellationToken cancellationToken = default)
    {
        var updated = await _categoryRepository.UpdateAsync(category);
        await _auditService.LogCategoryUpdatedAsync(updated.Id, updated.Name);
        return updated;
    }

    public async Task DeleteCategoryAsync(int id, CancellationToken cancellationToken = default)
    {
        var category = await _categoryRepository.GetByIdAsync(id, GetWarehouseId());
        await _categoryRepository.DeleteAsync(id);
        await _auditService.LogCategoryDeletedAsync(id, category?.Name ?? $"Category#{id}");
    }

    public async Task<Product?> AddStockByScanAsync(string barcode, int quantity = 1, string? notes = null, CancellationToken cancellationToken = default)
    {
        var product = await GetProductByBarcodeAsync(barcode);
        if (product == null) return null;

        product.Quantity += quantity;
        await _productRepository.UpdateAsync(product);

        await DistributeStockToStorageLocationsAsync(product.Id, quantity);

        await _stockMovementRepository.CreateAsync(new StockMovement
        {
            ProductId = product.Id,
            QuantityChange = quantity,
            Type = MovementType.ScanAdd,
            ScannedBarcode = barcode,
            Notes = notes,
            WarehouseId = GetWarehouseId()
        });

        return product;
    }

    public async Task<Product?> RemoveStockByScanAsync(string barcode, int quantity = 1, string? notes = null, CancellationToken cancellationToken = default)
    {
        var product = await GetProductByBarcodeAsync(barcode);
        if (product == null) return null;

        product.Quantity = Math.Max(0, product.Quantity - quantity);
        await _productRepository.UpdateAsync(product);

        await ReduceStockFromStorageLocationsAsync(product.Id, quantity);
        await ReduceBatchQuantitiesAsync(product.Id, quantity);

        await _stockMovementRepository.CreateAsync(new StockMovement
        {
            ProductId = product.Id,
            QuantityChange = -quantity,
            Type = MovementType.ScanRemove,
            ScannedBarcode = barcode,
            Notes = notes,
            WarehouseId = GetWarehouseId()
        });

        return product;
    }

    public async Task<IEnumerable<StockMovement>> GetRecentMovementsAsync(int count = 10, CancellationToken cancellationToken = default)
    {
        return await _stockMovementRepository.GetRecentAsync(count, GetWarehouseId());
    }

    public async Task<IEnumerable<StockMovement>> GetMovementsByProductAsync(int productId, CancellationToken cancellationToken = default)
    {
        return await _stockMovementRepository.GetByProductAsync(productId, GetWarehouseId());
    }

    public async Task<DashboardStats> GetDashboardStatsAsync(CancellationToken cancellationToken = default)
    {
        var warehouseId = GetWarehouseId();
        var products = await _productRepository.GetAllAsync(warehouseId);
        var categories = await _categoryRepository.GetActiveAsync(warehouseId);
        var lowStock = await _productRepository.GetLowStockAsync(warehouseId);
        var todayMovements = await _stockMovementRepository.GetTodayMovementsAsync(warehouseId);

        var stats = new DashboardStats
        {
            TotalProducts = products.Count(),
            TotalCategories = categories.Count(),
            LowStockCount = lowStock.Count(),
            TotalStockValue = (int)products.Sum(p => p.Price * p.Quantity),
            TodayMovements = todayMovements.Count(),
            // Sort by product count (primary) and total quantity (secondary)
            CategoryStats = categories.Select(c => new CategoryStat
            {
                Name = c.Name,
                Icon = c.Icon,
                ProductCount = products.Count(p => p.CategoryId == c.Id),
                TotalQuantity = products.Where(p => p.CategoryId == c.Id).Sum(p => p.Quantity)
            })
            .OrderByDescending(cs => cs.ProductCount)
            .ThenByDescending(cs => cs.TotalQuantity)
            .ToList()
        };

        return stats;
    }

    /// <summary>
    /// Distributes additional stock across existing storage locations.
    /// </summary>
    private async Task DistributeStockToStorageLocationsAsync(int productId, int quantityToAdd, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var storageLocations = await context.ProductStorageLocations
            .Where(psl => psl.ProductId == productId)
            .OrderBy(psl => psl.Quantity)
            .ToListAsync(cancellationToken);

        if (!storageLocations.Any())
        {
            return;
        }

        var quantityPerLocation = quantityToAdd / storageLocations.Count;
        var remainder = quantityToAdd % storageLocations.Count;

        for (int i = 0; i < storageLocations.Count; i++)
        {
            var location = storageLocations[i];
            location.Quantity += quantityPerLocation;

            if (i < remainder)
            {
                location.Quantity++;
            }

            location.UpdatedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task ReduceStockFromStorageLocationsAsync(int productId, int quantityToRemove, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var storageLocations = await context.ProductStorageLocations
            .Where(psl => psl.ProductId == productId && psl.Quantity > 0)
            .OrderByDescending(psl => psl.Quantity)
            .ToListAsync(cancellationToken);

        if (!storageLocations.Any())
        {
            return;
        }

        var remainingToRemove = quantityToRemove;

        foreach (var location in storageLocations)
        {
            if (remainingToRemove <= 0) break;

            var removeFromThis = Math.Min(location.Quantity, remainingToRemove);
            location.Quantity -= removeFromThis;
            location.UpdatedAt = DateTime.UtcNow;
            remainingToRemove -= removeFromThis;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<Product?> AdjustStockAsync(int productId, int newQuantity, string? notes = null, CancellationToken cancellationToken = default)
    {
        var product = await GetProductByIdAsync(productId);
        if (product == null) return null;

        var quantityChange = newQuantity - product.Quantity;
        product.Quantity = newQuantity;
        await _productRepository.UpdateAsync(product);

        // Sync with ProductStorageLocations
        if (quantityChange > 0)
        {
            await DistributeStockToStorageLocationsAsync(productId, quantityChange);
        }
        else if (quantityChange < 0)
        {
            await ReduceStockFromStorageLocationsAsync(productId, Math.Abs(quantityChange));
            await ReduceBatchQuantitiesAsync(productId, Math.Abs(quantityChange));
        }

        await _stockMovementRepository.CreateAsync(new StockMovement
        {
            ProductId = product.Id,
            QuantityChange = quantityChange,
            Type = MovementType.Adjustment,
            Notes = notes,
            WarehouseId = GetWarehouseId()
        });

        return product;
    }

    /// <summary>
    /// Reduces batch quantities using FIFO (First In, First Out by expiry date).
    /// </summary>
    private async Task ReduceBatchQuantitiesAsync(int productId, int quantityToRemove, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var batches = await context.ProductBatches
            .Where(pb => pb.ProductId == productId && pb.Quantity > 0)
            .OrderBy(pb => pb.ExpiryDate)
            .ThenBy(pb => pb.CreatedAt)
            .ToListAsync(cancellationToken);

        if (!batches.Any()) return;

        var remainingToRemove = quantityToRemove;

        foreach (var batch in batches)
        {
            if (remainingToRemove <= 0) break;

            var removeFromBatch = Math.Min(batch.Quantity, remainingToRemove);
            batch.Quantity -= removeFromBatch;
            batch.UpdatedAt = DateTime.UtcNow;

            remainingToRemove -= removeFromBatch;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private string GetCurrentUsername()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            return user.Identity.Name ?? user.FindFirst(ClaimTypes.Name)?.Value ?? "System";
        }
        return "System";
    }

    public async Task<IReadOnlyList<ProductStorageLocation>> GetProductStorageLocationsAsync(int productId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ProductStorageLocations
            .AsNoTracking()
            .Include(psl => psl.StorageLocation)
            .Where(psl => psl.ProductId == productId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProductStorageLocation>> GetActiveStorageLocationsForProductAsync(int productId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ProductStorageLocations
            .AsNoTracking()
            .Include(psl => psl.StorageLocation)
            .Where(psl => psl.ProductId == productId && psl.Quantity > 0)
            .OrderBy(psl => psl.StorageLocation!.Code)
            .ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<int, int>> GetStorageLocationCountsForProductsAsync(IEnumerable<int> productIds, CancellationToken cancellationToken = default)
    {
        var ids = productIds as IReadOnlyCollection<int> ?? productIds.ToList();
        if (ids.Count == 0) return new Dictionary<int, int>();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ProductStorageLocations
            .AsNoTracking()
            .Where(psl => ids.Contains(psl.ProductId))
            .GroupBy(psl => psl.ProductId)
            .Select(g => new { ProductId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProductId, x => x.Count);
    }

    public async Task ReplaceProductStorageLocationsAsync(
        int productId,
        IEnumerable<ProductStorageLocationAssignment> assignments, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await context.ProductStorageLocations
            .Where(psl => psl.ProductId == productId)
            .ToListAsync(cancellationToken);
        context.ProductStorageLocations.RemoveRange(existing);

        var now = DateTime.UtcNow;
        foreach (var item in assignments.Where(a => a.Quantity > 0))
        {
            context.ProductStorageLocations.Add(new ProductStorageLocation
            {
                ProductId = productId,
                StorageLocationId = item.StorageLocationId,
                Quantity = item.Quantity,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProductBatch>> GetProductBatchesAsync(int productId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ProductBatches
            .AsNoTracking()
            .Where(pb => pb.ProductId == productId)
            .OrderBy(pb => pb.ExpiryDate ?? DateTime.MaxValue)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProductBatch>> GetActiveBatchesForProductAsync(int productId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ProductBatches
            .AsNoTracking()
            .Where(pb => pb.ProductId == productId && pb.Quantity > 0)
            .OrderByDescending(pb => pb.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task ReplaceProductBatchesAsync(
        int productId,
        int warehouseId,
        IEnumerable<ProductBatch> batches, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var existingBatches = await context.ProductBatches
            .Where(pb => pb.ProductId == productId)
            .ToListAsync(cancellationToken);
        context.ProductBatches.RemoveRange(existingBatches);

        var now = DateTime.UtcNow;
        foreach (var batch in batches.Where(b => b.Quantity > 0))
        {
            context.ProductBatches.Add(new ProductBatch
            {
                ProductId = productId,
                BatchNumber = batch.BatchNumber,
                Quantity = batch.Quantity,
                ExpiryDate = batch.ExpiryDate,
                ManufactureDate = batch.ManufactureDate,
                Notes = batch.Notes,
                WarehouseId = warehouseId,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<Result> ProcessScannerMovementAsync(ScannerMovementCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var product = await context.Products.FindAsync(command.ProductId);
            if (product is null)
            {
                return Result.Failure("scanner.productnotfound", $"Product {command.ProductId} not found");
            }

            var now = DateTime.UtcNow;

            // Update / create storage location assignments
            foreach (var item in command.Distribution.Where(d => d.Quantity > 0))
            {
                var psl = await context.ProductStorageLocations
                    .FirstOrDefaultAsync(p =>
                        p.ProductId == command.ProductId &&
                        p.StorageLocationId == item.StorageLocationId, cancellationToken);

                if (psl != null)
                {
                    psl.Quantity = command.IsAdd
                        ? psl.Quantity + item.Quantity
                        : Math.Max(0, psl.Quantity - item.Quantity);
                    psl.UpdatedAt = now;
                }
                else if (command.IsAdd)
                {
                    context.ProductStorageLocations.Add(new ProductStorageLocation
                    {
                        ProductId = command.ProductId,
                        StorageLocationId = item.StorageLocationId,
                        Quantity = item.Quantity,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }
            }

            // Update product total
            product.Quantity = command.IsAdd
                ? product.Quantity + command.Quantity
                : Math.Max(0, product.Quantity - command.Quantity);
            product.UpdatedAt = now;

            // Inbound batch handling
            if (command.IsAdd && command.ProcessBatch)
            {
                if (command.SelectedBatchId.HasValue)
                {
                    var existingBatch = await context.ProductBatches.FindAsync(command.SelectedBatchId.Value);
                    if (existingBatch != null)
                    {
                        existingBatch.Quantity += command.Quantity;
                        existingBatch.UpdatedAt = now;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(command.BatchNumber))
                {
                    await context.ProductBatches.AddAsync(new ProductBatch
                    {
                        ProductId = command.ProductId,
                        BatchNumber = command.BatchNumber!,
                        Quantity = command.Quantity,
                        ExpiryDate = command.BatchExpiryDate,
                        ManufactureDate = command.BatchManufactureDate,
                        Notes = string.IsNullOrWhiteSpace(command.BatchNotes)
                            ? $"Scanner-Eingang am {now:dd.MM.yyyy HH:mm}"
                            : command.BatchNotes,
                        WarehouseId = command.WarehouseId,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }
            }

            // Outbound batch handling (FIFO)
            if (!command.IsAdd && command.ProcessBatch)
            {
                var remainingToRemove = command.Quantity;
                var emptyBatches = new List<ProductBatch>();

                if (command.SelectedBatchId.HasValue)
                {
                    var selectedBatch = await context.ProductBatches.FindAsync(command.SelectedBatchId.Value);
                    if (selectedBatch != null && selectedBatch.Quantity > 0)
                    {
                        var removeFromBatch = Math.Min(selectedBatch.Quantity, remainingToRemove);
                        selectedBatch.Quantity -= removeFromBatch;
                        selectedBatch.UpdatedAt = now;
                        if (selectedBatch.Quantity == 0)
                        {
                            emptyBatches.Add(selectedBatch);
                        }
                        remainingToRemove -= removeFromBatch;
                    }
                }

                if (remainingToRemove > 0)
                {
                    var batches = await context.ProductBatches
                        .Where(pb => pb.ProductId == command.ProductId
                                     && pb.Quantity > 0
                                     && pb.Id != command.SelectedBatchId)
                        .OrderBy(pb => pb.ExpiryDate)
                        .ToListAsync(cancellationToken);

                    foreach (var batch in batches)
                    {
                        if (remainingToRemove <= 0) break;
                        var removeFromBatch = Math.Min(batch.Quantity, remainingToRemove);
                        batch.Quantity -= removeFromBatch;
                        batch.UpdatedAt = now;
                        if (batch.Quantity == 0)
                        {
                            emptyBatches.Add(batch);
                        }
                        remainingToRemove -= removeFromBatch;
                    }
                }

                if (emptyBatches.Count > 0)
                {
                    context.ProductBatches.RemoveRange(emptyBatches);
                }
            }

            // Stock movement log
            await context.StockMovements.AddAsync(new StockMovement
            {
                ProductId = command.ProductId,
                QuantityChange = command.IsAdd ? command.Quantity : -command.Quantity,
                Type = command.IsAdd ? MovementType.ScanAdd : MovementType.ScanRemove,
                ScannedBarcode = command.Barcode,
                Notes = command.MovementNotes,
                WarehouseId = command.WarehouseId,
                Timestamp = now
            });

            await context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing scanner movement for product {ProductId}", command.ProductId);
            return Result.Failure("scanner.movementfailed", ex.Message);
        }
    }
}
