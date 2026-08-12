using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Data.Repositories;

public class CategoryRepository : ICategoryRepository
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;

    public CategoryRepository(IDbContextFactory<InventoryDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IEnumerable<Category>> GetAllAsync(int warehouseId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Categories
            .Include(c => c.Products)
            .Where(c => c.WarehouseId == warehouseId)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<Category>> GetActiveAsync(int warehouseId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Categories
            .Where(c => c.IsActive && c.WarehouseId == warehouseId)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<Category?> GetByIdAsync(int id, int warehouseId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Categories
            .Include(c => c.Products)
            .FirstOrDefaultAsync(c => c.Id == id && c.WarehouseId == warehouseId);
    }

    public async Task<Category?> GetByNameAsync(string name)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Categories
            .FirstOrDefaultAsync(c => c.Name.ToLower() == name.ToLower());
    }

    public async Task<Category> CreateAsync(Category category)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        category.CreatedAt = DateTime.UtcNow;
        context.Categories.Add(category);
        await context.SaveChangesAsync();
        return category;
    }

    public async Task<Category> UpdateAsync(Category category)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Categories.Update(category);
        await context.SaveChangesAsync();
        return category;
    }

    public async Task DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var category = await context.Categories.FindAsync(id);
        if (category != null)
        {
            context.Categories.Remove(category);
            await context.SaveChangesAsync();
        }
    }

    public async Task<int> GetProductCountAsync(int categoryId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Products.CountAsync(p => p.CategoryId == categoryId);
    }
}
