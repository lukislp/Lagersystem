using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;
using ClosedXML.Excel;

namespace LagersystemLVHome.Application.Services;

public sealed class ImportService : IImportService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly ILogger<ImportService> _logger;
    private readonly IAuditService _auditService;

    public ImportService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        ILogger<ImportService> logger,
        IAuditService auditService)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _auditService = auditService;
    }

    public async Task<ImportResult> ImportProductsFromCsvAsync(Stream fileStream, int warehouseId, int userId, CancellationToken cancellationToken = default)
    {
        var result = new ImportResult();

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            using var reader = new StreamReader(fileStream, Encoding.UTF8);
            var headerLine = await reader.ReadLineAsync();

            if (string.IsNullOrEmpty(headerLine))
            {
                result.Errors.Add("CSV-Datei ist leer");
                return result;
            }

            // Expected header: Name,Beschreibung,Barcode,Kategorie,Menge,Mindestbestand
            var headers = ParseCsvLine(headerLine);

            var lineNumber = 1;
            while (!reader.EndOfStream)
            {
                lineNumber++;
                var line = await reader.ReadLineAsync();

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var fields = ParseCsvLine(line);

                    if (fields.Length < 4)
                    {
                        result.Errors.Add($"Zeile {lineNumber}: Zu wenige Felder");
                        result.FailedCount++;
                        continue;
                    }

                    var name = fields[0].Trim();
                    var description = fields.Length > 1 ? fields[1].Trim() : "";
                    var barcode = fields.Length > 2 ? fields[2].Trim() : "";
                    var categoryName = fields.Length > 3 ? fields[3].Trim() : "";
                    var quantity = fields.Length > 4 && int.TryParse(fields[4], out var q) ? q : 0;
                    var minQuantity = fields.Length > 5 && int.TryParse(fields[5], out var m) ? m : 0;

                    if (string.IsNullOrEmpty(name))
                    {
                        result.Errors.Add($"Zeile {lineNumber}: Name ist erforderlich");
                        result.FailedCount++;
                        continue;
                    }

                    var existingProduct = await context.Products
                        .FirstOrDefaultAsync(p => p.Name == name && p.WarehouseId == warehouseId, cancellationToken);

                    if (existingProduct != null)
                    {
                        result.Warnings.Add($"Zeile {lineNumber}: Produkt '{name}' existiert bereits");
                        result.FailedCount++;
                        continue;
                    }

                    // Find or create category
                    Category? category = null;
                    if (!string.IsNullOrEmpty(categoryName))
                    {
                        category = await context.Categories
                            .FirstOrDefaultAsync(c => c.Name == categoryName && c.WarehouseId == warehouseId, cancellationToken);

                        if (category == null)
                        {
                            category = new Category
                            {
                                Name = categoryName,
                                Icon = "bi-tag",
                                WarehouseId = warehouseId,
                                CreatedAt = DateTime.UtcNow
                            };
                            context.Categories.Add(category);
                            await context.SaveChangesAsync(cancellationToken);
                        }
                    }

                    var product = new Product
                    {
                        Name = name,
                        Description = description,
                        Barcode = barcode,
                        CategoryId = category?.Id ?? 0,
                        WarehouseId = warehouseId,
                        Quantity = quantity,
                        MinQuantity = minQuantity,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    context.Products.Add(product);
                    await context.SaveChangesAsync(cancellationToken);

                    await _auditService.LogAsync(
                        $"Produkt '{name}' importiert",
                        "Product",
                        product.Id,
                        warehouseId);

                    result.SuccessCount++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Zeile {lineNumber}: {ex.Message}");
                    result.FailedCount++;
                    _logger.LogError(ex, "Error importing product at line {LineNumber}", lineNumber);
                }
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Fehler beim Lesen der Datei: {ex.Message}");
            _logger.LogError(ex, "Error reading CSV file");
        }

        return result;
    }

    public async Task<ImportResult> ImportCategoriesFromCsvAsync(Stream fileStream, int warehouseId, int userId, CancellationToken cancellationToken = default)
    {
        var result = new ImportResult();

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            using var reader = new StreamReader(fileStream, Encoding.UTF8);
            var headerLine = await reader.ReadLineAsync();

            if (string.IsNullOrEmpty(headerLine))
            {
                result.Errors.Add("CSV-Datei ist leer");
                return result;
            }

            // Expected header: Name,Icon
            var lineNumber = 1;
            while (!reader.EndOfStream)
            {
                lineNumber++;
                var line = await reader.ReadLineAsync();

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var fields = ParseCsvLine(line);

                    if (fields.Length < 1)
                    {
                        result.Errors.Add($"Zeile {lineNumber}: Zu wenige Felder");
                        result.FailedCount++;
                        continue;
                    }

                    var name = fields[0].Trim();
                    var icon = fields.Length > 1 ? fields[1].Trim() : "bi-tag";

                    if (string.IsNullOrEmpty(name))
                    {
                        result.Errors.Add($"Zeile {lineNumber}: Name ist erforderlich");
                        result.FailedCount++;
                        continue;
                    }

                    var existingCategory = await context.Categories
                        .FirstOrDefaultAsync(c => c.Name == name && c.WarehouseId == warehouseId, cancellationToken);

                    if (existingCategory != null)
                    {
                        result.Warnings.Add($"Zeile {lineNumber}: Kategorie '{name}' existiert bereits");
                        result.FailedCount++;
                        continue;
                    }

                    var category = new Category
                    {
                        Name = name,
                        Icon = icon,
                        WarehouseId = warehouseId,
                        CreatedAt = DateTime.UtcNow
                    };

                    context.Categories.Add(category);
                    await context.SaveChangesAsync(cancellationToken);

                    await _auditService.LogAsync(
                        $"Kategorie '{name}' importiert",
                        "Category",
                        category.Id,
                        warehouseId);

                    result.SuccessCount++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Zeile {lineNumber}: {ex.Message}");
                    result.FailedCount++;
                    _logger.LogError(ex, "Error importing category at line {LineNumber}", lineNumber);
                }
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Fehler beim Lesen der Datei: {ex.Message}");
            _logger.LogError(ex, "Error reading CSV file");
        }

        return result;
    }

    public async Task<ImportResult> ImportProductsFromExcelAsync(Stream fileStream, int warehouseId, int userId, CancellationToken cancellationToken = default)
    {
        var result = new ImportResult();

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            using var workbook = new XLWorkbook(fileStream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet.LastRowUsed() == null || worksheet.LastRowUsed().RowNumber() < 2)
            {
                result.Errors.Add("Excel-Datei enth\u00e4lt keine Daten");
                return result;
            }

            // Columns: Name | Beschreibung | Barcode | Kategorie | Menge | Mindestbestand
            int startRow = 2;
            int lastRow = worksheet.LastRowUsed().RowNumber();

            for (int row = startRow; row <= lastRow; row++)
            {
                try
                {
                    var name = worksheet.Cell(row, 1).GetString().Trim();
                    var description = worksheet.Cell(row, 2).GetString().Trim();
                    var barcode = worksheet.Cell(row, 3).GetString().Trim();
                    var categoryName = worksheet.Cell(row, 4).GetString().Trim();

                    int quantity = 0;
                    if (worksheet.Cell(row, 5).TryGetValue(out int q))
                        quantity = q;

                    int minQuantity = 0;
                    if (worksheet.Cell(row, 6).TryGetValue(out int m))
                        minQuantity = m;

                    if (string.IsNullOrEmpty(name))
                    {
                        result.Errors.Add($"Zeile {row}: Name ist erforderlich");
                        result.FailedCount++;
                        continue;
                    }

                    var existingProduct = await context.Products
                        .FirstOrDefaultAsync(p => p.Name == name && p.WarehouseId == warehouseId, cancellationToken);

                    if (existingProduct != null)
                    {
                        result.Warnings.Add($"Zeile {row}: Produkt '{name}' existiert bereits");
                        result.FailedCount++;
                        continue;
                    }

                    // Find or create category
                    Category? category = null;
                    if (!string.IsNullOrEmpty(categoryName))
                    {
                        category = await context.Categories
                            .FirstOrDefaultAsync(c => c.Name == categoryName && c.WarehouseId == warehouseId, cancellationToken);

                        if (category == null)
                        {
                            category = new Category
                            {
                                Name = categoryName,
                                Icon = "bi-tag",
                                WarehouseId = warehouseId,
                                CreatedAt = DateTime.UtcNow
                            };
                            context.Categories.Add(category);
                            await context.SaveChangesAsync(cancellationToken);
                        }
                    }

                    var product = new Product
                    {
                        Name = name,
                        Description = description,
                        Barcode = barcode,
                        CategoryId = category?.Id ?? 0,
                        WarehouseId = warehouseId,
                        Quantity = quantity,
                        MinQuantity = minQuantity,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    context.Products.Add(product);
                    await context.SaveChangesAsync(cancellationToken);

                    await _auditService.LogAsync(
                        $"Produkt '{name}' importiert (Excel)",
                        "Product",
                        product.Id,
                        warehouseId);

                    result.SuccessCount++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Zeile {row}: {ex.Message}");
                    result.FailedCount++;
                    _logger.LogError(ex, "Error importing product from Excel at row {Row}", row);
                }
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Fehler beim Lesen der Excel-Datei: {ex.Message}");
            _logger.LogError(ex, "Error reading Excel file");
        }

        return result;
    }

    public async Task<ImportResult> ImportCategoriesFromExcelAsync(Stream fileStream, int warehouseId, int userId, CancellationToken cancellationToken = default)
    {
        var result = new ImportResult();

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            using var workbook = new XLWorkbook(fileStream);
            var worksheet = workbook.Worksheet(1);

            if (worksheet.LastRowUsed() == null || worksheet.LastRowUsed().RowNumber() < 2)
            {
                result.Errors.Add("Excel-Datei enth\u00e4lt keine Daten");
                return result;
            }

            // Header: Name | Icon
            int startRow = 2;
            int lastRow = worksheet.LastRowUsed().RowNumber();

            for (int row = startRow; row <= lastRow; row++)
            {
                try
                {
                    var name = worksheet.Cell(row, 1).GetString().Trim();
                    var icon = worksheet.Cell(row, 2).GetString().Trim();

                    if (string.IsNullOrEmpty(icon))
                        icon = "bi-tag";

                    if (string.IsNullOrEmpty(name))
                    {
                        result.Errors.Add($"Zeile {row}: Name ist erforderlich");
                        result.FailedCount++;
                        continue;
                    }

                    var existingCategory = await context.Categories
                        .FirstOrDefaultAsync(c => c.Name == name && c.WarehouseId == warehouseId, cancellationToken);

                    if (existingCategory != null)
                    {
                        result.Warnings.Add($"Zeile {row}: Kategorie '{name}' existiert bereits");
                        result.FailedCount++;
                        continue;
                    }

                    var category = new Category
                    {
                        Name = name,
                        Icon = icon,
                        WarehouseId = warehouseId,
                        CreatedAt = DateTime.UtcNow
                    };

                    context.Categories.Add(category);
                    await context.SaveChangesAsync(cancellationToken);

                    await _auditService.LogAsync(
                        $"Kategorie '{name}' importiert (Excel)",
                        "Category",
                        category.Id,
                        warehouseId);

                    result.SuccessCount++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Zeile {row}: {ex.Message}");
                    result.FailedCount++;
                    _logger.LogError(ex, "Error importing category from Excel at row {Row}", row);
                }
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Fehler beim Lesen der Excel-Datei: {ex.Message}");
            _logger.LogError(ex, "Error reading Excel file");
        }

        return result;
    }

    private string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var currentField = new StringBuilder();
        var inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    currentField.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(currentField.ToString());
                currentField.Clear();
            }
            else
            {
                currentField.Append(c);
            }
        }

        fields.Add(currentField.ToString());

        return fields.ToArray();
    }
}
