using LagersystemLVHome.Data;
using LagersystemLVHome.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace LagersystemLVHome.Application.Services;

public sealed class ExportService : IExportService
{
    private readonly IDbContextFactory<InventoryDbContext> _contextFactory;
    private readonly ILogger<ExportService> _logger;

    public ExportService(
        IDbContextFactory<InventoryDbContext> contextFactory,
        ILogger<ExportService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    // CSV Export
    public async Task<string> ExportProductsToCsvAsync(int warehouseId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var products = await context.Products
                .Include(p => p.Category)
                .Where(p => p.WarehouseId == warehouseId)
                .OrderBy(p => p.Name)
                .ToListAsync(cancellationToken);

            var csv = new StringBuilder();
            csv.AppendLine("Name,Beschreibung,Barcode,Kategorie,Menge,Mindestbestand,Erstellt,Geaendert");

            foreach (var product in products)
            {
                csv.AppendLine($"\"{EscapeCsv(product.Name)}\"," +
                    $"\"{EscapeCsv(product.Description)}\"," +
                    $"\"{EscapeCsv(product.Barcode)}\"," +
                    $"\"{EscapeCsv(product.Category?.Name ?? "")}\"," +
                    $"{product.Quantity}," +
                    $"{product.MinQuantity}," +
                    $"{product.CreatedAt:yyyy-MM-dd HH:mm}," +
                    $"{product.UpdatedAt:yyyy-MM-dd HH:mm}");
            }

            return csv.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting products to CSV");
            throw;
        }
    }

