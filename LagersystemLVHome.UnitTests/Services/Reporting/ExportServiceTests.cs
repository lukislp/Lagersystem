using ClosedXML.Excel;
using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;

namespace LagersystemLVHome.UnitTests.Services.Reporting;

public class ExportServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    /// <summary>
    /// Fails on context creation, used to exercise the try/catch-log-rethrow paths every
    /// export method has around its entire body (unreachable with a healthy InMemory provider).
    /// </summary>
    private sealed class ThrowingContextFactory : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => throw new InvalidOperationException("Simulated DB failure");

        public Task<InventoryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated DB failure");
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static ExportService Build(IDbContextFactory<InventoryDbContext> factory)
        => new(factory, NullLogger<ExportService>.Instance);

    // ---- Entity builders ------------------------------------------------------

    private static Warehouse MakeWarehouse(int id, string name = "WH") => new()
    {
        Id = id,
        Name = name,
        Code = $"W{id:000}",
        Address = "addr",
        IsActive = true
    };

    private static Category MakeCategory(int id, int warehouseId, string name = "Cat") => new()
    {
        Id = id,
        Name = name,
        Icon = "bi-tag",
        WarehouseId = warehouseId
    };

    private static Product MakeProduct(
        int id, int warehouseId, int categoryId, string name,
        string description = "", string barcode = "",
        int quantity = 1, int minQuantity = 1, decimal price = 10) => new()
        {
            Id = id,
            Name = name,
            Description = description,
            Barcode = barcode,
            WarehouseId = warehouseId,
            CategoryId = categoryId,
            Quantity = quantity,
            MinQuantity = minQuantity,
            Price = price,
            CreatedAt = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 1, 16, 11, 0, 0, DateTimeKind.Utc)
        };

    private static StockMovement MakeMovement(int productId, int warehouseId, int quantityChange, MovementType type, DateTime timestamp, string? notes = null) => new()
    {
        ProductId = productId,
        WarehouseId = warehouseId,
        QuantityChange = quantityChange,
        Type = type,
        Notes = notes,
        Timestamp = timestamp
    };

    private static StorageLocation MakeLocation(int id, int warehouseId, string code, string? room = null) => new()
    {
        Id = id,
        Code = code,
        Name = $"Location {code}",
        Description = "desc",
        Room = room,
        WarehouseId = warehouseId,
        CreatedAt = new DateTime(2026, 1, 10, 9, 0, 0, DateTimeKind.Utc)
    };

    // ---- ExportProductsToCsvAsync -----------------------------------------------

    [Fact]
    public async Task ExportProductsToCsvAsync_HeaderAndRows_MatchExpectedFormat()
    {
        var factory = CreateFactory(nameof(ExportProductsToCsvAsync_HeaderAndRows_MatchExpectedFormat));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.Categories.Add(MakeCategory(1, 1, "Electronics"));
            db.Products.Add(MakeProduct(1, 1, 1, "Widget", description: "A widget", barcode: "123", quantity: 5, minQuantity: 2));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var csv = await sut.ExportProductsToCsvAsync(1);

        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Be("Name,Beschreibung,Barcode,Kategorie,Menge,Mindestbestand,Erstellt,Geaendert");
        lines[1].Should().Be("\"Widget\",\"A widget\",\"123\",\"Electronics\",5,2,2026-01-15 10:30,2026-01-16 11:00");
    }

    [Fact]
    public async Task ExportProductsToCsvAsync_EscapesQuotesAndStripsNewlines()
    {
        var factory = CreateFactory(nameof(ExportProductsToCsvAsync_EscapesQuotesAndStripsNewlines));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.Categories.Add(MakeCategory(1, 1));
            db.Products.Add(MakeProduct(1, 1, 1, "Wid\"get", description: "Line1\r\nLine2"));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var csv = await sut.ExportProductsToCsvAsync(1);

        csv.Should().Contain("\"Wid\"\"get\"", "embedded quotes are doubled per CSV convention");
        csv.Should().Contain("\"Line1 Line2\"", "CR/LF inside a field are stripped/collapsed to a single space");
        csv.Should().NotContain("\r\nLine2");
    }

    [Fact]
    public async Task ExportProductsToCsvAsync_FiltersByWarehouseAndOrdersByName()
    {
        var factory = CreateFactory(nameof(ExportProductsToCsvAsync_FiltersByWarehouseAndOrdersByName));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.AddRange(MakeWarehouse(1), MakeWarehouse(2));
            db.Categories.AddRange(MakeCategory(1, 1), MakeCategory(2, 2));
            db.Products.AddRange(
                MakeProduct(1, 1, 1, "Zebra"),
                MakeProduct(2, 1, 1, "Apple"),
                MakeProduct(3, 2, 2, "Other-Warehouse"));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var csv = await sut.ExportProductsToCsvAsync(1);

        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(3); // header + 2 rows
        lines[1].Should().StartWith("\"Apple\"", "products are ordered by name ascending");
        lines[2].Should().StartWith("\"Zebra\"");
        csv.Should().NotContain("Other-Warehouse");
    }

    [Fact]
    public async Task ExportProductsToCsvAsync_NoProducts_ReturnsHeaderOnly()
    {
        var factory = CreateFactory(nameof(ExportProductsToCsvAsync_NoProducts_ReturnsHeaderOnly));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var csv = await sut.ExportProductsToCsvAsync(1);

        csv.Trim().Should().Be("Name,Beschreibung,Barcode,Kategorie,Menge,Mindestbestand,Erstellt,Geaendert");
    }

    [Fact]
    public async Task ExportProductsToCsvAsync_ContextFactoryThrows_LogsAndRethrows()
    {
        var sut = Build(new ThrowingContextFactory());

        var act = () => sut.ExportProductsToCsvAsync(1);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ---- ExportCategoriesToCsvAsync ----------------------------------------------

    [Fact]
    public async Task ExportCategoriesToCsvAsync_HeaderAndRows_MatchExpectedFormat()
    {
        var factory = CreateFactory(nameof(ExportCategoriesToCsvAsync_HeaderAndRows_MatchExpectedFormat));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.Categories.Add(new Category
            {
                Id = 1,
                Name = "Books",
                Icon = "bi-book",
                WarehouseId = 1,
                CreatedAt = new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc)
            });
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var csv = await sut.ExportCategoriesToCsvAsync(1);

        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Be("Name,Icon,Erstellt");
        lines[1].Should().Be("\"Books\",\"bi-book\",2026-02-01 08:00");
    }

    [Fact]
    public async Task ExportCategoriesToCsvAsync_ContextFactoryThrows_LogsAndRethrows()
    {
        var sut = Build(new ThrowingContextFactory());

        var act = () => sut.ExportCategoriesToCsvAsync(1);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ---- ExportMovementsToCsvAsync -----------------------------------------------

    [Fact]
    public async Task ExportMovementsToCsvAsync_FiltersByWarehouseAndDateRangeOrdersDescending()
    {
        var factory = CreateFactory(nameof(ExportMovementsToCsvAsync_FiltersByWarehouseAndDateRangeOrdersDescending));
        var now = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.AddRange(MakeWarehouse(1), MakeWarehouse(2));
            db.Categories.Add(MakeCategory(1, 1));
            db.Products.Add(MakeProduct(1, 1, 1, "Widget"));
            db.StockMovements.AddRange(
                MakeMovement(1, 1, 5, MovementType.ManualAdd, now.AddDays(-1), notes: "in"),
                MakeMovement(1, 1, -2, MovementType.ManualRemove, now, notes: "out"),
                MakeMovement(1, 1, 100, MovementType.ManualAdd, now.AddDays(-30)), // outside range
                MakeMovement(1, 2, 1, MovementType.ManualAdd, now)); // other warehouse
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var csv = await sut.ExportMovementsToCsvAsync(1, now.AddDays(-5), now.AddDays(1));

        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Be("Produkt,Typ,Menge,Notizen,Zeitstempel");
        lines.Should().HaveCount(3); // header + 2 in-range same-warehouse movements
        lines[1].Should().Contain("\"out\"", "movements are ordered by timestamp descending, most recent first");
    }

    [Fact]
    public async Task ExportMovementsToCsvAsync_NoDateFilter_IncludesAllForWarehouse()
    {
        var factory = CreateFactory(nameof(ExportMovementsToCsvAsync_NoDateFilter_IncludesAllForWarehouse));
        var now = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.Categories.Add(MakeCategory(1, 1));
            db.Products.Add(MakeProduct(1, 1, 1, "Widget"));
            db.StockMovements.AddRange(
                MakeMovement(1, 1, 5, MovementType.ManualAdd, now.AddYears(-5)),
                MakeMovement(1, 1, 2, MovementType.ManualAdd, now));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var csv = await sut.ExportMovementsToCsvAsync(1, null, null);

        csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(3);
    }

    [Fact]
    public async Task ExportMovementsToCsvAsync_ContextFactoryThrows_LogsAndRethrows()
    {
        var sut = Build(new ThrowingContextFactory());

        var act = () => sut.ExportMovementsToCsvAsync(1, null, null);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ---- ExportProductsToExcelAsync ------------------------------------------------

    [Fact]
    public async Task ExportProductsToExcelAsync_WritesHeaderStylingAndDataRows()
    {
        var factory = CreateFactory(nameof(ExportProductsToExcelAsync_WritesHeaderStylingAndDataRows));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.Categories.Add(MakeCategory(1, 1, "Electronics"));
            db.Products.Add(MakeProduct(1, 1, 1, "Widget", quantity: 5, minQuantity: 2, price: 19.99m));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var bytes = await sut.ExportProductsToExcelAsync(1);

        bytes.Should().NotBeEmpty();
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var ws = workbook.Worksheet("Produkte");

        ws.Cell(1, 1).GetString().Should().Be("Name");
        ws.Cell(1, 7).GetString().Should().Be("Preis");
        ws.Cell(1, 1).Style.Font.Bold.Should().BeTrue();
        ws.Cell(1, 1).Style.Fill.BackgroundColor.Should().Be(XLColor.FromHtml("#667eea"));

        ws.Cell(2, 1).GetString().Should().Be("Widget");
        ws.Cell(2, 4).GetString().Should().Be("Electronics");
        ws.Cell(2, 5).GetValue<int>().Should().Be(5);
        ws.Cell(2, 6).GetValue<int>().Should().Be(2);
        ws.Cell(2, 7).GetValue<decimal>().Should().Be(19.99m);
        ws.Cell(2, 8).GetString().Should().Be("15.01.2026 10:30");
    }

    [Fact]
    public async Task ExportProductsToExcelAsync_ContextFactoryThrows_LogsAndRethrows()
    {
        var sut = Build(new ThrowingContextFactory());

        var act = () => sut.ExportProductsToExcelAsync(1);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ---- ExportCategoriesToExcelAsync ----------------------------------------------

    [Fact]
    public async Task ExportCategoriesToExcelAsync_WritesHeaderAndDataRows()
    {
        var factory = CreateFactory(nameof(ExportCategoriesToExcelAsync_WritesHeaderAndDataRows));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.Categories.Add(MakeCategory(1, 1, "Books"));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var bytes = await sut.ExportCategoriesToExcelAsync(1);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var ws = workbook.Worksheet("Kategorien");
        ws.Cell(1, 1).GetString().Should().Be("Name");
        ws.Cell(2, 1).GetString().Should().Be("Books");
    }

    [Fact]
    public async Task ExportCategoriesToExcelAsync_ContextFactoryThrows_LogsAndRethrows()
    {
        var sut = Build(new ThrowingContextFactory());

        var act = () => sut.ExportCategoriesToExcelAsync(1);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ---- ExportMovementsToExcelAsync -----------------------------------------------

    [Fact]
    public async Task ExportMovementsToExcelAsync_WritesHeaderAndDataRows()
    {
        var factory = CreateFactory(nameof(ExportMovementsToExcelAsync_WritesHeaderAndDataRows));
        var now = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.Categories.Add(MakeCategory(1, 1));
            db.Products.Add(MakeProduct(1, 1, 1, "Widget"));
            db.StockMovements.Add(MakeMovement(1, 1, 3, MovementType.ScanAdd, now, notes: "scanned"));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var bytes = await sut.ExportMovementsToExcelAsync(1, null, null);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var ws = workbook.Worksheet("Bewegungen");
        ws.Cell(1, 1).GetString().Should().Be("Produkt");
        ws.Cell(2, 1).GetString().Should().Be("Widget");
        ws.Cell(2, 2).GetString().Should().Be(nameof(MovementType.ScanAdd));
        ws.Cell(2, 3).GetValue<int>().Should().Be(3);
        ws.Cell(2, 4).GetString().Should().Be("scanned");
    }

    [Fact]
    public async Task ExportMovementsToExcelAsync_FiltersByDateRange()
    {
        var factory = CreateFactory(nameof(ExportMovementsToExcelAsync_FiltersByDateRange));
        var now = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.Categories.Add(MakeCategory(1, 1));
            db.Products.Add(MakeProduct(1, 1, 1, "Widget"));
            db.StockMovements.AddRange(
                MakeMovement(1, 1, 3, MovementType.ScanAdd, now, notes: "in-range"),
                MakeMovement(1, 1, 1, MovementType.ScanAdd, now.AddDays(-30), notes: "before-range"),
                MakeMovement(1, 1, 1, MovementType.ScanAdd, now.AddDays(30), notes: "after-range"));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var bytes = await sut.ExportMovementsToExcelAsync(1, now.AddDays(-1), now.AddDays(1));

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var ws = workbook.Worksheet("Bewegungen");
        var lastRow = ws.LastRowUsed()!.RowNumber();
        lastRow.Should().Be(2, "only the in-range movement should be present (header + 1 data row)");
        ws.Cell(2, 4).GetString().Should().Be("in-range");
    }

    [Fact]
    public async Task ExportMovementsToExcelAsync_ContextFactoryThrows_LogsAndRethrows()
    {
        var sut = Build(new ThrowingContextFactory());

        var act = () => sut.ExportMovementsToExcelAsync(1, null, null);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ---- ExportStorageLocationsToExcelAsync -----------------------------------------

    [Fact]
    public async Task ExportStorageLocationsToExcelAsync_WritesHeaderAndDataRows()
    {
        // Regression test: production code used to do `.Include(sl => sl.Room)`, but
        // StorageLocation.Room is a plain string column, not a navigation property - see
        // StorageLocation.cs. EF Core's Include() requires a navigation property, so calling
        // it on a scalar member threw InvalidOperationException at query-translation time on
        // every call, making this export endpoint completely broken. The Include() is gone;
        // Room is a normal column and needs no eager-loading.
        var factory = CreateFactory(nameof(ExportStorageLocationsToExcelAsync_WritesHeaderAndDataRows));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.StorageLocations.Add(MakeLocation(1, 1, "A1", room: "Hall A"));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);

        var bytes = await sut.ExportStorageLocationsToExcelAsync(1);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var ws = workbook.Worksheet("Lagerplaetze");
        ws.Cell(1, 1).GetString().Should().Be("Code");
        ws.Cell(1, 3).GetString().Should().Be("Raum");
        ws.Cell(2, 1).GetString().Should().Be("A1");
        ws.Cell(2, 3).GetString().Should().Be("Hall A");
    }

    // ---- GenerateInventoryReportPdfAsync (HTML) -------------------------------------

    [Fact]
    public async Task GenerateInventoryReportPdfAsync_EmitsHtmlWithWarehouseAndProductRows()
    {
        var factory = CreateFactory(nameof(GenerateInventoryReportPdfAsync_EmitsHtmlWithWarehouseAndProductRows));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1, "Main Warehouse"));
            db.Categories.Add(MakeCategory(1, 1, "Electronics"));
            db.Products.Add(MakeProduct(1, 1, 1, "Widget", quantity: 5, minQuantity: 2));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var bytes = await sut.GenerateInventoryReportPdfAsync(1);

        var html = Encoding.UTF8.GetString(bytes);
        html.Should().Contain("<!DOCTYPE html>");
        html.Should().Contain("Main Warehouse");
        html.Should().Contain("Anzahl Produkte: 1");
        html.Should().Contain("<td>Widget</td>");
        html.Should().Contain("<td>Electronics</td>");
        html.Should().Contain("<td>5</td>");
    }

    [Fact]
    public async Task GenerateInventoryReportPdfAsync_UnknownWarehouse_OmitsNameGracefully()
    {
        var factory = CreateFactory(nameof(GenerateInventoryReportPdfAsync_UnknownWarehouse_OmitsNameGracefully));
        var sut = Build(factory);

        var bytes = await sut.GenerateInventoryReportPdfAsync(999);

        var html = Encoding.UTF8.GetString(bytes);
        html.Should().Contain("Anzahl Produkte: 0");
    }

    [Fact]
    public async Task GenerateInventoryReportPdfAsync_ContextFactoryThrows_LogsAndRethrows()
    {
        var sut = Build(new ThrowingContextFactory());

        var act = () => sut.GenerateInventoryReportPdfAsync(1);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ---- GenerateStockMovementReportPdfAsync (HTML) ---------------------------------

    [Fact]
    public async Task GenerateStockMovementReportPdfAsync_EmitsHtmlWithMovementRows()
    {
        var factory = CreateFactory(nameof(GenerateStockMovementReportPdfAsync_EmitsHtmlWithMovementRows));
        var now = DateTime.UtcNow;
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1, "Main Warehouse"));
            db.Categories.Add(MakeCategory(1, 1));
            db.Products.Add(MakeProduct(1, 1, 1, "Widget"));
            db.StockMovements.Add(MakeMovement(1, 1, 3, MovementType.ManualAdd, now, notes: "restock"));
            await db.SaveChangesAsync();
        }

        var sut = Build(factory);
        var bytes = await sut.GenerateStockMovementReportPdfAsync(1, now.AddDays(-1), now.AddDays(1));

        var html = Encoding.UTF8.GetString(bytes);
        html.Should().Contain("Main Warehouse");
        html.Should().Contain("Anzahl Bewegungen: 1");
        html.Should().Contain("<td>Widget</td>");
        html.Should().Contain("<td>restock</td>");
    }

    [Fact]
    public async Task GenerateStockMovementReportPdfAsync_ContextFactoryThrows_LogsAndRethrows()
    {
        var sut = Build(new ThrowingContextFactory());

        var act = () => sut.GenerateStockMovementReportPdfAsync(1, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
