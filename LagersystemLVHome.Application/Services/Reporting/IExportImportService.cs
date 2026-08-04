namespace LagersystemLVHome.Application.Services;

public interface IExportService
{
    // Excel Export
    Task<byte[]> ExportProductsToExcelAsync(int warehouseId, CancellationToken cancellationToken = default);
    Task<byte[]> ExportCategoriesToExcelAsync(int warehouseId, CancellationToken cancellationToken = default);
    Task<byte[]> ExportMovementsToExcelAsync(int warehouseId, DateTime? from, DateTime? to, CancellationToken cancellationToken = default);
    Task<byte[]> ExportStorageLocationsToExcelAsync(int warehouseId, CancellationToken cancellationToken = default);

    // CSV Export
    Task<string> ExportProductsToCsvAsync(int warehouseId, CancellationToken cancellationToken = default);
    Task<string> ExportCategoriesToCsvAsync(int warehouseId, CancellationToken cancellationToken = default);
    Task<string> ExportMovementsToCsvAsync(int warehouseId, DateTime? from, DateTime? to, CancellationToken cancellationToken = default);

    // PDF Export
    Task<byte[]> GenerateInventoryReportPdfAsync(int warehouseId, CancellationToken cancellationToken = default);
    Task<byte[]> GenerateStockMovementReportPdfAsync(int warehouseId, DateTime from, DateTime to, CancellationToken cancellationToken = default);
}

public interface IImportService
{
    // CSV Import
    Task<ImportResult> ImportProductsFromCsvAsync(Stream fileStream, int warehouseId, int userId, CancellationToken cancellationToken = default);
    Task<ImportResult> ImportCategoriesFromCsvAsync(Stream fileStream, int warehouseId, int userId, CancellationToken cancellationToken = default);

    // Excel Import
    Task<ImportResult> ImportProductsFromExcelAsync(Stream fileStream, int warehouseId, int userId, CancellationToken cancellationToken = default);
    Task<ImportResult> ImportCategoriesFromExcelAsync(Stream fileStream, int warehouseId, int userId, CancellationToken cancellationToken = default);
}

public sealed class ImportResult
{
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public bool HasErrors => Errors.Any();
    public bool HasWarnings => Warnings.Any();
}
