using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Provides the query data used by the StorageDistributionPanel component.
/// Encapsulates direct DbContext access so the Razor component stays free
/// of persistence concerns.
/// </summary>
public interface IStorageDistributionService
{
    /// <summary>
    /// Returns all active storage locations for the given warehouse and,
    /// when <paramref name="productId"/> is positive, the current quantity
    /// assigned to each location for that product.
    /// </summary>
    Task<StorageDistributionData> GetDistributionDataAsync(
        int productId,
        int warehouseId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Query result for <see cref="IStorageDistributionService"/>.
/// </summary>
/// <param name="Locations">Active storage locations ordered by <see cref="StorageLocation.Code"/>.</param>
/// <param name="ExistingAssignments">Map of <see cref="StorageLocation.Id"/> to the quantity assigned to the product.</param>
public sealed record StorageDistributionData(
    IReadOnlyList<StorageLocation> Locations,
    IReadOnlyDictionary<int, int> ExistingAssignments);
