using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.API.DTOs;

namespace LagersystemLVHome.API.Controllers;

/// <summary>
/// API controller for warehouses (read-only).
/// </summary>
[ApiController]
[Route("api/warehouses")]
public class WarehousesApiController : BaseApiController
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly ILogger<WarehousesApiController> _logger;

    public WarehousesApiController(IDbContextFactory<InventoryDbContext> contextFactory, ILogger<WarehousesApiController> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <summary>
    /// Gets all warehouses (SuperAdmin sees all, regular users only their own).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<WarehouseDto>>), 200)]
    public async Task<ActionResult<ApiResponse<List<WarehouseDto>>>> GetWarehouses()
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var warehouses = await context.Warehouses
                .Where(w => w.Id == CurrentWarehouseId || CurrentUserRole == UserRole.SuperAdmin)
                .Select(w => new WarehouseDto
                {
                    Id = w.Id,
                    Name = w.Name,
                    Code = w.Code,
                    Address = w.Address,
                    IsActive = w.IsActive,
                    CreatedAt = w.CreatedAt
                })
                .ToListAsync();

            return Success(warehouses);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching warehouses");
            return Error<List<WarehouseDto>>("Error fetching warehouses", 500);
        }
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<WarehouseDto>), 200)]
    public async Task<ActionResult<ApiResponse<WarehouseDto>>> GetWarehouse(int id)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            // Only own warehouse or SuperAdmin
            if (id != CurrentWarehouseId && CurrentUserRole != UserRole.SuperAdmin)
            {
                return Forbid();
            }

            var warehouse = await context.Warehouses
                .FirstOrDefaultAsync(w => w.Id == id);

            if (warehouse == null)
            {
                return NotFound<WarehouseDto>("Warehouse not found");
            }

            var dto = new WarehouseDto
            {
                Id = warehouse.Id,
                Name = warehouse.Name,
                Code = warehouse.Code,
                Address = warehouse.Address,
                IsActive = warehouse.IsActive,
                CreatedAt = warehouse.CreatedAt
            };

            return Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching warehouse");
            return Error<WarehouseDto>("Error fetching warehouse", 500);
        }
    }

    [HttpGet("{id}/stats")]
    [ProducesResponseType(typeof(ApiResponse<WarehouseStatsDto>), 200)]
    public async Task<ActionResult<ApiResponse<WarehouseStatsDto>>> GetWarehouseStats(int id)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            // Only own warehouse or SuperAdmin
            if (id != CurrentWarehouseId && CurrentUserRole != UserRole.SuperAdmin)
            {
                return Forbid();
            }

            var warehouse = await context.Warehouses.FirstOrDefaultAsync(w => w.Id == id);
            if (warehouse == null) return NotFound<WarehouseStatsDto>("Warehouse not found");

            var totalProducts = await context.Products.CountAsync(p => p.WarehouseId == id);
            var totalCategories = await context.Categories.CountAsync(c => c.WarehouseId == id && c.IsActive);
            var totalStorageLocations = await context.StorageLocations.CountAsync(sl => sl.WarehouseId == id);
            var totalUsers = await context.Users.CountAsync(u => u.WarehouseId == id);

            var products = await context.Products.Where(p => p.WarehouseId == id).Select(p => new { p.Quantity, p.Price }).ToListAsync();
            var totalValue = products.Sum(p => p.Quantity * p.Price);

            var lowStockCount = await context.Products.CountAsync(p => p.WarehouseId == id && p.Quantity <= p.MinQuantity);

            var stats = new WarehouseStatsDto
            {
                Id = warehouse.Id,
                Name = warehouse.Name,
                TotalProducts = totalProducts,
                TotalCategories = totalCategories,
                TotalStorageLocations = totalStorageLocations,
                TotalUsers = totalUsers,
                TotalInventoryValue = totalValue,
                LowStockCount = lowStockCount,
                LastUpdated = DateTime.UtcNow
            };

            return Success(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching warehouse stats");
            return Error<WarehouseStatsDto>("Error fetching warehouse stats", 500);
        }
    }
}
