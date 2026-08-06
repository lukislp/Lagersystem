using ClosedXML.Excel;
using LagersystemLVHome.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;

namespace LagersystemLVHome.UnitTests.Services.Reporting;

public class ImportServiceTests
{
    private sealed class InMemoryContextFactory(DbContextOptions<InventoryDbContext> options)
        : IDbContextFactory<InventoryDbContext>
    {
        public InventoryDbContext CreateDbContext() => new(options);
    }

    /// <summary>Stream whose Read* members always throw, used to exercise the outer
    /// "Fehler beim Lesen der Datei" catch block around CSV reading.</summary>
    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("Simulated read failure");
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => throw new IOException("Simulated read failure");
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static IDbContextFactory<InventoryDbContext> CreateFactory(string name)
        => new InMemoryContextFactory(
            new DbContextOptionsBuilder<InventoryDbContext>().UseInMemoryDatabase(name).Options);

    private static ImportService Build(IDbContextFactory<InventoryDbContext> factory, IAuditService? audit = null)
        => new(factory, NullLogger<ImportService>.Instance, audit ?? Substitute.For<IAuditService>());

    private static Warehouse MakeWarehouse(int id, string name = "WH") => new()
    {
        Id = id,
        Name = name,
        Code = $"W{id:000}",
        Address = "addr",
        IsActive = true
    };

    private static Stream CsvStream(params string[] lines)
        => new MemoryStream(Encoding.UTF8.GetBytes(string.Join("\r\n", lines)));

    private static Stream ExcelStream(string[] headers, IEnumerable<object?[]> rows)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Sheet1");
        for (var c = 0; c < headers.Length; c++)
            ws.Cell(1, c + 1).Value = headers[c];

        var row = 2;
        foreach (var values in rows)
        {
            for (var c = 0; c < values.Length; c++)
            {
                var v = values[c];
                if (v is int i) ws.Cell(row, c + 1).Value = i;
                else if (v is not null) ws.Cell(row, c + 1).Value = v.ToString();
            }
            row++;
        }

        var ms = new MemoryStream();
        workbook.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    // ---- ImportProductsFromCsvAsync -----------------------------------------------

    [Fact]
    public async Task ImportProductsFromCsvAsync_ValidRow_CreatesProductAndCategoryAndLogsAudit()
    {
        var factory = CreateFactory(nameof(ImportProductsFromCsvAsync_ValidRow_CreatesProductAndCategoryAndLogsAudit));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            await db.SaveChangesAsync();
        }
        var audit = Substitute.For<IAuditService>();
        var sut = Build(factory, audit);
        var stream = CsvStream(
            "Name,Beschreibung,Barcode,Kategorie,Menge,Mindestbestand",
            "Widget,A nice widget,123,Electronics,10,2");

        var result = await sut.ImportProductsFromCsvAsync(stream, warehouseId: 1, userId: 1);

        result.SuccessCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
        result.HasErrors.Should().BeFalse();

        await using var verify = factory.CreateDbContext();
        var product = (await verify.Products.ToListAsync()).Should().ContainSingle().Subject;
        product.Name.Should().Be("Widget");
        product.Description.Should().Be("A nice widget");
        product.Quantity.Should().Be(10);
        product.MinQuantity.Should().Be(2);

        var category = (await verify.Categories.ToListAsync()).Should().ContainSingle().Subject;
        category.Name.Should().Be("Electronics");
        product.CategoryId.Should().Be(category.Id);

