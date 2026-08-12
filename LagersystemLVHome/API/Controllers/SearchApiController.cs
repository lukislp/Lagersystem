using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;
using LagersystemLVHome.API.DTOs;

namespace LagersystemLVHome.API.Controllers;

/// <summary>
/// API controller for global search.
/// </summary>
[ApiController]
[Route("api/search")]
public class SearchApiController : BaseApiController
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly ILogger<SearchApiController> _logger;

    public SearchApiController(IDbContextFactory<InventoryDbContext> contextFactory, ILogger<SearchApiController> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <summary>
    /// Global search across all entities.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<GlobalSearchResultDto>), 200)]
    public async Task<ActionResult<ApiResponse<GlobalSearchResultDto>>> GlobalSearch(
        [FromQuery] string q,
        [FromQuery] int limit = 10)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            {
                return Error<GlobalSearchResultDto>("Query must be at least 2 characters");
            }

            var query = q.ToLower();
            var warehouseId = CurrentWarehouseId;

            var products = await context.Products
                .Where(p => p.WarehouseId == warehouseId &&
                    (p.Name.ToLower().Contains(query) ||
                    (p.Barcode != null && p.Barcode.ToLower().Contains(query)) ||
                    (p.Description != null && p.Description.ToLower().Contains(query))))
                .Take(limit)
                .Select(p => new SearchResultDto
                {
                    Type = "Product",
                    Id = p.Id,
                    Name = p.Name,
                    Description = p.Description,
                    Icon = "mdi:package-variant",
                    AdditionalInfo = $"Barcode: {p.Barcode}, Qty: {p.Quantity}",
                    RelevanceScore = 1.0
                })
                .ToListAsync();

            var categories = await context.Categories
                .Where(c => c.WarehouseId == warehouseId &&
                    c.Name.ToLower().Contains(query))
                .Take(limit)
                .Select(c => new SearchResultDto
                {
                    Type = "Category",
                    Id = c.Id,
                    Name = c.Name,
                    Description = c.Description,
                    Icon = c.Icon,
                    AdditionalInfo = $"{c.Products.Count} products",
                    RelevanceScore = 0.9
                })
                .ToListAsync();

            var storageLocations = await context.StorageLocations
                .Where(sl => sl.WarehouseId == warehouseId &&
                    (sl.Code.ToLower().Contains(query) ||
                    sl.Name.ToLower().Contains(query) ||
                    (sl.Room != null && sl.Room.ToLower().Contains(query))))
                .Take(limit)
                .Select(sl => new SearchResultDto
                {
                    Type = "StorageLocation",
                    Id = sl.Id,
                    Name = sl.Name,
                    Description = $"Code: {sl.Code}",
                    Icon = "mdi:map-marker",
                    AdditionalInfo = sl.Room,
                    RelevanceScore = 0.8
                })
                .ToListAsync();

            var result = new GlobalSearchResultDto
            {
                Products = products,
                Categories = categories,
                StorageLocations = storageLocations,
                TotalResults = products.Count + categories.Count + storageLocations.Count,
                Query = q
            };

            return Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API: Error in global search");
            return Error<GlobalSearchResultDto>("Error performing search", 500);
        }
    }

    /// <summary>
    /// Searches products by name or barcode.
    /// </summary>
    [HttpGet("products")]
    public async Task<ActionResult<ApiResponse<List<SearchResultDto>>>> SearchProducts(
        [FromQuery] string q,
        [FromQuery] int limit = 20)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var query = q.ToLower();
        var results = await context.Products
            .Where(p => p.WarehouseId == CurrentWarehouseId &&
                (p.Name.ToLower().Contains(query) || (p.Barcode != null && p.Barcode.Contains(query))))
            .Take(limit)
            .Select(p => new SearchResultDto { Type = "Product", Id = p.Id, Name = p.Name, AdditionalInfo = p.Barcode })
            .ToListAsync();
        return Success(results);
    }

    /// <summary>
    /// Searches categories by name.
    /// </summary>
    [HttpGet("categories")]
    public async Task<ActionResult<ApiResponse<List<SearchResultDto>>>> SearchCategories(
        [FromQuery] string q,
        [FromQuery] int limit = 20)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var query = q.ToLower();
        var results = await context.Categories
            .Where(c => c.WarehouseId == CurrentWarehouseId && c.Name.ToLower().Contains(query))
            .Take(limit)
            .Select(c => new SearchResultDto { Type = "Category", Id = c.Id, Name = c.Name, Icon = c.Icon })
            .ToListAsync();
        return Success(results);
    }
}
