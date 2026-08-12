using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.API.DTOs;

namespace LagersystemLVHome.API.Controllers;

/// <summary>
/// API controller for rooms (based on StorageLocations.Room).
/// </summary>
[ApiController]
[Route("api/rooms")]
public class RoomsApiController : BaseApiController
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly ILogger<RoomsApiController> _logger;

    public RoomsApiController(IDbContextFactory<InventoryDbContext> contextFactory, ILogger<RoomsApiController> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<RoomDto>>), 200)]
    public async Task<ActionResult<ApiResponse<List<RoomDto>>>> GetRooms()
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var warehouseId = CurrentWarehouseId;

            var rooms = await context.StorageLocations
                .Where(sl => sl.WarehouseId == warehouseId && !string.IsNullOrEmpty(sl.Room))
                .GroupBy(sl => sl.Room)
                .Select(g => new
                {
                    Room = g.Key!,
                    Locations = g.ToList()
                })
                .ToListAsync();

            var roomDtos = new List<RoomDto>();

            foreach (var room in rooms)
            {
                var productCount = await context.ProductStorageLocations
                    .Where(psl => room.Locations.Select(l => l.Id).Contains(psl.StorageLocationId))
                    .Select(psl => psl.ProductId)
                    .Distinct()
                    .CountAsync();

                var totalCapacity = room.Locations.Where(l => l.MaxCapacity.HasValue).Sum(l => l.MaxCapacity!.Value);
                var usedCapacity = await context.ProductStorageLocations
                    .Where(psl => room.Locations.Select(l => l.Id).Contains(psl.StorageLocationId))
                    .SumAsync(psl => psl.Quantity);

                roomDtos.Add(new RoomDto
                {
                    Name = room.Room,
                    StorageLocationCount = room.Locations.Count,
                    ProductCount = productCount,
                    TotalCapacity = totalCapacity,
                    UsedCapacity = usedCapacity,
                    UtilizationPercentage = totalCapacity > 0 ? (double)usedCapacity / totalCapacity * 100 : 0
                });
            }

            return Success(roomDtos.OrderBy(r => r.Name).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching rooms");
            return Error<List<RoomDto>>("Error fetching rooms", 500);
        }
    }

    [HttpGet("{name}/storage-locations")]
    public async Task<ActionResult<ApiResponse<List<StorageLocationDetailDto>>>> GetStorageLocations(string name)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var locations = await context.StorageLocations
                .Include(sl => sl.ProductStorageLocations).ThenInclude(psl => psl.Product).ThenInclude(p => p.Category)
                .Where(sl => sl.WarehouseId == CurrentWarehouseId && sl.Room == name)
                .ToListAsync();

            var dtos = locations.Select(sl => new StorageLocationDetailDto
            {
                Id = sl.Id,
                Code = sl.Code,
                Name = sl.Name,
                Room = sl.Room,
                MaxCapacity = sl.MaxCapacity,
                CurrentCapacity = sl.ProductStorageLocations.Sum(psl => psl.Quantity),
                UtilizationPercentage = sl.MaxCapacity.HasValue && sl.MaxCapacity.Value > 0
                    ? (double)sl.ProductStorageLocations.Sum(psl => psl.Quantity) / sl.MaxCapacity.Value * 100 : 0,
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
            }).ToList();

            return Success(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error fetching storage locations for room");
            return Error<List<StorageLocationDetailDto>>("Error fetching storage locations", 500);
        }
    }

    [HttpGet("{name}/products")]
    public async Task<ActionResult<ApiResponse<List<ProductInLocationDto>>>> GetProducts(string name)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var products = await context.ProductStorageLocations
                .Include(psl => psl.Product).ThenInclude(p => p.Category)
                .Include(psl => psl.StorageLocation)
                .Where(psl => psl.StorageLocation.WarehouseId == CurrentWarehouseId && psl.StorageLocation.Room == name)
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
            _logger.LogError(ex, "API: Error fetching products for room");
            return Error<List<ProductInLocationDto>>("Error fetching products", 500);
        }
    }
}
