using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.Application.Services;

public sealed class PriceHistoryService : IPriceHistoryService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly IAuthService _authService;
    private readonly IAuditService _auditService;
    private readonly ILogger<PriceHistoryService> _logger;

    public PriceHistoryService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        IAuthService authService,
        IAuditService auditService,
        ILogger<PriceHistoryService> logger)
    {
        _contextFactory = contextFactory;
        _authService = authService;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<ProductPrice> AddPriceAsync(
        int productId, decimal price, DateTime validFrom, DateTime? validTo,
        string? reason = null, string? notes = null, string? createdBy = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        if (price <= 0)
            throw new ArgumentException("Preis muss gr\u00f6\u00dfer als 0 sein", nameof(price));

        if (validTo.HasValue && validTo.Value <= validFrom)
            throw new ArgumentException("ValidTo muss nach ValidFrom liegen", nameof(validTo));

        var product = await context.Products.FindAsync(productId);
        if (product == null)
            throw new InvalidOperationException($"Produkt mit ID {productId} nicht gefunden");

        var overlappingPrices = await context.ProductPrices
            .Where(pp => pp.ProductId == productId)
            .Where(pp =>
                (pp.ValidFrom <= validFrom && (!pp.ValidTo.HasValue || pp.ValidTo.Value >= validFrom)) ||
                (validTo.HasValue && pp.ValidFrom <= validTo.Value && (!pp.ValidTo.HasValue || pp.ValidTo.Value >= validTo.Value)))
            .ToListAsync(cancellationToken);

        if (overlappingPrices.Any())
        {
            foreach (var overlap in overlappingPrices)
            {
                if (overlap.ValidFrom < validFrom)
                {
                    overlap.ValidTo = validFrom.AddSeconds(-1);
                }
                else
                {
                    context.ProductPrices.Remove(overlap);
                }
            }
        }

        var currentUser = await _authService.GetCurrentUserAsync();

        var productPrice = new ProductPrice
        {
            ProductId = productId,
            Price = price,
            ValidFrom = validFrom,
            ValidTo = validTo,
            Reason = reason,
            Notes = notes,
            CreatedBy = createdBy ?? currentUser?.Username ?? "System",
            WarehouseId = _authService.GetCurrentWarehouseId(),
            CreatedAt = DateTime.UtcNow
        };

        context.ProductPrices.Add(productPrice);

        // Sync Product.Price directly from the values already in scope instead of re-reading
        // "the current price" through GetCurrentPriceAsync's own separate DbContext - that read
        // would run against the database as it stood before this call's pending insert is saved,
        // so it could never see the price being added here.
        var now = DateTime.UtcNow;
        if (validFrom <= now && (!validTo.HasValue || validTo.Value >= now))
        {
            product.Price = price;
        }

        await context.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "PRODUCT_PRICE_CREATED", "ProductPrice", productPrice.Id,
            new { Price = price, ProductName = product.Name, ValidFrom = validFrom, ValidTo = validTo, Reason = reason },
            AuditSeverity.Info);

        return productPrice;
    }

    public async Task<ProductPrice> UpdateCurrentPriceAsync(int productId, decimal newPrice, string? reason = null, string? createdBy = null, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var currentPrice = await GetCurrentPriceAsync(productId);
        if (currentPrice != null)
        {
            currentPrice.ValidTo = DateTime.UtcNow.AddSeconds(-1);
            context.ProductPrices.Update(currentPrice);
        }

        return await AddPriceAsync(productId, newPrice, DateTime.UtcNow, null, reason,
            $"Preis\u00e4nderung von {(currentPrice != null ? currentPrice.Price.ToString("C") : "N/A")} auf {newPrice:C}", createdBy);
    }

    public async Task<ProductPrice> ScheduleFuturePriceAsync(int productId, decimal price, DateTime validFrom, DateTime? validTo, string? reason = null, string? createdBy = null, CancellationToken cancellationToken = default)
    {
        if (validFrom <= DateTime.UtcNow)
            throw new ArgumentException("ValidFrom muss in der Zukunft liegen", nameof(validFrom));

        return await AddPriceAsync(productId, price, validFrom, validTo, reason, $"Geplante Preis\u00e4nderung: {reason}", createdBy);
    }

    // Core methods for auto-tracking

    public async Task<ProductPrice?> GetCurrentPriceAsync(int productId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;

        return await context.ProductPrices
            .Where(pp => pp.ProductId == productId)
            .Where(pp => pp.ValidFrom <= now && (!pp.ValidTo.HasValue || pp.ValidTo.Value >= now))
            .OrderByDescending(pp => pp.ValidFrom)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ProductPrice?> GetPriceAtDateAsync(int productId, DateTime date, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.ProductPrices
            .Where(pp => pp.ProductId == productId)
            .Where(pp => pp.ValidFrom <= date && (!pp.ValidTo.HasValue || pp.ValidTo.Value >= date))
            .OrderByDescending(pp => pp.ValidFrom)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<ProductPrice>> GetPriceHistoryAsync(int productId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ProductPrices
            .Where(pp => pp.ProductId == productId)
            .OrderByDescending(pp => pp.ValidFrom)
            .ToListAsync(cancellationToken);
    }

    public async Task CreateInitialPriceAsync(int productId, int warehouseId, decimal price, string currency, string? createdBy, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var initialPrice = new ProductPrice
        {
            ProductId = productId,
            WarehouseId = warehouseId,
            Price = price,
            ValidFrom = DateTime.UtcNow,
            ValidTo = null,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow,
            Reason = "Initialpreis bei Produkterstellung",
            Notes = "Automatisch erstellt beim Anlegen des Produkts"
        };

        context.ProductPrices.Add(initialPrice);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Initial price {Price} created for product {ProductId} by {User}",
            price, productId, createdBy ?? "System");
    }

    /// <summary>
    /// Automatically updates the price (closes old entry, creates new one).
    /// </summary>
    public async Task UpdatePriceAutomaticAsync(int productId, int warehouseId, decimal oldPrice, decimal newPrice, string currency, string? updatedBy, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTime.UtcNow;

        // Close current price entry
        var currentPrice = await context.ProductPrices
            .Where(pp => pp.ProductId == productId && pp.ValidTo == null)
            .FirstOrDefaultAsync(cancellationToken);

        if (currentPrice != null)
        {
            currentPrice.ValidTo = now;
            _logger.LogInformation("Closed price entry {PriceId} with ValidTo={ValidTo} for product {ProductId}",
                currentPrice.Id, currentPrice.ValidTo, productId);
        }

        // Create new price entry
        var priceChange = newPrice - oldPrice;
        var priceChangePercent = oldPrice > 0 ? (priceChange / oldPrice) * 100 : 0;

        var newPriceEntry = new ProductPrice
        {
            ProductId = productId,
            WarehouseId = warehouseId,
            Price = newPrice,
            ValidFrom = now,
            ValidTo = null,
            CreatedBy = updatedBy,
            CreatedAt = now,
            Reason = "Preis\u00e4nderung via Produktbearbeitung",
            Notes = $"Alter Preis: {oldPrice:C} -> Neuer Preis: {newPrice:C} ({priceChange:+0.00;-0.00} / {priceChangePercent:+0.0;-0.0}%)"
        };

        context.ProductPrices.Add(newPriceEntry);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created new price entry for product {ProductId}: {OldPrice} -> {NewPrice} by {User}",
            productId, oldPrice, newPrice, updatedBy ?? "System");
    }

    public async Task<bool> HasPriceHistoryAsync(int productId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ProductPrices
            .AnyAsync(pp => pp.ProductId == productId, cancellationToken);
    }

    public async Task<int> GetPriceChangeCountAsync(int productId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ProductPrices
            .Where(pp => pp.ProductId == productId)
            .CountAsync(cancellationToken);
    }

    public async Task<PriceStatistics> GetPriceStatisticsAsync(int productId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var prices = await context.ProductPrices
            .Where(pp => pp.ProductId == productId)
            .OrderBy(pp => pp.ValidFrom)
            .ToListAsync(cancellationToken);

        if (!prices.Any())
        {
            return new PriceStatistics();
        }

        var currentPrice = prices.FirstOrDefault(p => p.ValidTo == null);
        var previousPrice = prices
            .Where(p => p.ValidTo != null)
            .OrderByDescending(p => p.ValidFrom)
            .FirstOrDefault();

        decimal? priceChange = null;
        double? priceChangePercent = null;

        if (currentPrice != null && previousPrice != null)
        {
            priceChange = currentPrice.Price - previousPrice.Price;
            priceChangePercent = previousPrice.Price > 0
                ? (double)((priceChange.Value / previousPrice.Price) * 100)
                : 0;
        }

        return new PriceStatistics
        {
            MinPrice = prices.Min(p => p.Price),
            MaxPrice = prices.Max(p => p.Price),
            AveragePrice = prices.Average(p => p.Price),
            TotalChanges = prices.Count,
            FirstPriceDate = prices.First().ValidFrom,
            LastPriceDate = prices.Last().ValidFrom,
            CurrentPrice = currentPrice?.Price,
            PriceChange = priceChange,
            PriceChangePercent = priceChangePercent
        };
    }

    /// <summary>
    /// Monthly price change statistics.
    /// </summary>
    public async Task<MonthlyPriceStatistics> GetMonthlyStatisticsAsync(int productId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var monthStart = DateTime.SpecifyKind(new DateTime(now.Year, now.Month, 1, 0, 0, 0), DateTimeKind.Utc);
        var monthEnd = monthStart.AddMonths(1);

        var pricesThisMonth = await context.ProductPrices
            .Where(pp => pp.ProductId == productId)
            .Where(pp => pp.ValidFrom >= monthStart && pp.ValidFrom < monthEnd)
            .OrderBy(pp => pp.ValidFrom)
            .ToListAsync(cancellationToken);

        var startPrice = await context.ProductPrices
            .Where(pp => pp.ProductId == productId)
            .Where(pp => pp.ValidFrom <= monthStart && (!pp.ValidTo.HasValue || pp.ValidTo.Value >= monthStart))
            .OrderByDescending(pp => pp.ValidFrom)
            .Select(pp => pp.Price)
            .FirstOrDefaultAsync(cancellationToken);

        var endPrice = await context.ProductPrices
            .Where(pp => pp.ProductId == productId)
            .Where(pp => pp.ValidFrom <= monthEnd && (!pp.ValidTo.HasValue || pp.ValidTo.Value >= monthEnd))
            .OrderByDescending(pp => pp.ValidFrom)
            .Select(pp => pp.Price)
            .FirstOrDefaultAsync(cancellationToken);

        if (endPrice == 0)
        {
            var currentPrice = await GetCurrentPriceAsync(productId);
            if (currentPrice != null)
            {
                endPrice = currentPrice.Price;
            }
        }

        if (startPrice == 0)
        {
            var firstPrice = await context.ProductPrices
                .Where(pp => pp.ProductId == productId)
                .OrderBy(pp => pp.ValidFrom)
                .Select(pp => pp.Price)
                .FirstOrDefaultAsync(cancellationToken);

            if (firstPrice > 0)
            {
                startPrice = firstPrice;
            }
        }

        if (startPrice == 0 && endPrice == 0)
        {
            return new MonthlyPriceStatistics
            {
                Month = now.Month,
                Year = now.Year,
                ChangesCount = pricesThisMonth.Count,
                HasData = false
            };
        }

        if (startPrice == 0) startPrice = endPrice;
        if (endPrice == 0) endPrice = startPrice;

        var priceChange = endPrice - startPrice;
        var priceChangePercent = startPrice > 0 ? (priceChange / startPrice) * 100 : 0;

        return new MonthlyPriceStatistics
        {
            Month = now.Month,
            Year = now.Year,
            StartPrice = startPrice,
            EndPrice = endPrice,
            PriceChange = priceChange,
            PriceChangePercent = (double)priceChangePercent,
            ChangesCount = pricesThisMonth.Count,
            HasData = true
        };
    }

    /// <summary>
    /// Yearly price change statistics.
    /// </summary>
    public async Task<YearlyPriceStatistics> GetYearlyStatisticsAsync(int productId, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var yearStart = DateTime.SpecifyKind(new DateTime(now.Year, 1, 1, 0, 0, 0), DateTimeKind.Utc);
        var yearEnd = yearStart.AddYears(1);

        var pricesThisYear = await context.ProductPrices
            .Where(pp => pp.ProductId == productId)
            .Where(pp => pp.ValidFrom >= yearStart && pp.ValidFrom < yearEnd)
            .OrderBy(pp => pp.ValidFrom)
            .ToListAsync(cancellationToken);

        var startPrice = await context.ProductPrices
            .Where(pp => pp.ProductId == productId)
            .Where(pp => pp.ValidFrom <= yearStart && (!pp.ValidTo.HasValue || pp.ValidTo.Value >= yearStart))
            .OrderByDescending(pp => pp.ValidFrom)
            .Select(pp => pp.Price)
            .FirstOrDefaultAsync(cancellationToken);

        var endPrice = await context.ProductPrices
            .Where(pp => pp.ProductId == productId)
            .Where(pp => pp.ValidFrom <= yearEnd && (!pp.ValidTo.HasValue || pp.ValidTo.Value >= yearEnd))
            .OrderByDescending(pp => pp.ValidFrom)
            .Select(pp => pp.Price)
            .FirstOrDefaultAsync(cancellationToken);

        if (endPrice == 0)
        {
            var currentPrice = await GetCurrentPriceAsync(productId);
            if (currentPrice != null)
            {
                endPrice = currentPrice.Price;
            }
        }

        if (startPrice == 0)
        {
            var firstPrice = await context.ProductPrices
                .Where(pp => pp.ProductId == productId)
                .OrderBy(pp => pp.ValidFrom)
                .Select(pp => pp.Price)
                .FirstOrDefaultAsync(cancellationToken);

            if (firstPrice > 0)
            {
                startPrice = firstPrice;
            }
        }

        if (startPrice == 0 && endPrice == 0)
        {
            return new YearlyPriceStatistics
            {
                Year = now.Year,
                ChangesCount = pricesThisYear.Count,
                HasData = false
            };
        }

        if (startPrice == 0) startPrice = endPrice;
        if (endPrice == 0) endPrice = startPrice;

        var priceChange = endPrice - startPrice;
        var priceChangePercent = startPrice > 0 ? (priceChange / startPrice) * 100 : 0;

        var minPrice = endPrice;
        var maxPrice = endPrice;

        if (pricesThisYear.Any())
        {
            minPrice = Math.Min(startPrice, pricesThisYear.Min(p => p.Price));
            maxPrice = Math.Max(startPrice, pricesThisYear.Max(p => p.Price));
        }
        else
        {
            minPrice = Math.Min(startPrice, endPrice);
            maxPrice = Math.Max(startPrice, endPrice);
        }

        return new YearlyPriceStatistics
        {
            Year = now.Year,
            StartPrice = startPrice,
            EndPrice = endPrice,
            PriceChange = priceChange,
            PriceChangePercent = (double)priceChangePercent,
            ChangesCount = pricesThisYear.Count,
            MaxPrice = maxPrice,
            MinPrice = minPrice,
            HasData = true
        };
    }
}
