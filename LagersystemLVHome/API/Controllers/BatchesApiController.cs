using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.API.DTOs;

namespace LagersystemLVHome.API.Controllers;

[ApiController]
[Route("api/batches")]
public class BatchesApiController : BaseApiController
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly ILogger<BatchesApiController> _logger;

    public BatchesApiController(IDbContextFactory<InventoryDbContext> contextFactory, ILogger<BatchesApiController> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    [HttpGet("expiring")]
    public async Task<ActionResult<ApiResponse<List<BatchDto>>>> GetExpiringBatches([FromQuery] int days = 7, [FromQuery] int limit = 50)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var now = DateTime.UtcNow;
        var threshold = now.AddDays(days);
        var batches = await context.ProductBatches
            .Include(b => b.Product)
            .Where(b => b.Product.WarehouseId == CurrentWarehouseId && b.ExpiryDate.HasValue && b.ExpiryDate.Value > now && b.ExpiryDate.Value <= threshold)
            .OrderBy(b => b.ExpiryDate)
            .Take(limit)
            .Select(b => new BatchDto
            {
                Id = b.Id,
                ProductId = b.ProductId,
                ProductName = b.Product.Name,
                BatchNumber = b.BatchNumber,
                ExpiryDate = b.ExpiryDate,
                Quantity = b.Quantity,
                CreatedAt = b.CreatedAt,
                DaysUntilExpiry = b.ExpiryDate.HasValue ? (int)(b.ExpiryDate.Value - now).TotalDays : null,
                Status = "Expiring"
            })
            .ToListAsync();
        return Success(batches);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<BatchDto>>> GetBatch(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var batch = await context.ProductBatches.Include(b => b.Product)
            .FirstOrDefaultAsync(b => b.Id == id && b.Product.WarehouseId == CurrentWarehouseId);
        if (batch == null) return NotFound<BatchDto>("Batch not found");

        var now = DateTime.UtcNow;
        var dto = new BatchDto
        {
            Id = batch.Id,
            ProductId = batch.ProductId,
            ProductName = batch.Product.Name,
            BatchNumber = batch.BatchNumber,
            ExpiryDate = batch.ExpiryDate,
            Quantity = batch.Quantity,
            CreatedAt = batch.CreatedAt,
            DaysUntilExpiry = batch.ExpiryDate.HasValue ? (int)(batch.ExpiryDate.Value - now).TotalDays : null,
            Status = batch.ExpiryDate.HasValue ? (batch.ExpiryDate.Value <= now ? "Expired" : "Fresh") : "Fresh"
        };
        return Success(dto);
    }

    [HttpGet("by-product/{productId}")]
    public async Task<ActionResult<ApiResponse<List<BatchDto>>>> GetBatchesByProduct(int productId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var now = DateTime.UtcNow;
        var batches = await context.ProductBatches.Include(b => b.Product)
            .Where(b => b.ProductId == productId && b.Product.WarehouseId == CurrentWarehouseId)
            .OrderBy(b => b.ExpiryDate)
            .Select(b => new BatchDto
            {
                Id = b.Id,
                ProductId = b.ProductId,
                ProductName = b.Product.Name,
                BatchNumber = b.BatchNumber,
                ExpiryDate = b.ExpiryDate,
                Quantity = b.Quantity,
                CreatedAt = b.CreatedAt,
                DaysUntilExpiry = b.ExpiryDate.HasValue ? (int)(b.ExpiryDate.Value - now).TotalDays : null,
                Status = b.ExpiryDate.HasValue ? (b.ExpiryDate.Value <= now ? "Expired" : "Fresh") : "Fresh"
            })
            .ToListAsync();
        return Success(batches);
    }
}