        await audit.Received(1).LogAsync(
            Arg.Is<string>(s => s.Contains("Widget")), "Product", Arg.Any<int?>(), Arg.Any<object?>(),
            Arg.Any<AuditSeverity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportProductsFromCsvAsync_ReusesExistingCategoryAcrossRows()
    {
        var factory = CreateFactory(nameof(ImportProductsFromCsvAsync_ReusesExistingCategoryAcrossRows));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);
        var stream = CsvStream(
            "Name,Beschreibung,Barcode,Kategorie,Menge,Mindestbestand",
            "Widget A,,,Electronics,1,1",
            "Widget B,,,Electronics,2,1");

        var result = await sut.ImportProductsFromCsvAsync(stream, warehouseId: 1, userId: 1);

        result.SuccessCount.Should().Be(2);
        await using var verify = factory.CreateDbContext();
        (await verify.Categories.CountAsync()).Should().Be(1, "both rows share the same category name");
        (await verify.Products.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task ImportProductsFromCsvAsync_EmptyFile_ReturnsErrorAndNoRows()
    {
        var factory = CreateFactory(nameof(ImportProductsFromCsvAsync_EmptyFile_ReturnsErrorAndNoRows));
        var sut = Build(factory);
        var stream = CsvStream("");

        var result = await sut.ImportProductsFromCsvAsync(stream, warehouseId: 1, userId: 1);

        result.Errors.Should().ContainSingle().Which.Should().Contain("leer");
        result.SuccessCount.Should().Be(0);
    }

    [Fact]
    public async Task ImportProductsFromCsvAsync_TooFewFields_RecordsErrorAndSkipsRow()
    {
        var factory = CreateFactory(nameof(ImportProductsFromCsvAsync_TooFewFields_RecordsErrorAndSkipsRow));
        var sut = Build(factory);
        var stream = CsvStream(
            "Name,Beschreibung,Barcode,Kategorie,Menge,Mindestbestand",
            "OnlyOne,Two,Three"); // 3 fields, < 4 required

        var result = await sut.ImportProductsFromCsvAsync(stream, warehouseId: 1, userId: 1);

        result.FailedCount.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("Zu wenige Felder");
        result.SuccessCount.Should().Be(0);
    }

    [Fact]
    public async Task ImportProductsFromCsvAsync_MissingName_RecordsErrorAndSkipsRow()
    {
        var factory = CreateFactory(nameof(ImportProductsFromCsvAsync_MissingName_RecordsErrorAndSkipsRow));
        var sut = Build(factory);
        var stream = CsvStream(
            "Name,Beschreibung,Barcode,Kategorie,Menge,Mindestbestand",
            ",Desc,Barcode,Cat,1,1");

        var result = await sut.ImportProductsFromCsvAsync(stream, warehouseId: 1, userId: 1);

        result.FailedCount.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("Name ist erforderlich");
    }

    [Fact]
    public async Task ImportProductsFromCsvAsync_DuplicateProductName_RecordsWarningAndSkipsRow()
    {
        var factory = CreateFactory(nameof(ImportProductsFromCsvAsync_DuplicateProductName_RecordsWarningAndSkipsRow));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.Categories.Add(new Category { Id = 1, Name = "Cat", WarehouseId = 1 });
            db.Products.Add(new Product { Id = 1, Name = "Widget", CategoryId = 1, WarehouseId = 1, Price = 1 });
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);
        var stream = CsvStream(
            "Name,Beschreibung,Barcode,Kategorie,Menge,Mindestbestand",
            "Widget,,,,1,1");

        var result = await sut.ImportProductsFromCsvAsync(stream, warehouseId: 1, userId: 1);

        result.FailedCount.Should().Be(1);
        result.Warnings.Should().ContainSingle().Which.Should().Contain("existiert bereits");
        result.SuccessCount.Should().Be(0);
    }

    [Fact]
    public async Task ImportProductsFromCsvAsync_NonNumericQuantities_DefaultToZero()
    {
        var factory = CreateFactory(nameof(ImportProductsFromCsvAsync_NonNumericQuantities_DefaultToZero));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);
        var stream = CsvStream(
            "Name,Beschreibung,Barcode,Kategorie,Menge,Mindestbestand",
            "Widget,,,,not-a-number,also-not-a-number");

        var result = await sut.ImportProductsFromCsvAsync(stream, warehouseId: 1, userId: 1);

        result.SuccessCount.Should().Be(1);
        await using var verify = factory.CreateDbContext();
        var product = (await verify.Products.ToListAsync()).Single();
        product.Quantity.Should().Be(0);
        product.MinQuantity.Should().Be(0);
    }