    public async Task<string> ExportCategoriesToCsvAsync(int warehouseId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var categories = await context.Categories
                .Where(c => c.WarehouseId == warehouseId)
                .OrderBy(c => c.Name)
                .ToListAsync(cancellationToken);

            var csv = new StringBuilder();
            csv.AppendLine("Name,Icon,Erstellt");

            foreach (var category in categories)
            {
                csv.AppendLine($"\"{EscapeCsv(category.Name)}\"," +
                    $"\"{EscapeCsv(category.Icon)}\"," +
                    $"{category.CreatedAt:yyyy-MM-dd HH:mm}");
            }

            return csv.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting categories to CSV");
            throw;
        }
    }

    public async Task<string> ExportMovementsToCsvAsync(int warehouseId, DateTime? from, DateTime? to, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var query = context.StockMovements
                .Include(sm => sm.Product)
                .Where(sm => sm.WarehouseId == warehouseId);

            if (from.HasValue)
                query = query.Where(sm => sm.Timestamp >= from.Value);

            if (to.HasValue)
                query = query.Where(sm => sm.Timestamp <= to.Value);

            var movements = await query
                .OrderByDescending(sm => sm.Timestamp)
                .ToListAsync(cancellationToken);

            var csv = new StringBuilder();
            csv.AppendLine("Produkt,Typ,Menge,Notizen,Zeitstempel");

            foreach (var movement in movements)
            {
                csv.AppendLine($"\"{EscapeCsv(movement.Product.Name)}\"," +
                    $"{movement.Type}," +
                    $"{movement.QuantityChange}," +
                    $"\"{EscapeCsv(movement.Notes ?? "")}\"," +
                    $"{movement.Timestamp:yyyy-MM-dd HH:mm}");
            }

            return csv.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting movements to CSV");
            throw;
        }
    }

    // Excel Export (real Excel format with ClosedXML)
    public async Task<byte[]> ExportProductsToExcelAsync(int warehouseId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var products = await context.Products
                .Include(p => p.Category)
                .Where(p => p.WarehouseId == warehouseId)
                .OrderBy(p => p.Name)
                .ToListAsync(cancellationToken);

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Produkte");

            // Header
            worksheet.Cell(1, 1).Value = "Name";
            worksheet.Cell(1, 2).Value = "Beschreibung";
            worksheet.Cell(1, 3).Value = "Barcode";
            worksheet.Cell(1, 4).Value = "Kategorie";
            worksheet.Cell(1, 5).Value = "Menge";
            worksheet.Cell(1, 6).Value = "Mindestbestand";
            worksheet.Cell(1, 7).Value = "Preis";
            worksheet.Cell(1, 8).Value = "Erstellt";
            worksheet.Cell(1, 9).Value = "Geaendert";

            // Header styling
            var headerRow = worksheet.Range("A1:I1");
            headerRow.Style.Font.Bold = true;
            headerRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#667eea");
            headerRow.Style.Font.FontColor = XLColor.White;
            headerRow.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // Data
            int row = 2;
            foreach (var product in products)
            {
                worksheet.Cell(row, 1).Value = product.Name;
                worksheet.Cell(row, 2).Value = product.Description;
                worksheet.Cell(row, 3).Value = product.Barcode;
                worksheet.Cell(row, 4).Value = product.Category?.Name ?? "";
                worksheet.Cell(row, 5).Value = product.Quantity;
                worksheet.Cell(row, 6).Value = product.MinQuantity;
                worksheet.Cell(row, 7).Value = product.Price;
                worksheet.Cell(row, 8).Value = product.CreatedAt.ToString("dd.MM.yyyy HH:mm");
                worksheet.Cell(row, 9).Value = product.UpdatedAt.ToString("dd.MM.yyyy HH:mm");
                row++;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting products to Excel");
            throw;
        }
    }

    public async Task<byte[]> ExportCategoriesToExcelAsync(int warehouseId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var categories = await context.Categories
                .Where(c => c.WarehouseId == warehouseId)
                .OrderBy(c => c.Name)
                .ToListAsync(cancellationToken);

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Kategorien");

            // Header
            worksheet.Cell(1, 1).Value = "Name";
            worksheet.Cell(1, 2).Value = "Icon";
            worksheet.Cell(1, 3).Value = "Erstellt";

            // Header styling
            var headerRow = worksheet.Range("A1:C1");
            headerRow.Style.Font.Bold = true;
            headerRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#667eea");
            headerRow.Style.Font.FontColor = XLColor.White;

            // Data
            int row = 2;
            foreach (var category in categories)
            {
                worksheet.Cell(row, 1).Value = category.Name;
                worksheet.Cell(row, 2).Value = category.Icon;
                worksheet.Cell(row, 3).Value = category.CreatedAt.ToString("dd.MM.yyyy HH:mm");
                row++;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting categories to Excel");
            throw;
        }
    }

    public async Task<byte[]> ExportMovementsToExcelAsync(int warehouseId, DateTime? from, DateTime? to, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var query = context.StockMovements
                .Include(sm => sm.Product)
                .Where(sm => sm.WarehouseId == warehouseId);

            if (from.HasValue)
                query = query.Where(sm => sm.Timestamp >= from.Value);

            if (to.HasValue)
                query = query.Where(sm => sm.Timestamp <= to.Value);

            var movements = await query
                .OrderByDescending(sm => sm.Timestamp)
                .ToListAsync(cancellationToken);

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Bewegungen");

            // Header
            worksheet.Cell(1, 1).Value = "Produkt";
            worksheet.Cell(1, 2).Value = "Typ";
            worksheet.Cell(1, 3).Value = "Menge";
            worksheet.Cell(1, 4).Value = "Notizen";
            worksheet.Cell(1, 5).Value = "Zeitstempel";

            // Header styling
            var headerRow = worksheet.Range("A1:E1");
            headerRow.Style.Font.Bold = true;
            headerRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#667eea");
            headerRow.Style.Font.FontColor = XLColor.White;

            // Data
            int row = 2;
            foreach (var movement in movements)
            {
                worksheet.Cell(row, 1).Value = movement.Product.Name;
                worksheet.Cell(row, 2).Value = movement.Type.ToString();
                worksheet.Cell(row, 3).Value = movement.QuantityChange;
                worksheet.Cell(row, 4).Value = movement.Notes ?? "";
                worksheet.Cell(row, 5).Value = movement.Timestamp.ToString("dd.MM.yyyy HH:mm");
                row++;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting movements to Excel");
            throw;
        }
    }

    public async Task<byte[]> ExportStorageLocationsToExcelAsync(int warehouseId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var locations = await context.StorageLocations
                .Include(sl => sl.Room)
                .Where(sl => sl.WarehouseId == warehouseId)
                .OrderBy(sl => sl.Code)
                .ToListAsync(cancellationToken);

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Lagerplaetze");

            // Header
            worksheet.Cell(1, 1).Value = "Code";
            worksheet.Cell(1, 2).Value = "Name";
            worksheet.Cell(1, 3).Value = "Raum";
            worksheet.Cell(1, 4).Value = "Beschreibung";
            worksheet.Cell(1, 5).Value = "Erstellt";

            // Header styling
            var headerRow = worksheet.Range("A1:E1");
            headerRow.Style.Font.Bold = true;
            headerRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#667eea");
            headerRow.Style.Font.FontColor = XLColor.White;

            // Data
            int row = 2;
            foreach (var location in locations)
            {
                worksheet.Cell(row, 1).Value = location.Code;
                worksheet.Cell(row, 2).Value = location.Name;
                worksheet.Cell(row, 3).Value = location.Room ?? "";
                worksheet.Cell(row, 4).Value = location.Description;
                worksheet.Cell(row, 5).Value = location.CreatedAt.ToString("dd.MM.yyyy HH:mm");
                row++;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting storage locations to Excel");
            throw;
        }
    }

    // PDF Export (simplified - HTML format)
    public async Task<byte[]> GenerateInventoryReportPdfAsync(int warehouseId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var products = await context.Products
                .Include(p => p.Category)
                .Where(p => p.WarehouseId == warehouseId)
                .OrderBy(p => p.Name)
                .ToListAsync(cancellationToken);

            var warehouse = await context.Warehouses.FindAsync(warehouseId);

            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html><html><head>");
            html.AppendLine("<meta charset='utf-8'>");
            html.AppendLine("<title>Inventarbericht</title>");
            html.AppendLine("<style>");
            html.AppendLine("body { font-family: Arial, sans-serif; margin: 20px; }");
            html.AppendLine("h1 { color: #667eea; }");
            html.AppendLine("table { width: 100%; border-collapse: collapse; margin-top: 20px; }");
            html.AppendLine("th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }");
            html.AppendLine("th { background-color: #667eea; color: white; }");
            html.AppendLine("</style>");
            html.AppendLine("</head><body>");
            html.AppendLine($"<h1>Inventarbericht - {warehouse?.Name}</h1>");
            html.AppendLine($"<p>Erstellt am: {DateTime.UtcNow:dd.MM.yyyy HH:mm}</p>");
            html.AppendLine($"<p>Anzahl Produkte: {products.Count}</p>");
            html.AppendLine("<table>");
            html.AppendLine("<tr><th>Name</th><th>Kategorie</th><th>Menge</th><th>Mindestbestand</th></tr>");

            foreach (var product in products)
            {
                html.AppendLine($"<tr>");
                html.AppendLine($"<td>{product.Name}</td>");
                html.AppendLine($"<td>{product.Category?.Name ?? "-"}</td>");
                html.AppendLine($"<td>{product.Quantity}</td>");
                html.AppendLine($"<td>{product.MinQuantity}</td>");
                html.AppendLine($"</tr>");
            }

            html.AppendLine("</table>");
            html.AppendLine("</body></html>");

            return Encoding.UTF8.GetBytes(html.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating inventory report");
            throw;
        }
    }

    public async Task<byte[]> GenerateStockMovementReportPdfAsync(int warehouseId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var movements = await context.StockMovements
                .Include(sm => sm.Product)
                .Where(sm => sm.WarehouseId == warehouseId && sm.Timestamp >= from && sm.Timestamp <= to)
                .OrderByDescending(sm => sm.Timestamp)
                .ToListAsync(cancellationToken);

            var warehouse = await context.Warehouses.FindAsync(warehouseId);

            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html><html><head>");
            html.AppendLine("<meta charset='utf-8'>");
            html.AppendLine("<title>Bewegungsbericht</title>");
            html.AppendLine("<style>");
            html.AppendLine("body { font-family: Arial, sans-serif; margin: 20px; }");
            html.AppendLine("h1 { color: #667eea; }");
            html.AppendLine("table { width: 100%; border-collapse: collapse; margin-top: 20px; }");
            html.AppendLine("th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }");
            html.AppendLine("th { background-color: #667eea; color: white; }");
            html.AppendLine("</style>");
            html.AppendLine("</head><body>");
            html.AppendLine($"<h1>Bewegungsbericht - {warehouse?.Name}</h1>");
            html.AppendLine($"<p>Zeitraum: {from:dd.MM.yyyy} - {to:dd.MM.yyyy}</p>");
            html.AppendLine($"<p>Anzahl Bewegungen: {movements.Count}</p>");
            html.AppendLine("<table>");
            html.AppendLine("<tr><th>Datum</th><th>Produkt</th><th>Typ</th><th>Menge</th><th>Notizen</th></tr>");

            foreach (var movement in movements)
            {
                html.AppendLine($"<tr>");
                html.AppendLine($"<td>{movement.Timestamp:dd.MM.yyyy HH:mm}</td>");
                html.AppendLine($"<td>{movement.Product.Name}</td>");
                html.AppendLine($"<td>{movement.Type}</td>");
                html.AppendLine($"<td>{movement.QuantityChange}</td>");
                html.AppendLine($"<td>{movement.Notes ?? ""}</td>");
                html.AppendLine($"</tr>");
            }

            html.AppendLine("</table>");
            html.AppendLine("</body></html>");

            return Encoding.UTF8.GetBytes(html.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating movement report");
            throw;
        }
    }

    private string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        return value.Replace("\"", "\"\"").Replace("\n", " ").Replace("\r", "");
    }
}
