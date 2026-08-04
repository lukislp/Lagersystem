namespace LagersystemLVHome.API.DTOs;

/// <summary>
/// Category DTO for API responses.
/// </summary>
public class CategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "bi-tag";
    public string? Description { get; set; }
    public int ProductCount { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// Create/update category request.
/// </summary>
public class CreateCategoryRequest
{
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "bi-tag";
    public string? Description { get; set; }
}

public class UpdateCategoryRequest : CreateCategoryRequest
{
    public int Id { get; set; }
}
