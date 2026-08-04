using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.Application.Services;
using LagersystemLVHome.API.DTOs;
using LagersystemLVHome.Domain.Models;

namespace LagersystemLVHome.API.Controllers;

/// <summary>
/// API controller for category management.
/// </summary>
[ApiController]
[Route("api/categories")]
public class CategoriesApiController : BaseApiController
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly IAuditService _auditService;
    private readonly ILogger<CategoriesApiController> _logger;

    public CategoriesApiController(
        IDbContextFactory<InventoryDbContext> contextFactory,
        IAuditService auditService,
        ILogger<CategoriesApiController> logger)
    {
        _contextFactory = contextFactory;
        _auditService = auditService;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<CategoryDto>>), 200)]
    public async Task<ActionResult<ApiResponse<List<CategoryDto>>>> GetCategories()
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var categories = await context.Categories
                .Where(c => c.WarehouseId == CurrentWarehouseId && c.IsActive)
                .Select(c => new CategoryDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    Icon = c.Icon,
                    Description = c.Description,
                    ProductCount = c.Products.Count,
                    IsActive = c.IsActive
                })
                .OrderBy(c => c.Name)
                .ToListAsync();

            _logger.LogInformation("API: {Count} categories fetched by user {UserId}",
                categories.Count, CurrentUserId);

            return Success(categories);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching categories");
            return Error<List<CategoryDto>>("Error fetching categories", 500);
        }
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<CategoryDto>), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<ApiResponse<CategoryDto>>> GetCategory(int id)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var category = await context.Categories
                .Where(c => c.Id == id && c.WarehouseId == CurrentWarehouseId)
                .Select(c => new CategoryDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    Icon = c.Icon,
                    Description = c.Description,
                    ProductCount = c.Products.Count,
                    IsActive = c.IsActive
                })
                .FirstOrDefaultAsync();

            if (category == null)
            {
                return NotFound<CategoryDto>($"Category with ID {id} not found");
            }

            return Success(category);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching category {CategoryId}", id);
            return Error<CategoryDto>("Error fetching category", 500);
        }
    }

    [HttpGet("{id}/products")]
    [ProducesResponseType(typeof(ApiResponse<List<ProductDto>>), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<ApiResponse<List<ProductDto>>>> GetCategoryProducts(int id)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var category = await context.Categories
                .FirstOrDefaultAsync(c => c.Id == id && c.WarehouseId == CurrentWarehouseId);

            if (category == null)
            {
                return NotFound<List<ProductDto>>($"Category with ID {id} not found");
            }

            var products = await context.Products
                .Where(p => p.CategoryId == id && p.WarehouseId == CurrentWarehouseId)
                .Select(p => new ProductDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Barcode = p.Barcode,
                    Description = p.Description,
                    Quantity = p.Quantity,
                    MinStock = p.MinQuantity,
                    PurchasePrice = (double?)p.Price,
                    SalePrice = (double?)(p.Price * 1.3m),
                    ImageUrl = p.ImageUrl,
                    CreatedAt = p.CreatedAt,
                    UpdatedAt = p.UpdatedAt,
                    CategoryId = p.CategoryId,
                    CategoryName = category.Name,
                    CategoryIcon = category.Icon
                })
                .OrderBy(p => p.Name)
                .ToListAsync();

            return Success(products);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching products for category {CategoryId}", id);
            return Error<List<ProductDto>>("Error fetching category products", 500);
        }
    }

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CategoryDto>), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(403)]
    public async Task<ActionResult<ApiResponse<CategoryDto>>> CreateCategory([FromBody] CreateCategoryRequest request)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            if (!IsAdmin)
            {
                return Forbidden<CategoryDto>("Only admins can create categories");
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Error<CategoryDto>("Category name is required");
            }

            var exists = await context.Categories
                .AnyAsync(c => c.Name == request.Name && c.WarehouseId == CurrentWarehouseId);

            if (exists)
            {
                return Error<CategoryDto>($"Category '{request.Name}' already exists");
            }

            var category = new Category
            {
                Name = request.Name,
                Icon = request.Icon,
                Description = request.Description,
                WarehouseId = CurrentWarehouseId,
                IsActive = true
            };

            context.Categories.Add(category);
            await context.SaveChangesAsync();

            await _auditService.LogAsync(
                "CATEGORY_CREATED_API",
                "Category",
                category.Id,
                new { Name = category.Name, Via = "REST API" },
                AuditSeverity.Info);

            _logger.LogInformation("API: Category created: {CategoryId} by user {UserId}",
                category.Id, CurrentUserId);

            var dto = new CategoryDto
            {
                Id = category.Id,
                Name = category.Name,
                Icon = category.Icon,
                Description = category.Description,
                ProductCount = 0,
                IsActive = category.IsActive
            };

            return CreatedAtAction(nameof(GetCategory), new { id = category.Id },
                ApiResponse<CategoryDto>.SuccessResult(dto, "Category created successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error creating category");
            return Error<CategoryDto>("Error creating category", 500);
        }
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ApiResponse<CategoryDto>), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(403)]
    public async Task<ActionResult<ApiResponse<CategoryDto>>> UpdateCategory(int id, [FromBody] UpdateCategoryRequest request)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            if (!IsAdmin)
            {
                return Forbidden<CategoryDto>("Only admins can update categories");
            }

            var category = await context.Categories
                .FirstOrDefaultAsync(c => c.Id == id && c.WarehouseId == CurrentWarehouseId);

            if (category == null)
            {
                return NotFound<CategoryDto>($"Category with ID {id} not found");
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Error<CategoryDto>("Category name is required");
            }

            if (request.Name != category.Name)
            {
                var exists = await context.Categories
                    .AnyAsync(c => c.Name == request.Name && c.WarehouseId == CurrentWarehouseId && c.Id != id);

                if (exists)
                {
                    return Error<CategoryDto>($"Category '{request.Name}' already exists");
                }
            }

            category.Name = request.Name;
            category.Icon = request.Icon;
            category.Description = request.Description;

            await context.SaveChangesAsync();

            await _auditService.LogAsync(
                "CATEGORY_UPDATED_API",
                "Category",
                category.Id,
                new { Name = category.Name, Via = "REST API" },
                AuditSeverity.Info);

            _logger.LogInformation("API: Category updated: {CategoryId} by user {UserId}",
                category.Id, CurrentUserId);

            var productCount = await context.Products
                .CountAsync(p => p.CategoryId == category.Id);

            var dto = new CategoryDto
            {
                Id = category.Id,
                Name = category.Name,
                Icon = category.Icon,
                Description = category.Description,
                ProductCount = productCount,
                IsActive = category.IsActive
            };

            return Success(dto, "Category updated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error updating category {CategoryId}", id);
            return Error<CategoryDto>("Error updating category", 500);
        }
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(403)]
    public async Task<ActionResult<ApiResponse<object>>> DeleteCategory(int id)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            if (!IsAdmin)
            {
                return Forbidden<object>("Only admins can delete categories");
            }

            var category = await context.Categories
                .Include(c => c.Products)
                .FirstOrDefaultAsync(c => c.Id == id && c.WarehouseId == CurrentWarehouseId);

            if (category == null)
            {
                return NotFound<object>($"Category with ID {id} not found");
            }

            if (category.Products.Any())
            {
                return Error<object>($"Cannot delete category '{category.Name}' because it has {category.Products.Count} assigned products");
            }

            var categoryName = category.Name;
            context.Categories.Remove(category);
            await context.SaveChangesAsync();

            await _auditService.LogAsync(
                "CATEGORY_DELETED_API",
                "Category",
                id,
                new { Name = categoryName, Via = "REST API" },
                AuditSeverity.Warning);

            _logger.LogInformation("API: Category deleted: {CategoryId} by user {UserId}",
                id, CurrentUserId);

            return Success<object>(new { id, name = categoryName }, "Category deleted successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error deleting category {CategoryId}", id);
            return Error<object>("Error deleting category", 500);
        }
    }
}
