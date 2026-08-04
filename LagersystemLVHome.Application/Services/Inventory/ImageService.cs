using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Hosting;
using SkiaSharp;

namespace LagersystemLVHome.Application.Services;

public interface IImageService
{
    Task<(string imageUrl, string thumbnailUrl)> UploadProductImageAsync(IBrowserFile file, int productId, CancellationToken cancellationToken = default);
    Task DeleteProductImageAsync(string imageUrl, string thumbnailUrl, CancellationToken cancellationToken = default);

    // Profile image upload (GDPR compliant)
    Task<string> UploadProfileImageAsync(IBrowserFile file, CancellationToken cancellationToken = default);
    Task<bool> DeleteProfileImageAsync(string imagePath, CancellationToken cancellationToken = default);

    // Specification PDF upload
    Task<string> UploadSpecificationPdfAsync(IBrowserFile file, int productId, CancellationToken cancellationToken = default);
    Task<bool> DeleteSpecificationPdfAsync(string pdfPath, CancellationToken cancellationToken = default);

    string GetImagePath(string imageUrl);
    bool ImageExists(string imageUrl);
}

public sealed class ImageService : IImageService
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<ImageService> _logger;
    private const int MaxFileSize = 5 * 1024 * 1024; // 5 MB
    private const int ThumbnailWidth = 150;
    private const int ThumbnailHeight = 150;
    private const int ImageMaxWidth = 800;
    private const int ImageMaxHeight = 800;

    // Magic bytes for file format validation (prevents disguised files)
    private static readonly Dictionary<string, byte[][]> _fileSignatures = new()
    {
        { ".jpg", [new byte[] { 0xFF, 0xD8, 0xFF }] },
        { ".jpeg", [new byte[] { 0xFF, 0xD8, 0xFF }] },
        { ".png", [new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }] },
        { ".webp", [new byte[] { 0x52, 0x49, 0x46, 0x46 }] }, // RIFF header
        { ".pdf", [new byte[] { 0x25, 0x50, 0x44, 0x46 }] }  // %PDF
    };

    public ImageService(IWebHostEnvironment environment, ILogger<ImageService> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public async Task<(string imageUrl, string thumbnailUrl)> UploadProductImageAsync(IBrowserFile file, int productId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (file.Size > MaxFileSize)
            {
                throw new InvalidOperationException($"Datei ist zu gro\u00df. Maximum: {MaxFileSize / 1024 / 1024} MB");
            }

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var extension = Path.GetExtension(file.Name).ToLowerInvariant();
            if (!Array.Exists(allowedExtensions, ext => ext == extension))
            {
                throw new InvalidOperationException("Ung\u00fcltiges Dateiformat. Erlaubt: JPG, PNG, WebP");
            }

            // Create upload directory
            var uploadsPath = Path.Combine(_environment.WebRootPath, "uploads", "products");
            Directory.CreateDirectory(uploadsPath);

            // Unique filename
            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var fileName = $"product_{productId}_{timestamp}{extension}";
            var thumbnailFileName = $"product_{productId}_{timestamp}_thumb{extension}";

            var imagePath = Path.Combine(uploadsPath, fileName);
            var thumbnailPath = Path.Combine(uploadsPath, thumbnailFileName);

            // Load and compress image
            using var stream = file.OpenReadStream(MaxFileSize);
            // Copy stream to MemoryStream for synchronous SkiaSharp operations
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            // Validate magic bytes (prevents disguised files)
            ValidateFileSignature(memoryStream, extension, file.Name);
            memoryStream.Position = 0;

            using var original = SKBitmap.Decode(memoryStream);

            // Main image (max 800x800)
            var resizedImage = ResizeImage(original, ImageMaxWidth, ImageMaxHeight);
            SaveImage(resizedImage, imagePath, 85);

            // Thumbnail (150x150)
            var thumbnail = ResizeImage(original, ThumbnailWidth, ThumbnailHeight);
            SaveImage(thumbnail, thumbnailPath, 80);

            // Return relative URLs
            var imageUrl = $"/uploads/products/{fileName}";
            var thumbnailUrl = $"/uploads/products/{thumbnailFileName}";

            _logger.LogInformation("Image uploaded successfully: {ImageUrl}", imageUrl);

            return (imageUrl, thumbnailUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading product image");
            throw;
        }
    }

    public async Task DeleteProductImageAsync(string imageUrl, string thumbnailUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!string.IsNullOrEmpty(imageUrl))
            {
                var imagePath = GetImagePath(imageUrl);
                if (File.Exists(imagePath))
                {
                    File.Delete(imagePath);
                    _logger.LogInformation("Deleted image: {ImagePath}", imagePath);
                }
            }

            if (!string.IsNullOrEmpty(thumbnailUrl))
            {
                var thumbnailPath = GetImagePath(thumbnailUrl);
                if (File.Exists(thumbnailPath))
                {
                    File.Delete(thumbnailPath);
                    _logger.LogInformation("Deleted thumbnail: {ThumbnailPath}", thumbnailPath);
                }
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting product images");
        }
    }

    public string GetImagePath(string imageUrl)
    {
        if (string.IsNullOrEmpty(imageUrl))
            return string.Empty;

        // Remove leading slash and convert to physical path
        var relativePath = imageUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(_environment.WebRootPath, relativePath);
    }

    public bool ImageExists(string imageUrl)
    {
        if (string.IsNullOrEmpty(imageUrl))
            return false;

        var path = GetImagePath(imageUrl);
        return File.Exists(path);
    }

    // Profile image upload (GDPR compliant, stored in filesystem)
    public async Task<string> UploadProfileImageAsync(IBrowserFile file, CancellationToken cancellationToken = default)
    {
        try
        {
            const int maxProfileImageSize = 50 * 1024 * 1024; // 50 MB
            if (file.Size > maxProfileImageSize)
            {
                throw new InvalidOperationException("Profilbild zu gro\u00df. Maximum: 50 MB");
            }

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var extension = Path.GetExtension(file.Name).ToLowerInvariant();
            if (!Array.Exists(allowedExtensions, ext => ext == extension))
            {
                throw new InvalidOperationException("Ung\u00fcltiges Dateiformat. Erlaubt: JPG, PNG, WebP");
            }

            // Create upload directory
            var uploadsPath = Path.Combine(_environment.WebRootPath, "uploads", "profiles");
            Directory.CreateDirectory(uploadsPath);

            // GDPR: unique filename (no user reference)
            var fileName = $"{Guid.NewGuid()}{extension}";
            var imagePath = Path.Combine(uploadsPath, fileName);

            // Load and compress to 200x200 thumbnail
            using var stream = file.OpenReadStream(maxProfileImageSize);
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            // Validate magic bytes (prevents disguised files)
            ValidateFileSignature(memoryStream, extension, file.Name);
            memoryStream.Position = 0;

            using var original = SKBitmap.Decode(memoryStream);

            // Profile image: square 200x200 thumbnail
            var thumbnail = ResizeImageSquare(original, 200);
            SaveImage(thumbnail, imagePath, 90);

            // Return relative URL
            var imageUrl = $"/uploads/profiles/{fileName}";

            _logger.LogInformation("Profile image uploaded successfully: {ImageUrl}", imageUrl);

            return imageUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading profile image");
            throw;
        }
    }

    // GDPR: delete profile image (data minimization)
    public async Task<bool> DeleteProfileImageAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrEmpty(imagePath))
                return false;

            var physicalPath = GetImagePath(imagePath);
            if (File.Exists(physicalPath))
            {
                File.Delete(physicalPath);
                _logger.LogInformation("Deleted profile image: {ImagePath}", physicalPath);
                return true;
            }

            await Task.CompletedTask;
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting profile image");
            return false;
        }
    }

    // Specification PDF upload
    public async Task<string> UploadSpecificationPdfAsync(IBrowserFile file, int productId, CancellationToken cancellationToken = default)
    {
        try
        {
            const int maxPdfSize = 10 * 1024 * 1024; // 10 MB
            if (file.Size > maxPdfSize)
            {
                throw new InvalidOperationException("PDF zu gro\u00df. Maximum: 10 MB");
            }

            var extension = Path.GetExtension(file.Name).ToLowerInvariant();
            if (extension != ".pdf")
            {
                throw new InvalidOperationException("Nur PDF-Dateien sind erlaubt");
            }

            // Create upload directory
            var uploadsPath = Path.Combine(_environment.WebRootPath, "uploads", "specifications");
            Directory.CreateDirectory(uploadsPath);

            // Unique filename
            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var fileName = $"spec_product_{productId}_{timestamp}.pdf";
            var filePath = Path.Combine(uploadsPath, fileName);

            // Save PDF
            using var stream = file.OpenReadStream(maxPdfSize);
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            // Validate magic bytes (prevents disguised files)
            ValidateFileSignature(memoryStream, extension, file.Name);
            memoryStream.Position = 0;

            using var fileStream = File.Create(filePath);
            await memoryStream.CopyToAsync(fileStream);

            // Return relative URL
            var pdfUrl = $"/uploads/specifications/{fileName}";

            _logger.LogInformation("Specification PDF uploaded successfully: {PdfUrl}", pdfUrl);

            return pdfUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading specification PDF");
            throw;
        }
    }

    // Delete specification PDF
    public async Task<bool> DeleteSpecificationPdfAsync(string pdfPath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrEmpty(pdfPath))
                return false;

            var physicalPath = GetImagePath(pdfPath);
            if (File.Exists(physicalPath))
            {
                File.Delete(physicalPath);
                _logger.LogInformation("Deleted specification PDF: {PdfPath}", physicalPath);
                return true;
            }

            await Task.CompletedTask;
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting specification PDF");
            return false;
        }
    }

    // Helper: create square image (for avatars)
    private SKBitmap ResizeImageSquare(SKBitmap original, int size)
    {
        // Crop to square
        var minDimension = Math.Min(original.Width, original.Height);
        var x = (original.Width - minDimension) / 2;
        var y = (original.Height - minDimension) / 2;

        var square = new SKBitmap(minDimension, minDimension);
        using var canvas = new SKCanvas(square);
        canvas.DrawBitmap(original,
            new SKRect(x, y, x + minDimension, y + minDimension),
            new SKRect(0, 0, minDimension, minDimension));

        // Resize to target size
        var resized = square.Resize(new SKImageInfo(size, size), SKFilterQuality.High);
        return resized ?? square;
    }

    private SKBitmap ResizeImage(SKBitmap original, int maxWidth, int maxHeight)
    {
        var ratioX = (double)maxWidth / original.Width;
        var ratioY = (double)maxHeight / original.Height;
        var ratio = Math.Min(ratioX, ratioY);

        var newWidth = (int)(original.Width * ratio);
        var newHeight = (int)(original.Height * ratio);

        var resized = original.Resize(new SKImageInfo(newWidth, newHeight), SKFilterQuality.High);
        return resized ?? original;
    }

    private void SaveImage(SKBitmap bitmap, string path, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        using var stream = File.OpenWrite(path);
        data.SaveTo(stream);
    }

    /// <summary>
    /// Validates the magic bytes of a file against the expected file extension.
    /// Prevents disguised files (e.g. scripts uploaded as .jpg).
    /// </summary>
    private void ValidateFileSignature(MemoryStream stream, string extension, string fileName)
    {
        if (!_fileSignatures.TryGetValue(extension, out var signatures))
        {
            throw new InvalidOperationException($"Keine Signatur-Validierung f\u00fcr {extension} definiert");
        }

        var headerBytes = new byte[signatures.Max(s => s.Length)];
        var bytesRead = stream.Read(headerBytes, 0, headerBytes.Length);

        var isValid = signatures.Any(signature =>
            bytesRead >= signature.Length &&
            headerBytes.AsSpan(0, signature.Length).SequenceEqual(signature));

        // WebP: additionally check "WEBP" at offset 8 (RIFF....WEBP)
        if (isValid && extension == ".webp")
        {
            if (bytesRead >= 12)
            {
                stream.Position = 8;
                var webpMarker = new byte[4];
                stream.Read(webpMarker, 0, 4);
                isValid = webpMarker.AsSpan().SequenceEqual("WEBP"u8);
            }
            else
            {
                isValid = false;
            }
        }

        if (!isValid)
        {
            _logger.LogWarning("Invalid file signature detected: {FileName} (expected: {Extension})", fileName, extension);
            throw new InvalidOperationException(
                $"Ung\u00fcltige Datei: Der Inhalt von '{fileName}' entspricht nicht dem erwarteten Format ({extension})");
        }
    }
}
