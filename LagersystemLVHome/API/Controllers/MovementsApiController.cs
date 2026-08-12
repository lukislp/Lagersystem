using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.API.DTOs;

namespace LagersystemLVHome.API.Controllers;

/// <summary>
/// API controller for stock movements.
/// </summary>
[Route("api/movements")]
public class MovementsApiController : BaseApiController
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly ILogger<MovementsApiController> _logger;

    public MovementsApiController(
        IDbContextFactory<InventoryDbContext> contextFactory,
        ILogger<MovementsApiController> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <summary>
    /// Returns all stock movements (paginated) with optional filters.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<MovementDto>>> GetMovements(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] int? productId = null,
        [FromQuery] string? type = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var warehouseId = CurrentWarehouseId;

            var query = context.StockMovements
                .Include(m => m.Product)
                .Where(m => m.WarehouseId == warehouseId)
                .AsQueryable();

            if (productId.HasValue)
                query = query.Where(m => m.ProductId == productId.Value);

            if (!string.IsNullOrEmpty(type) && Enum.TryParse<MovementType>(type, true, out var movementType))
                query = query.Where(m => m.Type == movementType);

            if (from.HasValue)
                query = query.Where(m => m.Timestamp >= from.Value);

            if (to.HasValue)
                query = query.Where(m => m.Timestamp <= to.Value);

            var totalCount = await query.CountAsync();

            var movements = await query
                .OrderByDescending(m => m.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(m => new MovementDto
                {
                    Id = m.Id,
                    ProductId = m.ProductId,
                    ProductName = m.Product != null ? m.Product.Name : "Unknown",
                    Barcode = m.Product != null ? m.Product.Barcode : null,
                    QuantityChange = m.QuantityChange,
                    Type = m.Type.ToString(),
                    Notes = m.Notes,
                    Timestamp = m.Timestamp,
                    WarehouseId = m.WarehouseId
                })
                .ToListAsync();

            return Paginated(movements, page, pageSize, totalCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading movements");
            return StatusCode(500, new PaginatedResponse<MovementDto>
            {
                Success = false,
                Errors = new List<string> { "Error loading movements" }
            });
        }
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ApiResponse<MovementDto>>> GetMovement(int id)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var warehouseId = CurrentWarehouseId;

            var movement = await context.StockMovements
                .Include(m => m.Product)
                .Where(m => m.Id == id && m.WarehouseId == warehouseId)
                .Select(m => new MovementDto
                {
                    Id = m.Id,
                    ProductId = m.ProductId,
                    ProductName = m.Product != null ? m.Product.Name : "Unknown",
                    Barcode = m.Product != null ? m.Product.Barcode : null,
                    QuantityChange = m.QuantityChange,
                    Type = m.Type.ToString(),
                    Notes = m.Notes,
                    Timestamp = m.Timestamp,
                    WarehouseId = m.WarehouseId
                })
                .FirstOrDefaultAsync();

            if (movement == null)
                return NotFound<MovementDto>("Movement not found");

            return Success(movement);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading movement {MovementId}", id);
            return Error<MovementDto>("Error loading movement", 500);
        }
    }

    [HttpGet("product/{productId:int}")]
    public async Task<ActionResult<ApiResponse<List<MovementDto>>>> GetProductHistory(int productId)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var warehouseId = CurrentWarehouseId;

            var movements = await context.StockMovements
                .Include(m => m.Product)
                .Where(m => m.ProductId == productId && m.WarehouseId == warehouseId)
                .OrderByDescending(m => m.Timestamp)
                .Select(m => new MovementDto
                {
                    Id = m.Id,
                    ProductId = m.ProductId,
                    ProductName = m.Product != null ? m.Product.Name : "Unknown",
                    Barcode = m.Product != null ? m.Product.Barcode : null,
                    QuantityChange = m.QuantityChange,
                    Type = m.Type.ToString(),
                    Notes = m.Notes,
                    Timestamp = m.Timestamp,
                    WarehouseId = m.WarehouseId
                })
                .ToListAsync();

            return Success(movements);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading product history for product {ProductId}", productId);
            return Error<List<MovementDto>>("Error loading product history", 500);
        }
    }

    [HttpGet("recent")]
    public async Task<ActionResult<ApiResponse<List<MovementDto>>>> GetRecentMovements(
        [FromQuery] int limit = 10)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var warehouseId = CurrentWarehouseId;

            var movements = await context.StockMovements
                .Include(m => m.Product)
                .Where(m => m.WarehouseId == warehouseId)
                .OrderByDescending(m => m.Timestamp)
                .Take(limit)
                .Select(m => new MovementDto
                {
                    Id = m.Id,
                    ProductId = m.ProductId,
                    ProductName = m.Product != null ? m.Product.Name : "Unknown",
                    Barcode = m.Product != null ? m.Product.Barcode : null,
                    QuantityChange = m.QuantityChange,
                    Type = m.Type.ToString(),
                    Notes = m.Notes,
                    Timestamp = m.Timestamp,
                    WarehouseId = m.WarehouseId
                })
                .ToListAsync();

            return Success(movements);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading recent movements");
            return Error<List<MovementDto>>("Error loading recent movements", 500);
        }
    }

    [HttpGet("by-type/{type}")]
    public async Task<ActionResult<ApiResponse<List<MovementDto>>>> GetMovementsByType(
        string type,
        [FromQuery] int limit = 50)
    {
        try
        {
            if (!Enum.TryParse<MovementType>(type, true, out var movementType))
                return Error<List<MovementDto>>("Invalid movement type");

            await using var context = await _contextFactory.CreateDbContextAsync();
            var warehouseId = CurrentWarehouseId;

            var movements = await context.StockMovements
                .Include(m => m.Product)
                .Where(m => m.WarehouseId == warehouseId && m.Type == movementType)
                .OrderByDescending(m => m.Timestamp)
                .Take(limit)
                .Select(m => new MovementDto
                {
                    Id = m.Id,
                    ProductId = m.ProductId,
                    ProductName = m.Product != null ? m.Product.Name : "Unknown",
                    Barcode = m.Product != null ? m.Product.Barcode : null,
                    QuantityChange = m.QuantityChange,
                    Type = m.Type.ToString(),
                    Notes = m.Notes,
                    Timestamp = m.Timestamp,
                    WarehouseId = m.WarehouseId
                })
                .ToListAsync();

            return Success(movements);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading movements by type {Type}", type);
            return Error<List<MovementDto>>("Error loading movements", 500);
        }
    }
}
