using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.Application.Services;
using LagersystemLVHome.API.DTOs;
using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.API.Mapping;

namespace LagersystemLVHome.API.Controllers;

/// <summary>
/// API controller for product management.
/// </summary>
[ApiController]
[Route("api/products")]
public class ProductsApiController : BaseApiController
{
    private readonly IInventoryService _inventoryService;
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly IAuditService _auditService;
    private readonly ILogger<ProductsApiController> _logger;

    public ProductsApiController(
        IInventoryService inventoryService,
        IDbContextFactory<InventoryDbContext> contextFactory,
        IAuditService auditService,
        ILogger<ProductsApiController> logger)
    {
        _inventoryService = inventoryService;
        _contextFactory = contextFactory;
        _auditService = auditService;
        _logger = logger;
    }

    /// <param name="page">Page number (default: 1).</param>
    /// <param name="pageSize">Items per page (default: 50).</param>
    /// <param name="search">Search term.</param>
    /// <param name="categoryId">Filter by category ID.</param>
    /// <param name="lowStock">Only products with low stock.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<ProductDto>), 200)]
    public async Task<ActionResult<PaginatedResponse<ProductDto>>> GetProducts(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null,
        [FromQuery] int? categoryId = null,
        [FromQuery] bool lowStock = false)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var query = context.Products
                .Include(p => p.Category)
                .Include(p => p.ProductStorageLocations)
                .ThenInclude(psl => psl.StorageLocation)
                .Where(p => p.WarehouseId == CurrentWarehouseId);

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(p =>
                    p.Name.Contains(search) ||
                    (p.Barcode != null && p.Barcode.Contains(search)) ||
                    (p.Description != null && p.Description.Contains(search)));
            }

            if (categoryId.HasValue)
            {
                query = query.Where(p => p.CategoryId == categoryId.Value);
            }

            if (lowStock)
            {
                query = query.Where(p => p.Quantity <= p.MinQuantity);
            }

            var totalCount = await query.CountAsync();

            var products = await query
                .OrderBy(p => p.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
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
                    CategoryName = p.Category != null ? p.Category.Name : null,
                    CategoryIcon = p.Category != null ? p.Category.Icon : null,
                    StorageLocations = p.ProductStorageLocations.Select(psl => new StorageLocationDto
                    {
                        Id = psl.StorageLocationId,
                        Code = psl.StorageLocation.Code,
                        Name = psl.StorageLocation.Name,
                        RoomId = null,
                        RoomName = psl.StorageLocation.Room,
                        Quantity = psl.Quantity
                    }).ToList()
                })
                .ToListAsync();

            _logger.LogInformation("API: {Count} products fetched by user {UserId}",
                products.Count, CurrentUserId);

            return Paginated(products, page, pageSize, totalCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching products");
            return StatusCode(500, new PaginatedResponse<ProductDto>
            {
                Success = false,
                Errors = new List<string> { "Error fetching products" }
            });
        }
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<ProductDto>), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<ApiResponse<ProductDto>>> GetProduct(int id)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var product = await context.Products
                .Include(p => p.Category)
                .Include(p => p.ProductStorageLocations)
                .ThenInclude(psl => psl.StorageLocation)
                .FirstOrDefaultAsync(p => p.Id == id && p.WarehouseId == CurrentWarehouseId);

            if (product == null)
            {
                return NotFound<ProductDto>($"Product with ID {id} not found");
            }

            var dto = product.ToDto();
            dto.StorageLocations = product.ProductStorageLocations.Select(psl => new StorageLocationDto
            {
                Id = psl.StorageLocationId,
                Code = psl.StorageLocation.Code,
                Name = psl.StorageLocation.Name,
                RoomId = null,
                RoomName = psl.StorageLocation.Room,
                Quantity = psl.Quantity
            }).ToList();

            return Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching product {ProductId}", id);
            return Error<ProductDto>("Error fetching product", 500);
        }
    }

    /// <summary>
    /// Searches for a product by barcode.
    /// </summary>
    [HttpGet("barcode/{barcode}")]
    [ProducesResponseType(typeof(ApiResponse<ProductDto>), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<ApiResponse<ProductDto>>> GetProductByBarcode(string barcode)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var product = await context.Products
                .Include(p => p.Category)
                .Include(p => p.ProductStorageLocations)
                .ThenInclude(psl => psl.StorageLocation)
                .FirstOrDefaultAsync(p => p.Barcode == barcode && p.WarehouseId == CurrentWarehouseId);

            if (product == null)
            {
                return NotFound<ProductDto>($"Product with barcode '{barcode}' not found");
            }

            var dto = product.ToDto();

            return Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching product by barcode {Barcode}", barcode);
            return Error<ProductDto>("Error fetching product", 500);
        }
    }

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<ProductDto>), 201)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<ApiResponse<ProductDto>>> CreateProduct([FromBody] CreateProductRequest request)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Error<ProductDto>("Product name is required");
            }

            if (!string.IsNullOrWhiteSpace(request.Barcode))
            {
                var exists = await context.Products
                    .AnyAsync(p => p.Barcode == request.Barcode && p.WarehouseId == CurrentWarehouseId);

                if (exists)
                {
                    return Error<ProductDto>($"Product with barcode '{request.Barcode}' already exists");
                }
            }

            var product = ProductMapper.FromRequest(request, CurrentWarehouseId);

            context.Products.Add(product);
            await context.SaveChangesAsync();

            await _auditService.LogAsync(
                "PRODUCT_CREATED_API",
                "Product",
                product.Id,
                new { Name = product.Name, Via = "REST API" },
                AuditSeverity.Info);

            _logger.LogInformation("API: Product created: {ProductId} by user {UserId}",
                product.Id, CurrentUserId);

            var createdProduct = await context.Products
                .Include(p => p.Category)
                .FirstAsync(p => p.Id == product.Id);

            var dto = createdProduct.ToDto();

            return CreatedAtAction(nameof(GetProduct), new { id = product.Id },
                ApiResponse<ProductDto>.SuccessResult(dto, "Product created successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error creating product");
            return Error<ProductDto>("Error creating product", 500);
        }
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ApiResponse<ProductDto>), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<ApiResponse<ProductDto>>> UpdateProduct(int id, [FromBody] UpdateProductRequest request)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var product = await context.Products
                .Include(p => p.Category)
                .FirstOrDefaultAsync(p => p.Id == id && p.WarehouseId == CurrentWarehouseId);

            if (product == null)
            {
                return NotFound<ProductDto>($"Product with ID {id} not found");
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Error<ProductDto>("Product name is required");
            }

            if (!string.IsNullOrWhiteSpace(request.Barcode) && request.Barcode != product.Barcode)
            {
                var exists = await context.Products
                    .AnyAsync(p => p.Barcode == request.Barcode && p.WarehouseId == CurrentWarehouseId && p.Id != id);

                if (exists)
                {
                    return Error<ProductDto>($"Product with barcode '{request.Barcode}' already exists");
                }
            }

            product.UpdateFrom(request);

            await context.SaveChangesAsync();

            await _auditService.LogAsync(
                "PRODUCT_UPDATED_API",
                "Product",
                product.Id,
                new { Name = product.Name, Via = "REST API" },
                AuditSeverity.Info);

            _logger.LogInformation("API: Product updated: {ProductId} by user {UserId}",
                product.Id, CurrentUserId);

            var dto = product.ToDto();

            return Success(dto, "Product updated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error updating product {ProductId}", id);
            return Error<ProductDto>("Error updating product", 500);
        }
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<ApiResponse<object>>> DeleteProduct(int id)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var product = await context.Products
                .FirstOrDefaultAsync(p => p.Id == id && p.WarehouseId == CurrentWarehouseId);

            if (product == null)
            {
                return NotFound<object>($"Product with ID {id} not found");
            }

            var productName = product.Name;
            context.Products.Remove(product);
            await context.SaveChangesAsync();

            await _auditService.LogAsync(
                "PRODUCT_DELETED_API",
                "Product",
                id,
                new { Name = productName, Via = "REST API" },
                AuditSeverity.Warning);

            _logger.LogInformation("API: Product deleted: {ProductId} by user {UserId}",
                id, CurrentUserId);

            return Success<object>(new { id, name = productName }, "Product deleted successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error deleting product {ProductId}", id);
            return Error<object>("Error deleting product", 500);
        }
    }

    /// <summary>
    /// Performs a stock movement.
    /// </summary>
    [HttpPost("{id}/stock")]
    [ProducesResponseType(typeof(ApiResponse<ProductDto>), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<ApiResponse<ProductDto>>> UpdateStock(int id, [FromBody] StockMovementRequest request)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var product = await context.Products
                .Include(p => p.Category)
                .FirstOrDefaultAsync(p => p.Id == id && p.WarehouseId == CurrentWarehouseId);

            if (product == null)
            {
                return NotFound<ProductDto>($"Product with ID {id} not found");
            }

            var oldQuantity = product.Quantity;
            product.Quantity += request.Quantity;
            product.UpdatedAt = DateTime.UtcNow;

            if (product.Quantity < 0)
            {
                return Error<ProductDto>("Insufficient stock");
            }

            var movement = new StockMovement
            {
                ProductId = product.Id,
                QuantityChange = request.Quantity,
                Type = Enum.TryParse<MovementType>(request.Type, out var movementType) ? movementType : MovementType.ManualAdd,
                Notes = request.Reason ?? "API Stock Update",
                Timestamp = DateTime.UtcNow,
                WarehouseId = CurrentWarehouseId
            };

            context.StockMovements.Add(movement);
            await context.SaveChangesAsync();

            await _auditService.LogAsync(
                "STOCK_MOVEMENT_API",
                "Product",
                product.Id,
                new
                {
                    Name = product.Name,
                    OldQuantity = oldQuantity,
                    NewQuantity = product.Quantity,
                    Change = request.Quantity,
                    Via = "REST API"
                },
                AuditSeverity.Info);

            _logger.LogInformation("API: Stock updated for product {ProductId}: {OldQty} -> {NewQty}",
                product.Id, oldQuantity, product.Quantity);

            var dto = product.ToDto();

            return Success(dto, "Stock updated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error updating stock for product {ProductId}", id);
            return Error<ProductDto>("Error updating stock", 500);
        }
    }
}
