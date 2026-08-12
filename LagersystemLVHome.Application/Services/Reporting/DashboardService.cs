using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.Application.Configuration;

namespace LagersystemLVHome.Application.Services;

public sealed class DashboardService : IDashboardService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly ICacheService _cache;
    private readonly DashboardSettings _settings;
    private readonly ILogger<DashboardService> _logger;

    public DashboardService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        ICacheService cache,
        DashboardSettings settings,
        ILogger<DashboardService> logger)
    {
        _contextFactory = contextFactory;
        _cache = cache;
        _settings = settings;
        _logger = logger;
    }

    public async Task<DashboardData> GetDashboardDataAsync(int? warehouseId = null, DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var cacheKey = $"Dashboard_Data_{warehouseId}_{DateTime.UtcNow:yyyyMMddHHmm}";

            return await _cache.GetOrCreateAsync(cacheKey,
                TimeSpan.FromMinutes(1),
                async () =>
                {
                    await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                    var data = new DashboardData();

                    var productsQuery = context.Products
                        .Include(p => p.Category)
                        .Include(p => p.ProductStorageLocations)
                            .ThenInclude(psl => psl.StorageLocation)
                            .ThenInclude(sl => sl.Warehouse)
                        .AsNoTracking();

                    if (warehouseId.HasValue)
                    {
                        productsQuery = productsQuery.Where(p =>
                            p.ProductStorageLocations.Any(psl =>
                                psl.StorageLocation.WarehouseId == warehouseId.Value));
                    }

                    // KPIs
                    data.TotalProducts = await productsQuery.CountAsync(cancellationToken);

                    // FIFO stock value
                    data.TotalStockValue = warehouseId.HasValue
                        ? await CalculateFIFOStockValueAsync(warehouseId.Value)
                        : await CalculateFIFOStockValueForAllWarehousesAsync();

                    data.LowStockCount = await productsQuery.CountAsync(p => p.Quantity <= p.MinQuantity, cancellationToken);
                    data.TotalCategories = await context.Categories.CountAsync(cancellationToken);
                    data.TotalWarehouses = await context.Warehouses.CountAsync(cancellationToken);
                    data.TotalStorageLocations = await context.StorageLocations.CountAsync(cancellationToken);

                    var products = await productsQuery.ToListAsync(cancellationToken);
                    data.TotalStockQuantity = products.Sum(p => p.Quantity);

                    if (data.TotalProducts > 0)
                    {
                        data.AverageProductValue = data.TotalStockValue / data.TotalProducts;
                    }

                    data.InventoryHealthScore = await CalculateInventoryHealthScoreAsync();
                    data.StockTurnoverRate = await CalculateStockTurnoverRateAsync();
                    data.AbcAnalysis = await GetABCAnalysisAsync();
                    data.ExpiryAnalytics = await GetExpiryAnalyticsAsync();
                    data.StorageUtilization = await GetStorageUtilizationAsync();
                    data.StockTrends = await GetStockTrendsAsync(_settings.DefaultPeriodDays);
                    data.TopMovers = await GetTopMoversAsync(_settings.MaxTopMovers);
                    data.CategoryValues = await GetCategoryValuesAsync();
                    data.WarehouseDistribution = await GetWarehouseDistributionAsync();

                    // Recent movements
                    var movementsQuery = context.StockMovements
                        .Include(m => m.Product)
                            .ThenInclude(p => p.Category)
                        .OrderByDescending(m => m.Timestamp)
                        .Take(20)
                        .AsNoTracking();

                    if (warehouseId.HasValue)
                    {
                        movementsQuery = (IOrderedQueryable<StockMovement>)movementsQuery.Where(m =>
                            m.Product.ProductStorageLocations.Any(psl =>
                                psl.StorageLocation.WarehouseId == warehouseId.Value));
                    }

                    data.RecentMovements = await movementsQuery.ToListAsync(cancellationToken);

                    return data;
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting dashboard data");
            return new DashboardData();
        }
    }

    private async Task<double> CalculateInventoryHealthScoreAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var products = await context.Products.ToListAsync(cancellationToken);
            if (!products.Any()) return 0;

            double score = 100;

            var lowStockRatio = products.Count(p => p.Quantity <= p.MinQuantity) / (double)products.Count;
            score -= lowStockRatio * 30;

            var outOfStockRatio = products.Count(p => p.Quantity == 0) / (double)products.Count;
            score -= outOfStockRatio * 40;

            var expiredProducts = await context.ProductBatches.CountAsync(pb => pb.ExpiryDate < DateTime.UtcNow, cancellationToken);
            var expiredRatio = expiredProducts / (double)products.Count;
            score -= expiredRatio * 20;

            var balancedStock = products.Count(p => p.Quantity > p.MinQuantity * 2) / (double)products.Count;
            score += balancedStock * 10;

            return Math.Max(0, Math.Min(100, score));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating inventory health score");
            return 0;
        }
    }

    private async Task<double> CalculateStockTurnoverRateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

            // Outbound units in last 30 days
            var soldUnits = await context.StockMovements
                .Where(m => m.Timestamp >= thirtyDaysAgo && m.QuantityChange < 0)
                .SumAsync(m => (int?)Math.Abs(m.QuantityChange), cancellationToken) ?? 0;

            // Average inventory
            var avgInventory = await context.Products
                .AverageAsync(p => (int?)p.Quantity, cancellationToken) ?? 0;

            if (avgInventory == 0) return 0;

            return soldUnits / avgInventory;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating stock turnover rate");
            return 0;
        }
    }

    private async Task<ABCAnalysisData> GetABCAnalysisAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var products = await context.Products
                .Include(p => p.Category)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (!products.Any())
            {
                return new ABCAnalysisData();
            }

            // Calculate FIFO value for each product
            var productsWithValue = new List<(Product Product, decimal Value)>();

            foreach (var product in products)
            {
                var fifoValue = await CalculateProductFIFOValueAsync(context, product.Id, product.Quantity);
                productsWithValue.Add((product, fifoValue));
            }

            var sortedProducts = productsWithValue
                .OrderByDescending(x => x.Value)
                .ToList();

            var totalValue = sortedProducts.Sum(x => x.Value);

            if (totalValue == 0)
            {
                _logger.LogWarning("Total value is zero in ABC analysis");
                return new ABCAnalysisData
                {
                    ClassACount = 0,
                    ClassAValue = 0,
                    ClassBCount = 0,
                    ClassBValue = 0,
                    ClassCCount = products.Count,
                    ClassCValue = 0,
                    TotalValue = 0
                };
            }

            var cumulativeValue = 0m;
            var classA = new List<Product>();
            var classB = new List<Product>();
            var classC = new List<Product>();

            foreach (var item in sortedProducts)
            {
                cumulativeValue += item.Value;
                var percentage = (cumulativeValue / totalValue) * 100;

                if (percentage <= 80)
                {
                    classA.Add(item.Product);
                }
                else if (percentage <= 95)
                {
                    classB.Add(item.Product);
                }
                else
                {
                    classC.Add(item.Product);
                }
            }

            // Calculate class values using FIFO
            decimal classAValue = 0, classBValue = 0, classCValue = 0;

            foreach (var product in classA)
            {
                classAValue += await CalculateProductFIFOValueAsync(context, product.Id, product.Quantity);
            }
            foreach (var product in classB)
            {
                classBValue += await CalculateProductFIFOValueAsync(context, product.Id, product.Quantity);
            }
            foreach (var product in classC)
            {
                classCValue += await CalculateProductFIFOValueAsync(context, product.Id, product.Quantity);
            }

            return new ABCAnalysisData
            {
                ClassACount = classA.Count,
                ClassAValue = classAValue,
                ClassBCount = classB.Count,
                ClassBValue = classBValue,
                ClassCCount = classC.Count,
                ClassCValue = classCValue,
                TotalValue = totalValue
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting ABC analysis");
            return new ABCAnalysisData();
        }
    }

    private async Task<ExpiryAnalyticsData> GetExpiryAnalyticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var today = DateTime.UtcNow.Date;
            var batches = await context.ProductBatches
                .Include(pb => pb.Product)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var expired = batches.Where(pb => pb.ExpiryDate < today).ToList();
            var expiringSoon = batches.Where(pb =>
                pb.ExpiryDate >= today &&
                pb.ExpiryDate <= today.AddDays(7)).ToList();
            var expiringThisMonth = batches.Where(pb =>
                pb.ExpiryDate > today.AddDays(7) &&
                pb.ExpiryDate <= today.AddDays(30)).ToList();

            decimal expiredValue = 0, expiringSoonValue = 0, expiringThisMonthValue = 0;

            foreach (var batch in expired)
            {
                var batchValue = await CalculateProductFIFOValueAsync(context, batch.ProductId, batch.Quantity);
                expiredValue += batchValue;
            }

            foreach (var batch in expiringSoon)
            {
                var batchValue = await CalculateProductFIFOValueAsync(context, batch.ProductId, batch.Quantity);
                expiringSoonValue += batchValue;
            }

            foreach (var batch in expiringThisMonth)
            {
                var batchValue = await CalculateProductFIFOValueAsync(context, batch.ProductId, batch.Quantity);
                expiringThisMonthValue += batchValue;
            }

            return new ExpiryAnalyticsData
            {
                ExpiredCount = expired.Count,
                ExpiredValue = expiredValue,
                ExpiringSoonCount = expiringSoon.Count,
                ExpiringSoonValue = expiringSoonValue,
                ExpiringThisMonthCount = expiringThisMonth.Count,
                ExpiringThisMonthValue = expiringThisMonthValue
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting expiry analytics");
            return new ExpiryAnalyticsData();
        }
    }

    private async Task<StorageUtilizationData> GetStorageUtilizationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var locations = await context.StorageLocations
                .Include(sl => sl.ProductStorageLocations)
                    .ThenInclude(psl => psl.Product)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var withCapacity = locations.Where(sl => sl.MaxCapacity.HasValue).ToList();
            if (!withCapacity.Any())
            {
                return new StorageUtilizationData
                {
                    TotalLocations = locations.Count,
                    OccupiedLocations = locations.Count(sl => sl.ProductStorageLocations.Any())
                };
            }

            var utilizationData = withCapacity.Select(sl => new
            {
                Location = sl,
                CurrentCapacity = sl.ProductStorageLocations.Sum(psl => psl.Quantity),
                Utilization = sl.MaxCapacity.Value > 0
                    ? (sl.ProductStorageLocations.Sum(psl => psl.Quantity) / (double)sl.MaxCapacity.Value) * 100
                    : 0
            }).ToList();

            return new StorageUtilizationData
            {
                TotalLocations = locations.Count,
                OccupiedLocations = locations.Count(sl => sl.ProductStorageLocations.Any()),
                EmptyLocations = locations.Count(sl => !sl.ProductStorageLocations.Any()),
                FullLocations = utilizationData.Count(u => u.Utilization >= 90),
                AverageUtilization = utilizationData.Average(u => u.Utilization),
                LocationsWithCapacity = withCapacity.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting storage utilization");
            return new StorageUtilizationData();
        }
    }

    public async Task<List<StockTrendData>> GetStockTrendsAsync(int days = 30, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var startDate = DateTime.UtcNow.Date.AddDays(-days);

            var movements = await context.StockMovements
                .Include(m => m.Product)
                .Where(m => m.Timestamp >= startDate)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            _logger.LogInformation("Found {Count} movements for stock trends", movements.Count);

            var trends = new List<StockTrendData>();

            for (int i = 0; i < days; i++)
            {
                var date = startDate.AddDays(i);
                var dayMovements = movements.Where(m => m.Timestamp.Date == date.Date).ToList();

                var stockIn = dayMovements.Where(m => m.QuantityChange > 0).Sum(m => m.QuantityChange);
                var stockOut = Math.Abs(dayMovements.Where(m => m.QuantityChange < 0).Sum(m => m.QuantityChange));

                trends.Add(new StockTrendData
                {
                    Date = date,
                    StockIn = stockIn,
                    StockOut = stockOut,
                    TotalStock = stockIn - stockOut,
                    Value = dayMovements.Sum(m => Math.Abs(m.QuantityChange) * m.Product.Price)
                });
            }

            _logger.LogInformation("Generated {Count} trend data points", trends.Count);
            return trends;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting stock trends");
            return new List<StockTrendData>();
        }
    }

    public async Task<List<TopMoverData>> GetTopMoversAsync(int count = 10, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var thirtyDaysAgo = DateTime.UtcNow.Date.AddDays(-30);

            var movements = await context.StockMovements
                .Include(m => m.Product)
                    .ThenInclude(p => p.Category)
                .Where(m => m.Timestamp >= thirtyDaysAgo)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            _logger.LogInformation("Found {Count} movements for top movers", movements.Count);

            var topMovers = movements
                .GroupBy(m => new { m.ProductId, ProductName = m.Product.Name, CategoryName = m.Product.Category?.Name })
                .Select(g => new TopMoverData
                {
                    ProductId = g.Key.ProductId,
                    ProductName = g.Key.ProductName,
                    CategoryName = g.Key.CategoryName ?? "Ohne Kategorie",
                    MovementCount = g.Count(),
                    TotalValue = g.Sum(m => Math.Abs(m.QuantityChange) * m.Product.Price)
                })
                .OrderByDescending(t => t.MovementCount)
                .Take(count)
                .ToList();

            _logger.LogInformation("Generated {Count} top movers", topMovers.Count);
            return topMovers;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting top movers");
            return new List<TopMoverData>();
        }
    }

    public async Task<List<CategoryValueData>> GetCategoryValuesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var products = await context.Products
                .Include(p => p.Category)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            _logger.LogInformation("Found {Count} products for category values", products.Count);

            // Calculate FIFO value per category
            var categoryValuesList = new List<CategoryValueData>();

            var productsByCategory = products
                .GroupBy(p => new { p.CategoryId, p.Category?.Name })
                .ToList();

            foreach (var categoryGroup in productsByCategory)
            {
                decimal categoryFifoValue = 0;

                foreach (var product in categoryGroup)
                {
                    var productValue = await CalculateProductFIFOValueAsync(context, product.Id, product.Quantity);
                    categoryFifoValue += productValue;
                }

                categoryValuesList.Add(new CategoryValueData
                {
                    CategoryId = categoryGroup.Key.CategoryId,
                    CategoryName = categoryGroup.Key.Name ?? "Ohne Kategorie",
                    TotalValue = categoryFifoValue,
                    ProductCount = categoryGroup.Count(),
                    TotalQuantity = categoryGroup.Sum(p => p.Quantity)
                });
            }

            var sortedCategories = categoryValuesList
                .OrderByDescending(c => c.TotalValue)
                .ToList();

            _logger.LogInformation("Generated {Count} category value entries with FIFO calculation", sortedCategories.Count);
            return sortedCategories;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting category values");
            return new List<CategoryValueData>();
        }
    }

    public async Task<List<WarehouseStockData>> GetWarehouseDistributionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var warehouses = await context.Warehouses
                .Include(w => w.StorageLocations)
                    .ThenInclude(sl => sl.ProductStorageLocations)
                    .ThenInclude(psl => psl.Product)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var warehouseData = new List<WarehouseStockData>();

            foreach (var warehouse in warehouses)
            {
                var fifoValue = await CalculateFIFOStockValueAsync(warehouse.Id);

                warehouseData.Add(new WarehouseStockData
                {
                    WarehouseId = warehouse.Id,
                    WarehouseName = warehouse.Name,
                    StorageLocationCount = warehouse.StorageLocations.Count,
                    ProductCount = warehouse.StorageLocations
                        .SelectMany(sl => sl.ProductStorageLocations)
                        .Select(psl => psl.ProductId)
                        .Distinct()
                        .Count(),
                    TotalValue = fifoValue
                });
            }

            return warehouseData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting warehouse distribution");
            return new List<WarehouseStockData>();
        }
    }

    public async Task<DashboardData> GetDashboardDataAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null)
            {
                _logger.LogWarning("No authenticated user found for dashboard");
                return new DashboardData();
            }

            var cacheKey = $"Dashboard_Data_{currentUser.WarehouseId}_{DateTime.UtcNow:yyyyMMddHHmm}";

            return await _cache.GetOrCreateAsync<DashboardData>(cacheKey, TimeSpan.FromMinutes(1), async () =>
            {
                await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                var productsQuery = context.Products
                    .Include(p => p.Category)
                    .Include(p => p.ProductStorageLocations)
                        .ThenInclude(psl => psl.StorageLocation)
                        .ThenInclude(sl => sl.Warehouse)
                    .AsNoTracking();

                if (currentUser.WarehouseId > 0)
                {
                    productsQuery = productsQuery.Where(p =>
                        p.ProductStorageLocations.Any(psl =>
                            psl.StorageLocation.WarehouseId == currentUser.WarehouseId));
                }

                var totalProducts = await productsQuery.CountAsync(cancellationToken);
                var totalStockValue = await CalculateFIFOStockValueAsync(currentUser.WarehouseId);

                return new DashboardData
                {
                    TotalProducts = totalProducts,
                    TotalStockValue = totalStockValue,
                    LowStockCount = await productsQuery.CountAsync(p => p.Quantity <= p.MinQuantity, cancellationToken),
                    TotalCategories = await context.Categories.CountAsync(cancellationToken),
                    TotalWarehouses = await context.Warehouses.CountAsync(cancellationToken),
                    TotalStorageLocations = await context.StorageLocations.CountAsync(cancellationToken),
                    TotalStockQuantity = await productsQuery.SumAsync(p => p.Quantity, cancellationToken),
                    AverageProductValue = totalProducts > 0 ? totalStockValue / totalProducts : 0,
                    InventoryHealthScore = await CalculateInventoryHealthScoreAsync(),
                    StockTurnoverRate = await CalculateStockTurnoverRateAsync(),
                    AbcAnalysis = await GetABCAnalysisAsync(),
                    ExpiryAnalytics = await GetExpiryAnalyticsAsync(),
                    StorageUtilization = await GetStorageUtilizationAsync(),
                    StockTrends = await GetStockTrendsAsync(_settings.DefaultPeriodDays),
                    TopMovers = await GetTopMoversAsync(_settings.MaxTopMovers),
                    CategoryValues = await GetCategoryValuesAsync(),
                    WarehouseDistribution = await GetWarehouseDistributionAsync(),
                    RecentMovements = await context.StockMovements
                        .Include(m => m.Product)
                            .ThenInclude(p => p.Category)
                        .OrderByDescending(m => m.Timestamp)
                        .Take(20)
                        .AsNoTracking()
                        .ToListAsync(cancellationToken)
                };
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting dashboard data");
            return new DashboardData();
        }
    }

    private async Task<User?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await context.Users.FirstOrDefaultAsync(cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Calculates FIFO value across all warehouses.
    /// </summary>
    private async Task<decimal> CalculateFIFOStockValueForAllWarehousesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var warehouses = await context.Warehouses.Select(w => w.Id).ToListAsync(cancellationToken);
            decimal totalValue = 0;

            foreach (var warehouseId in warehouses)
            {
                totalValue += await CalculateFIFOStockValueAsync(warehouseId);
            }

            return totalValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating FIFO stock value for all warehouses");
            return 0;
        }
    }

    /// <summary>
    /// Calculates the stock value based on FIFO (First In, First Out)
    /// using historical purchase prices from PriceHistory.
    /// </summary>
    private async Task<decimal> CalculateFIFOStockValueAsync(int warehouseId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var products = await context.Products
                .Where(p => p.WarehouseId == warehouseId && p.Quantity > 0)
                .ToListAsync(cancellationToken);

            decimal totalValue = 0;

            foreach (var product in products)
            {
                var productValue = await CalculateProductFIFOValueAsync(context, product.Id, product.Quantity);
                totalValue += productValue;
            }

            return totalValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating FIFO stock value");
            return 0;
        }
    }

    /// <summary>
    /// Calculates the FIFO value for a single product.
    /// </summary>
    private async Task<decimal> CalculateProductFIFOValueAsync(InventoryDbContext context, int productId, int totalQuantity, CancellationToken cancellationToken = default)
    {
        // Load all inbound movements in FIFO order (oldest first)
        var stockIns = await context.StockMovements
            .Where(sm => sm.ProductId == productId &&
                sm.QuantityChange > 0 &&
                sm.Type == MovementType.ScanAdd)
            .OrderBy(sm => sm.Timestamp)
            .Select(sm => new
            {
                sm.Timestamp,
                Quantity = sm.QuantityChange
            })
            .ToListAsync(cancellationToken);

        decimal fifoValue = 0;
        int remainingQuantity = totalQuantity;

        foreach (var stockIn in stockIns)
        {
            if (remainingQuantity <= 0) break;

            var priceAtPurchase = await GetPriceAtTimestampAsync(context, productId, stockIn.Timestamp);

            var quantityFromThisBatch = Math.Min(stockIn.Quantity, remainingQuantity);
            fifoValue += quantityFromThisBatch * priceAtPurchase;

            remainingQuantity -= quantityFromThisBatch;
        }

        // Remaining quantity without matching stock movements (initial product creation)
        if (remainingQuantity > 0)
        {
            var initialPrice = await GetInitialPriceAsync(context, productId);
            fifoValue += remainingQuantity * initialPrice;
        }

        return fifoValue;
    }

    /// <summary>
    /// Retrieves the initial price of a product (at creation time).
    /// Uses the oldest PriceHistory entry or the current price as fallback.
    /// </summary>
    private async Task<decimal> GetInitialPriceAsync(InventoryDbContext context, int productId, CancellationToken cancellationToken = default)
    {
        var initialPrice = await context.ProductPrices
            .Where(pp => pp.ProductId == productId)
            .OrderBy(pp => pp.ValidFrom)
            .Select(pp => pp.Price)
            .FirstOrDefaultAsync(cancellationToken);

        if (initialPrice > 0)
        {
            return initialPrice;
        }

        var product = await context.Products.FindAsync(productId);
        return product?.Price ?? 0;
    }

    /// <summary>
    /// Retrieves the price of a product at a given timestamp via PriceHistory.
    /// </summary>
    private async Task<decimal> GetPriceAtTimestampAsync(InventoryDbContext context, int productId, DateTime timestamp, CancellationToken cancellationToken = default)
    {
        var price = await context.ProductPrices
            .Where(pp => pp.ProductId == productId && pp.ValidFrom <= timestamp)
            .OrderByDescending(pp => pp.ValidFrom)
            .Select(pp => pp.Price)
            .FirstOrDefaultAsync(cancellationToken);

        if (price > 0)
        {
            return price;
        }

        return await GetInitialPriceAsync(context, productId);
    }
}
