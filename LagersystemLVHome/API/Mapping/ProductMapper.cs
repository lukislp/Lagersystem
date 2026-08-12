using LagersystemLVHome.Domain.Models;
using LagersystemLVHome.API.DTOs;

namespace LagersystemLVHome.API.Mapping;

/// <summary>
/// Mapper between domain models and API DTOs.
/// Provides compatibility between old API schema and new data model.
/// </summary>
public static class ProductMapper
{
    public static ProductDto ToDto(this Product product)
    {
        return new ProductDto
        {
            Id = product.Id,
            Name = product.Name,
            Barcode = product.Barcode,
            Description = product.Description,
            Quantity = product.Quantity,
            MinStock = product.MinQuantity,
            PurchasePrice = (double?)product.Price,
            SalePrice = (double?)(product.Price * 1.3m),
            ImageUrl = product.ImageUrl,
            CreatedAt = product.CreatedAt,
            UpdatedAt = product.UpdatedAt,
            CategoryId = product.CategoryId,
            CategoryName = product.Category?.Name,
            CategoryIcon = product.Category?.Icon
        };
    }

    public static void UpdateFrom(this Product product, CreateProductRequest request)
    {
        product.Name = request.Name;
        product.Barcode = request.Barcode ?? string.Empty;
        product.Description = request.Description ?? string.Empty;
        product.Quantity = request.Quantity;
        product.MinQuantity = request.MinStock;

        // Use PurchasePrice as base, or SalePrice / 1.3 if only SalePrice is provided
        if (request.PurchasePrice.HasValue)
        {
            product.Price = (decimal)request.PurchasePrice.Value;
        }
        else if (request.SalePrice.HasValue)
        {
            product.Price = (decimal)(request.SalePrice.Value / 1.3);
        }

        product.CategoryId = request.CategoryId ?? 0;
        product.UpdatedAt = DateTime.UtcNow;
    }

    public static Product FromRequest(CreateProductRequest request, int warehouseId)
    {
        var product = new Product
        {
            Name = request.Name,
            Barcode = request.Barcode ?? string.Empty,
            Description = request.Description ?? string.Empty,
            Quantity = request.Quantity,
            MinQuantity = request.MinStock,
            Price = request.PurchasePrice.HasValue
                ? (decimal)request.PurchasePrice.Value
                : (request.SalePrice.HasValue ? (decimal)(request.SalePrice.Value / 1.3) : 0m),
            CategoryId = request.CategoryId ?? 0,
            WarehouseId = warehouseId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        return product;
    }
}
