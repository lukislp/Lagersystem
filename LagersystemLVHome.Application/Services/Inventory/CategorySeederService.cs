using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Service for initializing default categories.
/// Runs on first application startup.
/// </summary>
public class CategorySeederService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CategorySeederService> _logger;

    public CategorySeederService(
        IServiceProvider serviceProvider,
        ILogger<CategorySeederService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Initializes default categories for a warehouse.
    /// The warehouse must already exist; no automatic creation is performed.
    /// </summary>
    /// <remarks>
    /// Marked <c>virtual</c> to allow substitution in unit tests that exercise
    /// <see cref="WarehouseService"/> and <see cref="SetupService"/> without a
    /// fully wired <see cref="IServiceProvider"/>.
    /// </remarks>
    public virtual async Task SeedCategoriesAsync(int? warehouseId = null, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        try
        {
            Warehouse? warehouse;

            if (warehouseId.HasValue)
            {
                warehouse = await context.Warehouses.FindAsync(warehouseId.Value);
                if (warehouse == null)
                {
                    _logger.LogError("Warehouse with ID {WarehouseId} not found - categories cannot be created!", warehouseId.Value);
                    return;
                }
            }
            else
            {
                // Get the most recently created warehouse (for Setup.razor)
                warehouse = await context.Warehouses
                    .OrderByDescending(w => w.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);

                if (warehouse == null)
                {
                    _logger.LogError("No warehouse found and warehouseId was not supplied - categories cannot be created.");
                    _logger.LogWarning("Bitte erst ein Warehouse erstellen (Setup.razor, AdminWarehouses.razor oder Register.razor)");
                    return;
                }
            }

            // Check if categories already exist for this warehouse
            var existingCategoriesCount = await context.Categories
                .Where(c => c.WarehouseId == warehouse.Id)
                .CountAsync(cancellationToken);

            if (existingCategoriesCount >= 10)
            {
                _logger.LogInformation("Warehouse {WarehouseId} ({WarehouseName}) already has {Count} categories. Seeding skipped.",
                    warehouse.Id, warehouse.Name, existingCategoriesCount);
                return;
            }

            if (existingCategoriesCount > 0 && existingCategoriesCount < 10)
            {
                _logger.LogWarning("Warehouse {WarehouseId} has only {Count} categories (expected: 33). Creating missing categories...",
                    warehouse.Id, existingCategoriesCount);
            }

            _logger.LogInformation("Seeding {Count} standard categories for Warehouse {WarehouseId} ({WarehouseName})...",
                33, warehouse.Id, warehouse.Name);

            var categories = new List<Category>
            {
                // Electronics and technology (5)
                new() { Name = "Elektronik", Icon = "bi-laptop", Description = "Computer, Smartphones, Smart Home, Kameras, Audio & Video", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "Batterien", Icon = "bi-battery-charging", Description = "Batterien, Akkus, Ladeger\u00e4te, Knopfzellen, Energiezellen", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "Computer & Software", Icon = "bi-pc-display", Description = "PC-Komponenten, Grafikkarten, Prozessoren, Software, Gaming", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "Foto & Video", Icon = "bi-camera", Description = "Kameras, Objektive, Stative, Studioleuchten, Videokameras", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "Musikinstrumente", Icon = "bi-music-note", Description = "Gitarren, Klaviere, Schlagzeug, DJ-Equipment, Verst\u00e4rker", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },

                // Food and beverages (3)
                new() { Name = "Lebensmittel", Icon = "bi-basket", Description = "S\u00fc\u00dfigkeiten, Chips, Getr\u00e4nke, Kaffee, Konserven", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "Wein & Spirituosen", Icon = "bi-cup-straw", Description = "Wein, Whisky, Gin, Rum, Champagner, Bier, Cocktails", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "Kaffee & Tee", Icon = "bi-cup-hot", Description = "Kaffeebohnen, Kapseln, Teesorten, Kakao, Hei\u00dfe Getr\u00e4nke", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },

                // Household and living (6)
                new() { Name = "Haushalt", Icon = "bi-house", Description = "Geschirr, Besteck, T\u00f6pfe, Pfannen, K\u00fcchenhelfer, Reinigung", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "M\u00f6bel", Icon = "bi-door-open", Description = "Tische, St\u00fchle, Sofas, Betten, Schr\u00e4nke, Regale", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "Textilien", Icon = "bi-bag", Description = "Bettw\u00e4sche, Handt\u00fccher, Decken, Gardinen, Teppiche", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "Drogerie", Icon = "bi-droplet", Description = "Shampoo, Duschgel, Reinigungsmittel, Waschmittel, Kosmetik", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "Baumarkt", Icon = "bi-hammer", Description = "Farben, Tapeten, Werkzeug, Schrauben, D\u00fcbel, Sanit\u00e4r", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "Garten", Icon = "bi-flower1", Description = "Gartenger\u00e4te, Pflanzen, Rasenm\u00e4her, Grill, Gartenm\u00f6bel", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },

                // Clothing and accessories (2)
                new() { Name = "Kleidung", Icon = "bi-handbag", Description = "T-Shirts, Hosen, Jacken, Kleider, Schuhe, Sportbekleidung", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "Schmuck & Uhren", Icon = "bi-watch", Description = "Armbanduhren, Ketten, Ringe, Ohrringe, Armreifen", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },

                // Sports and leisure (4)
                new() { Name = "Sport & Fitness", Icon = "bi-heart-pulse", Description = "Fitnessger\u00e4te, Sportbekleidung, B\u00e4lle, Hanteln, Yoga", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "Camping & Outdoor", Icon = "bi-tree", Description = "Zelte, Schlafsa\u0308cke, Rucks\u00e4cke, Camping-Kocher, Outdoor", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "Spielzeug", Icon = "bi-gift", Description = "LEGO, Playmobil, Puppen, Brettspiele, Actionfiguren", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "Modellbau & Hobby", Icon = "bi-palette", Description = "Modellbau, RC-Modelle, Farben, Basteln, Eisenbahn", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },

                // Office and work (2)
                new() { Name = "B\u00fcrobedarf", Icon = "bi-pencil", Description = "Stifte, Papier, Ordner, Hefter, Post-its, Schreibwaren", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "Werkzeug", Icon = "bi-tools", Description = "Bohrmaschinen, Schraubendreher, S\u00e4gen, Zangen, Hammer", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },

                // Automotive and mobility (2)
                new() { Name = "Automotive", Icon = "bi-car-front", Description = "Autoteile, Motor\u00f6l, Reifen, Autopflege, Zubeh\u00f6r", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "Fahrr\u00e4der & E-Bikes", Icon = "bi-bicycle", Description = "Fahrr\u00e4der, E-Bikes, Mountainbikes, Zubeh\u00f6r, Ersatzteile", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },

                // Family and children (4)
                new() { Name = "Baby & Kind", Icon = "bi-heart", Description = "Windeln, Babynahrung, Kinderwagen, Babykleidung, Spielzeug", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "Haustiere", Icon = "bi-heart-pulse", Description = "Hundefutter, Katzenfutter, Spielzeug, Leinen, Aquarium", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "Tiermedizin & Pflege", Icon = "bi-capsule", Description = "Flohmittel, Wurmkuren, Shampoo, Nahrungserg\u00e4nzung", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "Party & Fest", Icon = "bi-balloon", Description = "Luftballons, Dekoration, Geschirr, Kost\u00fcme, Kerzen", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },

                // Media and entertainment (2)
                new() { Name = "B\u00fccher & Medien", Icon = "bi-book", Description = "B\u00fccher, DVDs, Blu-rays, CDs, Videospiele, H\u00f6rb\u00fccher", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "Gesundheit", Icon = "bi-bandaid", Description = "Vitamine, Medikamente, Pflaster, Thermometer, Pflege", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },

                // Miscellaneous (3)
                new() { Name = "Nahrungserg\u00e4nzung", Icon = "bi-capsule", Description = "Vitamine, Proteinpulver, Omega-3, Supplements, Fitness", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "Kunst & Basteln", Icon = "bi-brush", Description = "Farben, Pinsel, Leinw\u00e4nde, Bastelmaterial, Scrapbooking", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow },
                new() { Name = "Beleuchtung", Icon = "bi-lightbulb", Description = "LED-Lampen, Smart Lights, Leuchtmittel, Deckenlampen", WarehouseId = warehouse.Id, IsActive = true, CreatedAt = DateTime.UtcNow }
            };

            // Only insert categories that do not already exist
            var addedCount = 0;
            foreach (var category in categories)
            {
                var exists = await context.Categories
                    .AnyAsync(c => c.Name == category.Name && c.WarehouseId == warehouse.Id, cancellationToken);

                if (!exists)
                {
                    context.Categories.Add(category);
                    addedCount++;
                }
                else
                {
                    _logger.LogInformation("Category '{Name}' for Warehouse {WarehouseId} already exists, skipping...",
                        category.Name, warehouse.Id);
                }
            }

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Successfully seeded {AddedCount} of {TotalCount} standard categories!", addedCount, categories.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding categories");
            throw;
        }
    }
}
