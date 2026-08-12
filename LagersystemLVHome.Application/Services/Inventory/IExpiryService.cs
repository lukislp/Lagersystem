using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.Application.Services;

public interface IExpiryService
{
    Task<List<Product>> GetExpiringProductsAsync(int warehouseId, int daysThreshold = 7, CancellationToken cancellationToken = default);
    Task<List<Product>> GetExpiredProductsAsync(int warehouseId, CancellationToken cancellationToken = default);
    Task<bool> ShouldTrackExpiryForCategoryAsync(int categoryId, CancellationToken cancellationToken = default);
    Task CheckExpiryAndNotifyAsync(CancellationToken cancellationToken = default);

    // Batch management
    Task<List<ProductBatch>> GetExpiringBatchesAsync(int warehouseId, int daysThreshold = 7, CancellationToken cancellationToken = default);
    Task<int> GetExpiringBatchesCountAsync(int warehouseId, int daysThreshold = 7, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads every non-empty batch of a warehouse that has an expiry date,
    /// including <see cref="ProductBatch.Product"/> and its
    /// <see cref="Product.Category"/>. Used by the Expiry Monitoring page.
    /// </summary>
    Task<IReadOnlyList<ProductBatch>> GetAllNonEmptyBatchesWithExpiryAsync(int warehouseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a batch as disposed: records a disposal stock movement, updates
    /// the product's on-hand quantity and zeroes out the batch. All three
    /// operations happen in a single unit of work. Returns a failure result
    /// if the batch or its product is no longer available.
    /// </summary>
    Task<Result> MarkBatchAsDisposedAsync(int batchId, string? notes = null, CancellationToken cancellationToken = default);

    Task<List<ProductBatch>> GetExpiredBatchesAsync(int warehouseId, CancellationToken cancellationToken = default);
    Task<ProductBatch?> GetNextExpiringBatchForProductAsync(int productId, CancellationToken cancellationToken = default);
    Task<List<ProductBatch>> GetBatchesForProductAsync(int productId, CancellationToken cancellationToken = default);
}
