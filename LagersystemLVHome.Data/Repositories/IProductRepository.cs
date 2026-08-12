using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Data.Repositories;

public interface IProductRepository
{
    Task<IEnumerable<Product>> GetAllAsync(int warehouseId);
    Task<Product?> GetByIdAsync(int id, int warehouseId);
    Task<Product?> GetByBarcodeAsync(string barcode, int warehouseId);
    Task<IEnumerable<Product>> GetByCategoryAsync(int categoryId, int warehouseId);
    Task<IEnumerable<Product>> GetLowStockAsync(int warehouseId);
    Task<Product> CreateAsync(Product product);
    Task<Product> UpdateAsync(Product product);
    Task DeleteAsync(int id);
    Task<IEnumerable<Product>> SearchAsync(string searchTerm, int warehouseId);
}