    [Fact]
    public async Task ImportProductsFromCsvAsync_BlankLinesAreSkipped()
    {
        var factory = CreateFactory(nameof(ImportProductsFromCsvAsync_BlankLinesAreSkipped));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);
        var stream = CsvStream(
            "Name,Beschreibung,Barcode,Kategorie,Menge,Mindestbestand",
            "",
            "   ",
            "Widget,,,,1,1");

        var result = await sut.ImportProductsFromCsvAsync(stream, warehouseId: 1, userId: 1);

        result.SuccessCount.Should().Be(1);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportProductsFromCsvAsync_QuotedFieldsWithEmbeddedCommasAndQuotes_ParseCorrectly()
    {
        var factory = CreateFactory(nameof(ImportProductsFromCsvAsync_QuotedFieldsWithEmbeddedCommasAndQuotes_ParseCorrectly));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);
        var stream = CsvStream(
            "Name,Beschreibung,Barcode,Kategorie,Menge,Mindestbestand",
            "\"Widget, Deluxe\",\"Has \"\"quotes\"\" inside\",123,Electronics,3,1");

        var result = await sut.ImportProductsFromCsvAsync(stream, warehouseId: 1, userId: 1);

        result.SuccessCount.Should().Be(1);
        await using var verify = factory.CreateDbContext();
        var product = (await verify.Products.ToListAsync()).Single();
        product.Name.Should().Be("Widget, Deluxe");
        product.Description.Should().Be("Has \"quotes\" inside");
    }

    [Fact]
    public async Task ImportProductsFromCsvAsync_NonAsciiCharacters_ImportCorrectlyAsUtf8()
    {
        var factory = CreateFactory(nameof(ImportProductsFromCsvAsync_NonAsciiCharacters_ImportCorrectlyAsUtf8));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);
        var stream = CsvStream(
            "Name,Beschreibung,Barcode,Kategorie,Menge,Mindestbestand",
            "Ünïcödé Wärmeflasche,Bëschreibung mit Ümläuten,,Käse & Co.,1,1");

        var result = await sut.ImportProductsFromCsvAsync(stream, warehouseId: 1, userId: 1);

        result.SuccessCount.Should().Be(1);
        await using var verify = factory.CreateDbContext();
        var product = (await verify.Products.ToListAsync()).Single();
        product.Name.Should().Be("Ünïcödé Wärmeflasche");
        product.Description.Should().Be("Bëschreibung mit Ümläuten");
    }

    [Fact]
    public async Task ImportProductsFromCsvAsync_UnreadableStream_RecordsFileLevelError()
    {
        var factory = CreateFactory(nameof(ImportProductsFromCsvAsync_UnreadableStream_RecordsFileLevelError));
        var sut = Build(factory);

        var result = await sut.ImportProductsFromCsvAsync(new ThrowingStream(), warehouseId: 1, userId: 1);

        result.Errors.Should().ContainSingle().Which.Should().Contain("Fehler beim Lesen der Datei");
    }

    // ---- ImportCategoriesFromCsvAsync ---------------------------------------------

    [Fact]
    public async Task ImportCategoriesFromCsvAsync_ValidRow_CreatesCategoryAndLogsAudit()
    {
        var factory = CreateFactory(nameof(ImportCategoriesFromCsvAsync_ValidRow_CreatesCategoryAndLogsAudit));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            await db.SaveChangesAsync();
        }
        var audit = Substitute.For<IAuditService>();
        var sut = Build(factory, audit);
        var stream = CsvStream("Name,Icon", "Books,bi-book");

        var result = await sut.ImportCategoriesFromCsvAsync(stream, warehouseId: 1, userId: 1);

        result.SuccessCount.Should().Be(1);
        await using var verify = factory.CreateDbContext();
        var category = (await verify.Categories.ToListAsync()).Single();
        category.Name.Should().Be("Books");
        category.Icon.Should().Be("bi-book");
        await audit.Received(1).LogAsync(
            Arg.Any<string>(), "Category", Arg.Any<int?>(), Arg.Any<object?>(), Arg.Any<AuditSeverity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportCategoriesFromCsvAsync_MissingIcon_DefaultsToTagIcon()
    {
        var factory = CreateFactory(nameof(ImportCategoriesFromCsvAsync_MissingIcon_DefaultsToTagIcon));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);
        var stream = CsvStream("Name,Icon", "Books");

        var result = await sut.ImportCategoriesFromCsvAsync(stream, warehouseId: 1, userId: 1);

        result.SuccessCount.Should().Be(1);
        await using var verify = factory.CreateDbContext();
        (await verify.Categories.ToListAsync()).Single().Icon.Should().Be("bi-tag");
    }

    [Fact]
    public async Task ImportCategoriesFromCsvAsync_EmptyFile_ReturnsError()
    {
        var factory = CreateFactory(nameof(ImportCategoriesFromCsvAsync_EmptyFile_ReturnsError));
        var sut = Build(factory);
        var stream = CsvStream("");

        var result = await sut.ImportCategoriesFromCsvAsync(stream, warehouseId: 1, userId: 1);

        result.Errors.Should().ContainSingle().Which.Should().Contain("leer");
    }

    [Fact]
    public async Task ImportCategoriesFromCsvAsync_MissingName_RecordsError()
    {
        var factory = CreateFactory(nameof(ImportCategoriesFromCsvAsync_MissingName_RecordsError));
        var sut = Build(factory);
        var stream = CsvStream("Name,Icon", ",bi-book");

        var result = await sut.ImportCategoriesFromCsvAsync(stream, warehouseId: 1, userId: 1);

        result.FailedCount.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("Name ist erforderlich");
    }

    [Fact]
    public async Task ImportCategoriesFromCsvAsync_DuplicateName_RecordsWarning()
    {
        var factory = CreateFactory(nameof(ImportCategoriesFromCsvAsync_DuplicateName_RecordsWarning));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.Categories.Add(new Category { Id = 1, Name = "Books", WarehouseId = 1 });
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);
        var stream = CsvStream("Name,Icon", "Books,bi-book");

        var result = await sut.ImportCategoriesFromCsvAsync(stream, warehouseId: 1, userId: 1);

        result.Warnings.Should().ContainSingle().Which.Should().Contain("existiert bereits");
        result.FailedCount.Should().Be(1);
    }

    [Fact]
    public async Task ImportCategoriesFromCsvAsync_UnreadableStream_RecordsFileLevelError()
    {
        var factory = CreateFactory(nameof(ImportCategoriesFromCsvAsync_UnreadableStream_RecordsFileLevelError));
        var sut = Build(factory);

        var result = await sut.ImportCategoriesFromCsvAsync(new ThrowingStream(), warehouseId: 1, userId: 1);

        result.Errors.Should().ContainSingle().Which.Should().Contain("Fehler beim Lesen der Datei");
    }

    // ---- ImportProductsFromExcelAsync ----------------------------------------------

    [Fact]
    public async Task ImportProductsFromExcelAsync_ValidRow_CreatesProductAndCategory()
    {
        var factory = CreateFactory(nameof(ImportProductsFromExcelAsync_ValidRow_CreatesProductAndCategory));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);
        var stream = ExcelStream(
            ["Name", "Beschreibung", "Barcode", "Kategorie", "Menge", "Mindestbestand"],
            [["Widget", "Desc", "123", "Electronics", 10, 2]]);

        var result = await sut.ImportProductsFromExcelAsync(stream, warehouseId: 1, userId: 1);

        result.SuccessCount.Should().Be(1);
        await using var verify = factory.CreateDbContext();
        var product = (await verify.Products.ToListAsync()).Single();
        product.Name.Should().Be("Widget");
        product.Quantity.Should().Be(10);
        product.MinQuantity.Should().Be(2);
        (await verify.Categories.ToListAsync()).Single().Name.Should().Be("Electronics");
    }

    [Fact]
    public async Task ImportProductsFromExcelAsync_NonIntegerQuantityCell_DefaultsToZero()
    {
        var factory = CreateFactory(nameof(ImportProductsFromExcelAsync_NonIntegerQuantityCell_DefaultsToZero));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);
        var stream = ExcelStream(
            ["Name", "Beschreibung", "Barcode", "Kategorie", "Menge", "Mindestbestand"],
            [["Widget", "", "", "", "not-a-number", "also-not-a-number"]]);

        var result = await sut.ImportProductsFromExcelAsync(stream, warehouseId: 1, userId: 1);

        result.SuccessCount.Should().Be(1);
        await using var verify = factory.CreateDbContext();
        var product = (await verify.Products.ToListAsync()).Single();
        product.Quantity.Should().Be(0);
        product.MinQuantity.Should().Be(0);
    }

    [Fact]
    public async Task ImportProductsFromExcelAsync_MissingName_RecordsError()
    {
        var factory = CreateFactory(nameof(ImportProductsFromExcelAsync_MissingName_RecordsError));
        var sut = Build(factory);
        var stream = ExcelStream(
            ["Name", "Beschreibung", "Barcode", "Kategorie", "Menge", "Mindestbestand"],
            [["", "Desc", "", "", 1, 1]]);

        var result = await sut.ImportProductsFromExcelAsync(stream, warehouseId: 1, userId: 1);

        result.FailedCount.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("Name ist erforderlich");
    }

    [Fact]
    public async Task ImportProductsFromExcelAsync_DuplicateProductName_RecordsWarning()
    {
        var factory = CreateFactory(nameof(ImportProductsFromExcelAsync_DuplicateProductName_RecordsWarning));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.Categories.Add(new Category { Id = 1, Name = "Cat", WarehouseId = 1 });
            db.Products.Add(new Product { Id = 1, Name = "Widget", CategoryId = 1, WarehouseId = 1, Price = 1 });
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);
        var stream = ExcelStream(
            ["Name", "Beschreibung", "Barcode", "Kategorie", "Menge", "Mindestbestand"],
            [["Widget", "", "", "", 1, 1]]);

        var result = await sut.ImportProductsFromExcelAsync(stream, warehouseId: 1, userId: 1);

        result.Warnings.Should().ContainSingle().Which.Should().Contain("existiert bereits");
        result.FailedCount.Should().Be(1);
    }

    [Fact]
    public async Task ImportProductsFromExcelAsync_NoDataRows_RecordsError()
    {
        var factory = CreateFactory(nameof(ImportProductsFromExcelAsync_NoDataRows_RecordsError));
        var sut = Build(factory);
        var stream = ExcelStream(
            ["Name", "Beschreibung", "Barcode", "Kategorie", "Menge", "Mindestbestand"],
            []);

        var result = await sut.ImportProductsFromExcelAsync(stream, warehouseId: 1, userId: 1);

        result.Errors.Should().ContainSingle().Which.Should().Contain("keine Daten");
        result.SuccessCount.Should().Be(0);
    }

    [Fact]
    public async Task ImportProductsFromExcelAsync_NotAnExcelFile_RecordsFileLevelError()
    {
        var factory = CreateFactory(nameof(ImportProductsFromExcelAsync_NotAnExcelFile_RecordsFileLevelError));
        var sut = Build(factory);
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("this is not a real xlsx file"));

        var result = await sut.ImportProductsFromExcelAsync(stream, warehouseId: 1, userId: 1);

        result.Errors.Should().ContainSingle().Which.Should().Contain("Fehler beim Lesen der Excel-Datei");
    }

    // ---- ImportCategoriesFromExcelAsync --------------------------------------------

    [Fact]
    public async Task ImportCategoriesFromExcelAsync_ValidRow_CreatesCategory()
    {
        var factory = CreateFactory(nameof(ImportCategoriesFromExcelAsync_ValidRow_CreatesCategory));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);
        var stream = ExcelStream(["Name", "Icon"], [["Books", "bi-book"]]);

        var result = await sut.ImportCategoriesFromExcelAsync(stream, warehouseId: 1, userId: 1);

        result.SuccessCount.Should().Be(1);
        await using var verify = factory.CreateDbContext();
        var category = (await verify.Categories.ToListAsync()).Single();
        category.Name.Should().Be("Books");
        category.Icon.Should().Be("bi-book");
    }

    [Fact]
    public async Task ImportCategoriesFromExcelAsync_MissingIcon_DefaultsToTagIcon()
    {
        var factory = CreateFactory(nameof(ImportCategoriesFromExcelAsync_MissingIcon_DefaultsToTagIcon));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);
        var stream = ExcelStream(["Name", "Icon"], [["Books", ""]]);

        var result = await sut.ImportCategoriesFromExcelAsync(stream, warehouseId: 1, userId: 1);

        result.SuccessCount.Should().Be(1);
        await using var verify = factory.CreateDbContext();
        (await verify.Categories.ToListAsync()).Single().Icon.Should().Be("bi-tag");
    }

    [Fact]
    public async Task ImportCategoriesFromExcelAsync_MissingName_RecordsError()
    {
        var factory = CreateFactory(nameof(ImportCategoriesFromExcelAsync_MissingName_RecordsError));
        var sut = Build(factory);
        var stream = ExcelStream(["Name", "Icon"], [["", "bi-book"]]);

        var result = await sut.ImportCategoriesFromExcelAsync(stream, warehouseId: 1, userId: 1);

        result.FailedCount.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("Name ist erforderlich");
    }

    [Fact]
    public async Task ImportCategoriesFromExcelAsync_DuplicateName_RecordsWarning()
    {
        var factory = CreateFactory(nameof(ImportCategoriesFromExcelAsync_DuplicateName_RecordsWarning));
        await using (var db = factory.CreateDbContext())
        {
            db.Warehouses.Add(MakeWarehouse(1));
            db.Categories.Add(new Category { Id = 1, Name = "Books", WarehouseId = 1 });
            await db.SaveChangesAsync();
        }
        var sut = Build(factory);
        var stream = ExcelStream(["Name", "Icon"], [["Books", "bi-book"]]);

        var result = await sut.ImportCategoriesFromExcelAsync(stream, warehouseId: 1, userId: 1);

        result.Warnings.Should().ContainSingle().Which.Should().Contain("existiert bereits");
    }

    [Fact]
    public async Task ImportCategoriesFromExcelAsync_NoDataRows_RecordsError()
    {
        var factory = CreateFactory(nameof(ImportCategoriesFromExcelAsync_NoDataRows_RecordsError));
        var sut = Build(factory);
        var stream = ExcelStream(["Name", "Icon"], []);

        var result = await sut.ImportCategoriesFromExcelAsync(stream, warehouseId: 1, userId: 1);

        result.Errors.Should().ContainSingle().Which.Should().Contain("keine Daten");
    }

    [Fact]
    public async Task ImportCategoriesFromExcelAsync_NotAnExcelFile_RecordsFileLevelError()
    {
        var factory = CreateFactory(nameof(ImportCategoriesFromExcelAsync_NotAnExcelFile_RecordsFileLevelError));
        var sut = Build(factory);
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("garbage"));

        var result = await sut.ImportCategoriesFromExcelAsync(stream, warehouseId: 1, userId: 1);

        result.Errors.Should().ContainSingle().Which.Should().Contain("Fehler beim Lesen der Excel-Datei");
    }
}
