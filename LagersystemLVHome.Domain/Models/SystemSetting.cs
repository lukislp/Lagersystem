using System.ComponentModel.DataAnnotations;

namespace LagersystemLVHome.Domain.Models;

/// <summary>
/// System-wide settings (key-value store).
/// </summary>
public class SystemSetting
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    [Required]
    public string Value { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
