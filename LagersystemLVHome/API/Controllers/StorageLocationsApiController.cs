using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.API.DTOs;
using LagersystemLVHome.Application.Services;

namespace LagersystemLVHome.API.Controllers;

/// <summary>
/// API controller for storage locations.
/// </summary>
[ApiController]
[Route("api/storage-locations")]
public class StorageLocationsApiController : BaseApiController
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly IAuditService _auditService;
    private readonly ILogger<StorageLocationsApiController> _logger;

    public StorageLocationsApiController(
        IDbContextFactory<InventoryDbContext> contextFactory,
        IAuditService auditService,
        ILogger<StorageLocationsApiController> logger)
    {
        _contextFactory = contextFactory;
        _auditService = auditService;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<StorageLocationDetailDto>>), 200)]
    public async Task<ActionResult<ApiResponse<List<StorageLocationDetailDto>>>> GetStorageLocations(
        [FromQuery] string? room = null,
        [FromQuery] bool activeOnly = true)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var query = context.StorageLocations
                .Include(sl => sl.ProductStorageLocations)
                .ThenInclude(psl => psl.Product)
                .ThenInclude(p => p.Category)
                .Where(sl => sl.WarehouseId == CurrentWarehouseId);

            if (activeOnly)
            {
                query = query.Where(sl => sl.IsActive);
            }

            if (!string.IsNullOrWhiteSpace(room))
            {
                query = query.Where(sl => sl.Room == room);
            }

            var locations = await query
                .OrderBy(sl => sl.Code)
                .ToListAsync();

            var dtos = locations.Select(sl =>
            {
                var currentCapacity = sl.ProductStorageLocations.Sum(psl => psl.Quantity);
                var utilization = sl.MaxCapacity.HasValue && sl.MaxCapacity.Value > 0
                    ? (double)currentCapacity / sl.MaxCapacity.Value * 100
                    : 0;

                return new StorageLocationDetailDto
                {
                    Id = sl.Id,
                    Code = sl.Code,
                    Name = sl.Name,
                    Room = sl.Room,
                    MaxCapacity = sl.MaxCapacity,
                    CurrentCapacity = currentCapacity,
                    UtilizationPercentage = Math.Round(utilization, 1),
                    IsActive = sl.IsActive,
                    WarehouseId = sl.WarehouseId,
                    Products = sl.ProductStorageLocations.Select(psl => new ProductInLocationDto
                    {
                        ProductId = psl.ProductId,
                        ProductName = psl.Product.Name,
                        Barcode = psl.Product.Barcode,
                        Quantity = psl.Quantity,
                        CategoryName = psl.Product.Category?.Name
                    }).ToList()
                };
            }).ToList();

            _logger.LogInformation("API: {Count} storage locations fetched", dtos.Count);
            return Success(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching storage locations");
            return Error<List<StorageLocationDetailDto>>("Error fetching storage locations", 500);
        }
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<StorageLocationDetailDto>), 200)]
    public async Task<ActionResult<ApiResponse<StorageLocationDetailDto>>> GetStorageLocation(int id)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var location = await context.StorageLocations
                .Include(sl => sl.ProductStorageLocations)
                .ThenInclude(psl => psl.Product)
                .ThenInclude(p => p.Category)
                .FirstOrDefaultAsync(sl => sl.Id == id && sl.WarehouseId == CurrentWarehouseId);

            if (location == null)
            {
                return NotFound<StorageLocationDetailDto>("Storage location not found");
            }

            var currentCapacity = location.ProductStorageLocations.Sum(psl => psl.Quantity);
            var utilization = location.MaxCapacity.HasValue && location.MaxCapacity.Value > 0
                ? (double)currentCapacity / location.MaxCapacity.Value * 100
                : 0;

            var dto = new StorageLocationDetailDto
            {
                Id = location.Id,
                Code = location.Code,
                Name = location.Name,
                Room = location.Room,
                MaxCapacity = location.MaxCapacity,
                CurrentCapacity = currentCapacity,
                UtilizationPercentage = Math.Round(utilization, 1),
                IsActive = location.IsActive,
                WarehouseId = location.WarehouseId,
                Products = location.ProductStorageLocations.Select(psl => new ProductInLocationDto
                {
                    ProductId = psl.ProductId,
                    ProductName = psl.Product.Name,
                    Barcode = psl.Product.Barcode,
                    Quantity = psl.Quantity,
                    CategoryName = psl.Product.Category?.Name
                }).ToList()
            };

            return Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching storage location {Id}", id);
            return Error<StorageLocationDetailDto>("Error fetching storage location", 500);
        }
    }

    [HttpGet("{id}/products")]
    [ProducesResponseType(typeof(ApiResponse<List<ProductInLocationDto>>), 200)]
    public async Task<ActionResult<ApiResponse<List<ProductInLocationDto>>>> GetProductsInLocation(int id)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var products = await context.ProductStorageLocations
                .Include(psl => psl.Product)
                .ThenInclude(p => p.Category)
                .Where(psl => psl.StorageLocationId == id && psl.StorageLocation.WarehouseId == CurrentWarehouseId)
                .Select(psl => new ProductInLocationDto
                {
                    ProductId = psl.ProductId,
                    ProductName = psl.Product.Name,
                    Barcode = psl.Product.Barcode,
                    Quantity = psl.Quantity,
                    CategoryName = psl.Product.Category != null ? psl.Product.Category.Name : null
                })
                .ToListAsync();

            return Success(products);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching products in location {Id}", id);
            return Error<List<ProductInLocationDto>>("Error fetching products in location", 500);
        }
    }

    [HttpGet("by-room")]
    [ProducesResponseType(typeof(ApiResponse<List<StorageLocationDetailDto>>), 200)]
    public async Task<ActionResult<ApiResponse<List<StorageLocationDetailDto>>>> GetLocationsByRoom(
        [FromQuery] string room)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(room))
            {
                return Error<List<StorageLocationDetailDto>>("Room parameter is required");
            }

            return await GetStorageLocations(room, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching locations by room");
            return Error<List<StorageLocationDetailDto>>("Error fetching locations by room", 500);
        }
    }

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<StorageLocationDetailDto>), 201)]
    public async Task<ActionResult<ApiResponse<StorageLocationDetailDto>>> CreateStorageLocation(
        [FromBody] CreateStorageLocationRequest request)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            if (string.IsNullOrWhiteSpace(request.Code))
            {
                return Error<StorageLocationDetailDto>("Storage location code is required");
            }

            var exists = await context.StorageLocations
                .AnyAsync(sl => sl.Code == request.Code && sl.WarehouseId == CurrentWarehouseId);

            if (exists)
            {
                return Error<StorageLocationDetailDto>($"Storage location with code '{request.Code}' already exists");
            }

            var location = new StorageLocation
            {
                Code = request.Code,
                Name = request.Name,
                Room = request.Room,
                MaxCapacity = request.MaxCapacity,
                IsActive = true,
                WarehouseId = CurrentWarehouseId
            };

            context.StorageLocations.Add(location);
            await context.SaveChangesAsync();

            await _auditService.LogAsync(
                "STORAGE_LOCATION_CREATED_API",
                "StorageLocation",
                location.Id,
                new { Code = location.Code, Via = "REST API" },
                AuditSeverity.Info);

            var dto = new StorageLocationDetailDto
            {
                Id = location.Id,
                Code = location.Code,
                Name = location.Name,
                Room = location.Room,
                MaxCapacity = location.MaxCapacity,
                CurrentCapacity = 0,
                UtilizationPercentage = 0,
                IsActive = location.IsActive,
                WarehouseId = location.WarehouseId,
                Products = new List<ProductInLocationDto>()
            };

            _logger.LogInformation("API: Storage location created: {Code}", location.Code);
            return CreatedAtAction(nameof(GetStorageLocation), new { id = location.Id },
                ApiResponse<StorageLocationDetailDto>.SuccessResult(dto, "Storage location created"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error creating storage location");
            return Error<StorageLocationDetailDto>("Error creating storage location", 500);
        }
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ApiResponse<StorageLocationDetailDto>), 200)]
    public async Task<ActionResult<ApiResponse<StorageLocationDetailDto>>> UpdateStorageLocation(
        int id,
        [FromBody] UpdateStorageLocationRequest request)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var location = await context.StorageLocations
                .Include(sl => sl.ProductStorageLocations)
                .ThenInclude(psl => psl.Product)
                .ThenInclude(p => p.Category)
                .FirstOrDefaultAsync(sl => sl.Id == id && sl.WarehouseId == CurrentWarehouseId);

            if (location == null)
            {
                return NotFound<StorageLocationDetailDto>("Storage location not found");
            }

            if (!string.IsNullOrWhiteSpace(request.Name))
                location.Name = request.Name;
            if (request.Room != null)
                location.Room = request.Room;
            if (request.MaxCapacity.HasValue)
                location.MaxCapacity = request.MaxCapacity;
            if (request.IsActive.HasValue)
                location.IsActive = request.IsActive.Value;

            await context.SaveChangesAsync();

            await _auditService.LogAsync(
                "STORAGE_LOCATION_UPDATED_API",
                "StorageLocation",
                location.Id,
                new { Code = location.Code, Via = "REST API" },
                AuditSeverity.Info);

            var currentCapacity = location.ProductStorageLocations.Sum(psl => psl.Quantity);
            var utilization = location.MaxCapacity.HasValue && location.MaxCapacity.Value > 0
                ? (double)currentCapacity / location.MaxCapacity.Value * 100
                : 0;

            var dto = new StorageLocationDetailDto
            {
                Id = location.Id,
                Code = location.Code,
                Name = location.Name,
                Room = location.Room,
                MaxCapacity = location.MaxCapacity,
                CurrentCapacity = currentCapacity,
                UtilizationPercentage = Math.Round(utilization, 1),
                IsActive = location.IsActive,
                WarehouseId = location.WarehouseId,
                Products = location.ProductStorageLocations.Select(psl => new ProductInLocationDto
                {
                    ProductId = psl.ProductId,
                    ProductName = psl.Product.Name,
                    Barcode = psl.Product.Barcode,
                    Quantity = psl.Quantity,
                    CategoryName = psl.Product.Category?.Name
                }).ToList()
            };

            _logger.LogInformation("API: Storage location updated: {Id}", id);
            return Success(dto, "Storage location updated");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error updating storage location {Id}", id);
            return Error<StorageLocationDetailDto>("Error updating storage location", 500);
        }
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    public async Task<ActionResult<ApiResponse<object>>> DeleteStorageLocation(int id)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var location = await context.StorageLocations
                .Include(sl => sl.ProductStorageLocations)
                .FirstOrDefaultAsync(sl => sl.Id == id && sl.WarehouseId == CurrentWarehouseId);

            if (location == null)
            {
                return NotFound<object>("Storage location not found");
            }

            if (location.ProductStorageLocations.Any())
            {
                return Error<object>("Cannot delete storage location with products. Please remove products first.");
            }

            var code = location.Code;
            context.StorageLocations.Remove(location);
            await context.SaveChangesAsync();

            await _auditService.LogAsync(
                "STORAGE_LOCATION_DELETED_API",
                "StorageLocation",
                id,
                new { Code = code, Via = "REST API" },
                AuditSeverity.Warning);

            _logger.LogInformation("API: Storage location deleted: {Id}", id);
            return Success<object>(new { id, code }, "Storage location deleted");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error deleting storage location {Id}", id);
            return Error<object>("Error deleting storage location", 500);
        }
    }
}
