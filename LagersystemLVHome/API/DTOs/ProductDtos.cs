namespace LagersystemLVHome.API.DTOs;

/// <summary>
/// Product DTO for API responses.
/// </summary>
public class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string? Description { get; set; }
    public int Quantity { get; set; }
    public int MinStock { get; set; }
    public double? PurchasePrice { get; set; }
    public double? SalePrice { get; set; }
    public string? ImageUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Category info
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? CategoryIcon { get; set; }

    // Storage locations
    public List<StorageLocationDto>? StorageLocations { get; set; }

    // Batch info (if best-before date tracking is enabled)
    public List<ProductBatchDto>? Batches { get; set; }
}

/// <summary>
/// Create/update product request.
/// </summary>
public class CreateProductRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string? Description { get; set; }
    public int Quantity { get; set; }
    public int MinStock { get; set; } = 10;
    public double? PurchasePrice { get; set; }
    public double? SalePrice { get; set; }
    public int? CategoryId { get; set; }
}

public class UpdateProductRequest : CreateProductRequest
{
    public int Id { get; set; }
}

/// <summary>
/// Storage location DTO.
/// </summary>
public class StorageLocationDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Name { get; set; }
    public int? RoomId { get; set; }
    public string? RoomName { get; set; }
    public int Quantity { get; set; }
}

/// <summary>
/// Product batch DTO (best-before date system).
/// </summary>
public class ProductBatchDto
{
    public int Id { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ExpiryDate { get; set; }
    public int Quantity { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Stock movement request.
/// </summary>
public class StockMovementRequest
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public string Type { get; set; } = "MANUAL"; // IN, OUT, MANUAL, CORRECTION
    public string? Reason { get; set; }
    public int? StorageLocationId { get; set; }
    public int? BatchId { get; set; }
}
