namespace LagersystemLVHome.Domain.Models;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "📦"; // Emoji or icon class
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    // Multi-tenancy
    public int WarehouseId { get; set; }

    public virtual Warehouse? Warehouse { get; set; }
    public virtual ICollection<Product> Products { get; set; } = [];
}
