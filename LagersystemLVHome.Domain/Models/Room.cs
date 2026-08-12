using System.ComponentModel.DataAnnotations;

namespace LagersystemLVHome.Domain.Models;

/// <summary>
/// Room/area within a warehouse.
/// </summary>
public class Room
{
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty; // e.g. "H-A" (Hall A)

    [MaxLength(1000)]
    public string? Description { get; set; }

    public RoomType Type { get; set; } = RoomType.StorageRoom;

    public int? Floor { get; set; } // Floor/storey

    public decimal? Area { get; set; } // Area in m²

    public int? Capacity { get; set; } // Maximum capacity in units

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Foreign Key
    public int WarehouseId { get; set; }

    // Navigation
    public virtual Warehouse? Warehouse { get; set; }
}

public enum RoomType
{
    StorageRoom = 0,    // Storage room
    Warehouse = 1,      // Warehouse/hall
    Workshop = 2,       // Workshop
    Office = 3,         // Office
    ColdStorage = 4,    // Cold storage
    Archive = 5,        // Archive
    LoadingBay = 6      // Loading bay
}
