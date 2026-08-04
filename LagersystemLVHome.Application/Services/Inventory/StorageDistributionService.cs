using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.Application.Services;

/// <inheritdoc cref="IStorageDistributionService"/>
public sealed class StorageDistributionService : IStorageDistributionService
{
    private readonly InventoryDbContext _db;

    public StorageDistributionService(InventoryDbContext db)
    {
        _db = db;
    }

    public async Task<StorageDistributionData> GetDistributionDataAsync(
        int productId,
        int warehouseId,
        CancellationToken cancellationToken = default)
    {
        var locations = await _db.StorageLocations
            .AsNoTracking()
            .Where(sl => sl.WarehouseId == warehouseId && sl.IsActive)
            .OrderBy(sl => sl.Code)
            .ToListAsync(cancellationToken);

        IReadOnlyDictionary<int, int> assignments;
        if (productId > 0)
        {
            assignments = await _db.ProductStorageLocations
                .AsNoTracking()
                .Where(psl => psl.ProductId == productId)
                .ToDictionaryAsync(
                    psl => psl.StorageLocationId,
                    psl => psl.Quantity,
                    cancellationToken);
        }
        else
        {
            assignments = new Dictionary<int, int>();
        }

        return new StorageDistributionData(locations, assignments);
    }
}
