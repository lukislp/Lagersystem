using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.Application.Services;

public sealed class ExpiryService : IExpiryService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly INotificationService _notificationService;
    private readonly ILogger<ExpiryService> _logger;

    public ExpiryService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        INotificationService notificationService,
        ILogger<ExpiryService> logger)
    {
        _contextFactory = contextFactory;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<List<Product>> GetExpiringProductsAsync(int warehouseId, int daysThreshold = 7, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var thresholdDate = DateTime.UtcNow.AddDays(daysThreshold);

            return await context.Products
                .Include(p => p.Category)
                .Where(p => p.WarehouseId == warehouseId
                    && p.TrackExpiryDate
                    && p.ExpiryDate.HasValue
                    && p.ExpiryDate.Value <= thresholdDate
                    && p.ExpiryDate.Value >= DateTime.UtcNow)
                .OrderBy(p => p.ExpiryDate)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting expiring products");
            return [];
        }
    }

    public async Task<List<Product>> GetExpiredProductsAsync(int warehouseId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await context.Products
                .Include(p => p.Category)
                .Where(p => p.WarehouseId == warehouseId
                    && p.TrackExpiryDate
                    && p.ExpiryDate.HasValue
                    && p.ExpiryDate.Value < DateTime.UtcNow)
                .OrderBy(p => p.ExpiryDate)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting expired products");
            return [];
        }
    }

    public async Task<bool> ShouldTrackExpiryForCategoryAsync(int categoryId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var category = await context.Categories.FindAsync(categoryId);

            // Auto-enable for food categories
            return category?.Name?.ToLower().Contains("lebensmittel") == true ||
                category?.Name?.ToLower().Contains("food") == true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking expiry tracking for category {CategoryId}", categoryId);
            return false;
        }
    }

    public async Task CheckExpiryAndNotifyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            _logger.LogInformation("Starting expiry check...");

            // Check product-level expiry (legacy, kept for compatibility)
            var productsWithExpiry = await context.Products
                .Include(p => p.Category)
                .Where(p => p.TrackExpiryDate && p.ExpiryDate.HasValue)
                .ToListAsync(cancellationToken);

            foreach (var product in productsWithExpiry)
            {
                if (product.IsExpired)
                {
                    await NotifyExpiredProductAsync(product);
                }
                else if (product.IsExpiringSoon)
                {
                    await NotifyExpiringSoonAsync(product);
                }
            }

            // Check batch-level expiry
            var expiringBatches = await context.ProductBatches
                .Include(pb => pb.Product)
                .ThenInclude(p => p.Category)
                .Where(pb => pb.ExpiryDate.HasValue)
                .ToListAsync(cancellationToken);

            foreach (var batch in expiringBatches)
            {
                if (batch.IsExpired)
                {
                    await NotifyExpiredBatchAsync(batch);
                }
                else if (batch.IsExpiringSoon)
                {
                    await NotifyExpiringSoonBatchAsync(batch);
                }
            }

            _logger.LogInformation("Expiry check completed. Checked {ProductCount} products and {BatchCount} batches.",
                productsWithExpiry.Count, expiringBatches.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during expiry check");
        }
    }

    private async Task NotifyExpiredProductAsync(Product product, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var recentNotification = await context.Notifications
                .AnyAsync(n => n.Message.Contains(product.Name)
                    && n.CreatedAt > DateTime.UtcNow.AddHours(-24)
                    && n.Type == NotificationType.CriticalStock, cancellationToken);

            if (recentNotification)
                return;

            // Notify admins/managers
            var users = await context.Users
                .Where(u => u.WarehouseId == product.WarehouseId
                    && u.IsActive
                    && (u.Role == UserRole.Admin || u.Role == UserRole.SuperAdmin || u.Role == UserRole.Manager))
                .ToListAsync(cancellationToken);

            foreach (var user in users)
            {
                await _notificationService.CreateNotificationAsync(
                    user.Id,
                    NotificationType.CriticalStock,
                    "PRODUKT ABGELAUFEN!",
                    $"Das Produkt '{product.Name}' ist seit dem {product.ExpiryDate:dd.MM.yyyy} abgelaufen!",
                    $"/products?search={product.Name}",
                    NotificationChannel.All);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error notifying expired product {ProductId}", product.Id);
        }
    }

    private async Task NotifyExpiringSoonAsync(Product product, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var recentNotification = await context.Notifications
                .AnyAsync(n => n.Message.Contains(product.Name)
                    && n.CreatedAt > DateTime.UtcNow.AddHours(-24)
                    && n.Type == NotificationType.LowStock, cancellationToken);

            if (recentNotification)
                return;

            // Notify admins/managers
            var users = await context.Users
                .Where(u => u.WarehouseId == product.WarehouseId
                    && u.IsActive
                    && (u.Role == UserRole.Admin || u.Role == UserRole.SuperAdmin || u.Role == UserRole.Manager))
                .ToListAsync(cancellationToken);

            foreach (var user in users)
            {
                await _notificationService.CreateNotificationAsync(
                    user.Id,
                    NotificationType.LowStock,
                    "Produkt l\u00e4uft bald ab",
                    $"Das Produkt '{product.Name}' l\u00e4uft in {product.DaysUntilExpiry} Tagen ab (MHD: {product.ExpiryDate:dd.MM.yyyy}).",
                    $"/products?search={product.Name}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error notifying expiring soon product {ProductId}", product.Id);
        }
    }

    private async Task NotifyExpiredBatchAsync(ProductBatch batch, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var recentNotification = await context.Notifications
                .AnyAsync(n => n.Message.Contains(batch.BatchNumber)
                    && n.CreatedAt > DateTime.UtcNow.AddHours(-24)
                    && n.Type == NotificationType.CriticalStock, cancellationToken);

            if (recentNotification)
                return;

            var users = await context.Users
                .Where(u => u.WarehouseId == batch.WarehouseId
                    && u.IsActive
                    && (u.Role == UserRole.Admin || u.Role == UserRole.SuperAdmin || u.Role == UserRole.Manager))
                .ToListAsync(cancellationToken);

            foreach (var user in users)
            {
                await _notificationService.CreateNotificationAsync(
                    user.Id,
                    NotificationType.CriticalStock,
                    "CHARGE ABGELAUFEN!",
                    $"Charge '{batch.BatchNumber}' von '{batch.Product?.Name}' ist seit dem {batch.ExpiryDate:dd.MM.yyyy} abgelaufen! Noch {batch.Quantity} St\u00fcck im Lager.",
                    $"/products?search={batch.Product?.Name}",
                    NotificationChannel.All);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error notifying expired batch {BatchId}", batch.Id);
        }
    }

    private async Task NotifyExpiringSoonBatchAsync(ProductBatch batch, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var recentNotification = await context.Notifications
                .AnyAsync(n => n.Message.Contains(batch.BatchNumber)
                    && n.CreatedAt > DateTime.UtcNow.AddHours(-24)
                    && n.Type == NotificationType.LowStock, cancellationToken);

            if (recentNotification)
                return;

            var users = await context.Users
                .Where(u => u.WarehouseId == batch.WarehouseId
                    && u.IsActive
                    && (u.Role == UserRole.Admin || u.Role == UserRole.SuperAdmin || u.Role == UserRole.Manager))
                .ToListAsync(cancellationToken);

            foreach (var user in users)
            {
                await _notificationService.CreateNotificationAsync(
                    user.Id,
                    NotificationType.LowStock,
                    "Charge l\u00e4uft bald ab",
                    $"Charge '{batch.BatchNumber}' von '{batch.Product?.Name}' l\u00e4uft in {batch.DaysUntilExpiry} Tagen ab (MHD: {batch.ExpiryDate:dd.MM.yyyy}). Noch {batch.Quantity} St\u00fcck vorhanden.",
                    $"/products?search={batch.Product?.Name}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error notifying expiring soon batch {BatchId}", batch.Id);
        }
    }

    // Batch query methods

    public async Task<List<ProductBatch>> GetExpiringBatchesAsync(int warehouseId, int daysThreshold = 7, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var thresholdDate = DateTime.UtcNow.AddDays(daysThreshold);

            return await context.ProductBatches
                .Include(pb => pb.Product)
                .ThenInclude(p => p.Category)
                .Where(pb => pb.WarehouseId == warehouseId
                    && pb.ExpiryDate.HasValue
                    && pb.ExpiryDate.Value <= thresholdDate
                    && pb.ExpiryDate.Value >= DateTime.UtcNow
                    && pb.Quantity > 0)
                .OrderBy(pb => pb.ExpiryDate)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting expiring batches");
            return [];
        }
    }

    public async Task<int> GetExpiringBatchesCountAsync(
        int warehouseId,
        int daysThreshold = 7,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var thresholdDate = DateTime.UtcNow.AddDays(daysThreshold);

            return await context.ProductBatches
                .Where(pb => pb.WarehouseId == warehouseId
                    && pb.ExpiryDate.HasValue
                    && pb.Quantity > 0
                    && pb.ExpiryDate.Value <= thresholdDate)
                .CountAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting expiring batches");
            return 0;
        }
    }

    public async Task<IReadOnlyList<ProductBatch>> GetAllNonEmptyBatchesWithExpiryAsync(
        int warehouseId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            return await context.ProductBatches
                .AsNoTracking()
                .Include(pb => pb.Product)
                    .ThenInclude(p => p.Category)
                .Where(pb => pb.WarehouseId == warehouseId
                    && pb.ExpiryDate.HasValue
                    && pb.Quantity > 0)
                .OrderBy(pb => pb.ExpiryDate)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading batches with expiry");
            return Array.Empty<ProductBatch>();
        }
    }

    public async Task<Result> MarkBatchAsDisposedAsync(
        int batchId,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var batch = await context.ProductBatches
                .FirstOrDefaultAsync(pb => pb.Id == batchId, cancellationToken);

            if (batch is null)
            {
                return Result.Failure("batch.notfound", $"Batch {batchId} no longer exists");
            }

            if (batch.Quantity <= 0)
            {
                return Result.Failure("batch.alreadydisposed", "Batch is already empty");
            }

            var disposedQuantity = batch.Quantity;

            context.StockMovements.Add(new StockMovement
            {
                ProductId = batch.ProductId,
                QuantityChange = -disposedQuantity,
                Type = MovementType.Disposal,
                Notes = notes ?? $"Disposed: batch {batch.BatchNumber} expired {-batch.DaysUntilExpiry} days ago",
                WarehouseId = batch.WarehouseId,
                Timestamp = DateTime.UtcNow
            });

            var product = await context.Products.FindAsync([batch.ProductId], cancellationToken);
            if (product is not null)
            {
                product.Quantity = Math.Max(0, product.Quantity - disposedQuantity);
                product.UpdatedAt = DateTime.UtcNow;
            }

            batch.Quantity = 0;
            batch.UpdatedAt = DateTime.UtcNow;

            await context.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing batch {BatchId}", batchId);
            return Result.Failure("batch.disposefailed", ex.Message);
        }
    }

    public async Task<List<ProductBatch>> GetExpiredBatchesAsync(int warehouseId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await context.ProductBatches
                .Include(pb => pb.Product)
                .ThenInclude(p => p.Category)
                .Where(pb => pb.WarehouseId == warehouseId
                    && pb.ExpiryDate.HasValue
                    && pb.ExpiryDate.Value < DateTime.UtcNow
                    && pb.Quantity > 0)
                .OrderBy(pb => pb.ExpiryDate)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting expired batches");
            return [];
        }
    }

    public async Task<ProductBatch?> GetNextExpiringBatchForProductAsync(int productId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await context.ProductBatches
                .Where(pb => pb.ProductId == productId
                    && pb.ExpiryDate.HasValue
                    && pb.Quantity > 0)
                .OrderBy(pb => pb.ExpiryDate)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting next expiring batch for product {ProductId}", productId);
            return null;
        }
    }

    public async Task<List<ProductBatch>> GetBatchesForProductAsync(int productId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await context.ProductBatches
                .Where(pb => pb.ProductId == productId)
                .OrderBy(pb => pb.ExpiryDate ?? DateTime.MaxValue)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting batches for product {ProductId}", productId);
            return [];
        }
    }
}
