using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.Data.Repositories;

public class ProductRepository : IProductRepository
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;

    public ProductRepository(IDbContextFactory<InventoryDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IEnumerable<Product>> GetAllAsync(int warehouseId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Products
            .Include(p => p.Category)
            .Include(p => p.ProductStorageLocations)
                .ThenInclude(psl => psl.StorageLocation)
            .Where(p => p.WarehouseId == warehouseId)
            .ToListAsync();
    }

    public async Task<Product?> GetByIdAsync(int id, int warehouseId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Products
            .Include(p => p.Category)
            .Include(p => p.ProductStorageLocations)
                .ThenInclude(psl => psl.StorageLocation)
            .FirstOrDefaultAsync(p => p.Id == id && p.WarehouseId == warehouseId);
    }

    public async Task<Product?> GetByBarcodeAsync(string barcode, int warehouseId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Products
            .Include(p => p.Category)
            .Include(p => p.ProductStorageLocations)
                .ThenInclude(psl => psl.StorageLocation)
            .FirstOrDefaultAsync(p => p.Barcode == barcode && p.WarehouseId == warehouseId);
    }

    public async Task<IEnumerable<Product>> GetByCategoryAsync(int categoryId, int warehouseId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Products
            .Include(p => p.Category)
            .Include(p => p.ProductStorageLocations)
                .ThenInclude(psl => psl.StorageLocation)
            .Where(p => p.CategoryId == categoryId && p.WarehouseId == warehouseId)
            .ToListAsync();
    }

    public async Task<IEnumerable<Product>> GetLowStockAsync(int warehouseId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Products
            .Include(p => p.Category)
            .Include(p => p.ProductStorageLocations)
                .ThenInclude(psl => psl.StorageLocation)
            .Where(p => p.Quantity <= p.MinQuantity && p.WarehouseId == warehouseId)
            .OrderBy(p => p.Quantity)
            .ToListAsync();
    }

    public async Task<Product> CreateAsync(Product product)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        // Verify that the referenced category exists
        if (product.CategoryId > 0)
        {
            var categoryExists = await context.Categories
                .AnyAsync(c => c.Id == product.CategoryId);

            if (!categoryExists)
            {
                throw new InvalidOperationException(
                    $"Category with ID {product.CategoryId} does not exist. Please select a valid category.");
            }

            // Clear navigation property to prevent EF Core from inserting the category
            product.Category = null;
        }
        else
        {
            throw new InvalidOperationException("Product must have a valid CategoryId.");
        }

        product.CreatedAt = DateTime.UtcNow;
        product.UpdatedAt = DateTime.UtcNow;

        context.Products.Add(product);
        await context.SaveChangesAsync();

        return product;
    }

    public async Task<Product> UpdateAsync(Product product)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        product.UpdatedAt = DateTime.UtcNow;
        context.Products.Update(product);
        await context.SaveChangesAsync();
        return product;
    }

    public async Task DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var product = await context.Products.FindAsync(id);
        if (product != null)
        {
            context.Products.Remove(product);
            await context.SaveChangesAsync();
        }
    }

    public async Task<int> GetTotalCountAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Products.CountAsync();
    }

    public async Task<int> GetLowStockCountAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Products.CountAsync(p => p.Quantity <= p.MinQuantity);
    }

    public async Task<IEnumerable<Product>> SearchAsync(string searchTerm, int warehouseId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        if (string.IsNullOrWhiteSpace(searchTerm))
            return Enumerable.Empty<Product>();

        var term = searchTerm.ToLower();
        return await context.Products
            .Include(p => p.Category)
            .Include(p => p.ProductStorageLocations)
                .ThenInclude(psl => psl.StorageLocation)
            .Where(p => p.WarehouseId == warehouseId && (
                p.Name.ToLower().Contains(term) ||
                p.Barcode.ToLower().Contains(term) ||
                p.Description.ToLower().Contains(term)))
            .OrderBy(p => p.Name)
            .ToListAsync();
    }
}
