using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Data.Repositories;

public interface ICategoryRepository
{
    Task<IEnumerable<Category>> GetAllAsync(int warehouseId);
    Task<Category?> GetByIdAsync(int id, int warehouseId);
    Task<IEnumerable<Category>> GetActiveAsync(int warehouseId);
    Task<Category> CreateAsync(Category category);
    Task<Category> UpdateAsync(Category category);
    Task DeleteAsync(int id);
}
