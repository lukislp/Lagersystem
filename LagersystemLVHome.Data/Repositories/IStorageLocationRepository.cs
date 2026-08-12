using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Data.Repositories;

public interface IStorageLocationRepository
{
    Task<IEnumerable<StorageLocation>> GetAllAsync(int warehouseId);
    Task<StorageLocation?> GetByIdAsync(int id, int warehouseId);
    Task<StorageLocation?> GetByCodeAsync(string code, int warehouseId);
    Task<StorageLocation?> GetByQRCodeAsync(string qrCode, int warehouseId);
    Task<IEnumerable<StorageLocation>> GetByAisleAsync(string aisle, int warehouseId);
    Task<IEnumerable<StorageLocation>> GetByRoomAsync(string room, int warehouseId);
    Task<IEnumerable<string>> GetAllRoomsAsync(int warehouseId);
    Task<IEnumerable<Product>> GetProductsByLocationAsync(int locationId, int warehouseId);
    Task<StorageLocation> CreateAsync(StorageLocation location);
    Task<StorageLocation> UpdateAsync(StorageLocation location);
    Task<StorageLocation> GenerateQRCodeAsync(int locationId, string qrCodeContent);
    Task DeleteAsync(int id);
    Task<bool> CodeExistsAsync(string code, int warehouseId, int? excludeId = null);
}
