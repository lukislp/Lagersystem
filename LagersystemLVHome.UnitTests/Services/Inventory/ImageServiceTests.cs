using LagersystemLVHome.Application.Services;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace LagersystemLVHome.UnitTests.Services.Inventory;

public class ImageServiceTests : IDisposable
{
    // Each test gets its own WebRootPath under the OS temp folder so uploads/deletes
    // don't collide across parallel test runs; cleaned up in Dispose.
    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), "lagersystem-imgtests-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_webRoot))
        {
            try { Directory.Delete(_webRoot, recursive: true); } catch { /* best effort cleanup */ }
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>Minimal fake of Blazor's IBrowserFile backed by an in-memory byte buffer.</summary>
    private sealed class FakeBrowserFile(string name, byte[] content, long? sizeOverride = null) : IBrowserFile
    {
        public string Name { get; } = name;
        public DateTimeOffset LastModified { get; } = DateTimeOffset.UtcNow;
        public long Size { get; } = sizeOverride ?? content.Length;
        public string ContentType { get; } = "application/octet-stream";

        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
            => new MemoryStream(content);
    }

    private ImageService CreateSut()
    {
        Directory.CreateDirectory(_webRoot);
        var env = Substitute.For<IWebHostEnvironment>();
        env.WebRootPath.Returns(_webRoot);
        return new ImageService(env, NullLogger<ImageService>.Instance);
    }

    /// <summary>Encodes a small solid-color bitmap using SkiaSharp so tests exercise the
    /// real decode/resize/encode pipeline instead of hand-rolled bytes.</summary>
    private static byte[] MakeImageBytes(SKEncodedImageFormat format, int width = 40, int height = 20, int quality = 90)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(120, 60, 200));
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality);
        return data.ToArray();
    }

    private static (int width, int height) ReadImageDimensions(byte[] bytes)
    {
        using var bitmap = SKBitmap.Decode(bytes);
        return (bitmap.Width, bitmap.Height);
    }

    // ---- UploadProductImageAsync ----

    [Fact]
    public async Task UploadProductImageAsync_ValidPng_SavesMainAndThumbnail_WithinMaxDimensions()
    {
        var sut = CreateSut();
        var bytes = MakeImageBytes(SKEncodedImageFormat.Png, width: 1600, height: 800);
        var file = new FakeBrowserFile("photo.png", bytes);

        var (imageUrl, thumbnailUrl) = await sut.UploadProductImageAsync(file, productId: 7);

        imageUrl.Should().StartWith("/uploads/products/product_7_");
        thumbnailUrl.Should().EndWith("_thumb.png");
        sut.ImageExists(imageUrl).Should().BeTrue();
        sut.ImageExists(thumbnailUrl).Should().BeTrue();

        var mainBytes = await File.ReadAllBytesAsync(sut.GetImagePath(imageUrl));
        var (mw, mh) = ReadImageDimensions(mainBytes);
        mw.Should().BeLessOrEqualTo(800);
        mh.Should().BeLessOrEqualTo(800);

        var thumbBytes = await File.ReadAllBytesAsync(sut.GetImagePath(thumbnailUrl));
        var (tw, th) = ReadImageDimensions(thumbBytes);
        tw.Should().BeLessOrEqualTo(150);
        th.Should().BeLessOrEqualTo(150);
    }

    [Fact]
    public async Task UploadProductImageAsync_ValidJpeg_Succeeds()
    {
        var sut = CreateSut();
        var bytes = MakeImageBytes(SKEncodedImageFormat.Jpeg, width: 300, height: 300);
        var file = new FakeBrowserFile("photo.jpg", bytes);

        var (imageUrl, _) = await sut.UploadProductImageAsync(file, productId: 1);

        sut.ImageExists(imageUrl).Should().BeTrue();
    }

    [Fact]
    public async Task UploadProductImageAsync_FileTooLarge_Throws()
    {
        var sut = CreateSut();
        var file = new FakeBrowserFile("photo.png", content: [1, 2, 3], sizeOverride: 6L * 1024 * 1024);

        var act = async () => await sut.UploadProductImageAsync(file, productId: 1);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*zu gro*");
    }

    [Fact]
    public async Task UploadProductImageAsync_UnsupportedExtension_Throws()
    {
        var sut = CreateSut();
        var file = new FakeBrowserFile("photo.gif", content: [1, 2, 3]);

        var act = async () => await sut.UploadProductImageAsync(file, productId: 1);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Ung*ltiges Dateiformat*");
    }

    [Fact]
    public async Task UploadProductImageAsync_DisguisedFile_FailsSignatureValidation()
    {
        var sut = CreateSut();
        // .png extension but plain-text content: magic bytes won't match the PNG signature.
        var file = new FakeBrowserFile("fake.png", "not really a png"u8.ToArray());

        var act = async () => await sut.UploadProductImageAsync(file, productId: 1);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Ung*ltige Datei*");
    }

    /// <summary>
    /// Regression test for a real bug found by this suite: ValidateFileSignature()
    /// sized its read buffer purely by signature length - for ".webp" that's the
    /// 4-byte RIFF header, so the `bytesRead >= 12` guard for reading the "WEBP"
    /// marker at offset 8 was unconditionally false and EVERY WebP upload was
    /// rejected, even well-formed ones. The buffer is now at least 12 bytes, so a
    /// genuine WebP file passes signature validation (a real encoded WebP is used
    /// so the whole upload path incl. decode/resize succeeds end-to-end).
    /// </summary>
    [Fact]
    public async Task UploadProductImageAsync_Webp_WithValidSignature_PassesValidation()
    {
        var sut = CreateSut();
        var bytes = MakeImageBytes(SKEncodedImageFormat.Webp);
        var file = new FakeBrowserFile("photo.webp", bytes);

        var (imageUrl, thumbnailUrl) = await sut.UploadProductImageAsync(file, productId: 1);

        imageUrl.Should().StartWith("/uploads/products/");
        sut.ImageExists(imageUrl).Should().BeTrue();
        sut.ImageExists(thumbnailUrl).Should().BeTrue();
    }

    /// <summary>A file with a .webp extension whose content is NOT RIFF/WEBP must
    /// still be rejected - the fix widened the buffer, not the validation.</summary>
    [Fact]
    public async Task UploadProductImageAsync_Webp_WithForgedContent_IsRejected()
    {
        var sut = CreateSut();
        var file = new FakeBrowserFile("photo.webp", MakeImageBytes(SKEncodedImageFormat.Png));

        var act = async () => await sut.UploadProductImageAsync(file, productId: 1);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Ung*ltige Datei*");
    }

    // ---- DeleteProductImageAsync ----

    [Fact]
    public async Task DeleteProductImageAsync_ExistingFiles_DeletesBoth()
    {
        var sut = CreateSut();
        var bytes = MakeImageBytes(SKEncodedImageFormat.Png);
        var (imageUrl, thumbnailUrl) = await sut.UploadProductImageAsync(new FakeBrowserFile("p.png", bytes), 1);

        await sut.DeleteProductImageAsync(imageUrl, thumbnailUrl);

        sut.ImageExists(imageUrl).Should().BeFalse();
        sut.ImageExists(thumbnailUrl).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteProductImageAsync_NonExistentFiles_DoesNotThrow()
    {
        var sut = CreateSut();

        var act = async () => await sut.DeleteProductImageAsync("/uploads/products/missing.png", "/uploads/products/missing_thumb.png");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteProductImageAsync_EmptyUrls_NoOp()
    {
        var sut = CreateSut();

        var act = async () => await sut.DeleteProductImageAsync(string.Empty, string.Empty);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteProductImageAsync_LockedFile_SwallowsExceptionAndLeavesFileInPlace()
    {
        var sut = CreateSut();
        var bytes = MakeImageBytes(SKEncodedImageFormat.Png);
        var (imageUrl, thumbnailUrl) = await sut.UploadProductImageAsync(new FakeBrowserFile("p.png", bytes), 1);
        var physicalPath = sut.GetImagePath(imageUrl);

        // Hold an exclusive lock so File.Delete throws IOException inside the service,
        // exercising its catch-and-log branch instead of propagating.
        await using (new FileStream(physicalPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var act = async () => await sut.DeleteProductImageAsync(imageUrl, thumbnailUrl);
            await act.Should().NotThrowAsync();
        }

        File.Exists(physicalPath).Should().BeTrue("the delete attempt should have failed silently while the file was locked");
    }

    // ---- GetImagePath / ImageExists ----

    [Fact]
    public void GetImagePath_EmptyUrl_ReturnsEmptyString()
    {
        CreateSut().GetImagePath(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void GetImagePath_NormalizesLeadingSlashAndSeparators()
    {
        var sut = CreateSut();

        var path = sut.GetImagePath("/uploads/products/x.png");

        path.Should().Be(Path.Combine(_webRoot, "uploads", "products", "x.png"));
    }

    [Fact]
    public void ImageExists_UnknownUrl_ReturnsFalse()
    {
        CreateSut().ImageExists("/uploads/products/nope.png").Should().BeFalse();
    }

    [Fact]
    public void ImageExists_EmptyUrl_ReturnsFalse()
    {
        CreateSut().ImageExists(string.Empty).Should().BeFalse();
    }

    // ---- UploadProfileImageAsync / DeleteProfileImageAsync ----

    [Fact]
    public async Task UploadProfileImageAsync_ValidImage_ProducesSquareThumbnail()
    {
        var sut = CreateSut();
        var bytes = MakeImageBytes(SKEncodedImageFormat.Jpeg, width: 400, height: 100);
        var file = new FakeBrowserFile("avatar.jpg", bytes);

        var imageUrl = await sut.UploadProfileImageAsync(file);

        imageUrl.Should().StartWith("/uploads/profiles/");
        sut.ImageExists(imageUrl).Should().BeTrue();
        var savedBytes = await File.ReadAllBytesAsync(sut.GetImagePath(imageUrl));
        var (w, h) = ReadImageDimensions(savedBytes);
        w.Should().Be(200);
        h.Should().Be(200);
    }

    [Fact]
    public async Task UploadProfileImageAsync_FileTooLarge_Throws()
    {
        var sut = CreateSut();
        var file = new FakeBrowserFile("avatar.jpg", content: [1, 2, 3], sizeOverride: 51L * 1024 * 1024);

        var act = async () => await sut.UploadProfileImageAsync(file);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Profilbild zu gro*");
    }

    [Fact]
    public async Task UploadProfileImageAsync_UnsupportedExtension_Throws()
    {
        var sut = CreateSut();
        var file = new FakeBrowserFile("avatar.bmp", content: [1, 2, 3]);

        var act = async () => await sut.UploadProfileImageAsync(file);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UploadProfileImageAsync_DisguisedFile_FailsSignatureValidation()
    {
        var sut = CreateSut();
        var file = new FakeBrowserFile("avatar.png", "not a real png"u8.ToArray());

        var act = async () => await sut.UploadProfileImageAsync(file);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeleteProfileImageAsync_ExistingFile_DeletesAndReturnsTrue()
    {
        var sut = CreateSut();
        var imageUrl = await sut.UploadProfileImageAsync(new FakeBrowserFile("a.jpg", MakeImageBytes(SKEncodedImageFormat.Jpeg)));

        (await sut.DeleteProfileImageAsync(imageUrl)).Should().BeTrue();
        sut.ImageExists(imageUrl).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteProfileImageAsync_NonExistentFile_ReturnsFalse()
    {
        var sut = CreateSut();

        (await sut.DeleteProfileImageAsync("/uploads/profiles/missing.jpg")).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteProfileImageAsync_EmptyPath_ReturnsFalse()
    {
        var sut = CreateSut();

        (await sut.DeleteProfileImageAsync(string.Empty)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteProfileImageAsync_LockedFile_SwallowsExceptionAndReturnsFalse()
    {
        var sut = CreateSut();
        var imageUrl = await sut.UploadProfileImageAsync(new FakeBrowserFile("a.jpg", MakeImageBytes(SKEncodedImageFormat.Jpeg)));
        var physicalPath = sut.GetImagePath(imageUrl);

        await using (new FileStream(physicalPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            (await sut.DeleteProfileImageAsync(imageUrl)).Should().BeFalse();
        }

        File.Exists(physicalPath).Should().BeTrue();
    }

    // ---- UploadSpecificationPdfAsync / DeleteSpecificationPdfAsync ----

    private static byte[] MakeFakePdfBytes(string body = "fake pdf content")
        => [.. "%PDF-1.4\n"u8.ToArray(), .. System.Text.Encoding.ASCII.GetBytes(body)];

    [Fact]
    public async Task UploadSpecificationPdfAsync_ValidPdf_SavesFileWithMatchingContent()
    {
        var sut = CreateSut();
        var bytes = MakeFakePdfBytes();
        var file = new FakeBrowserFile("spec.pdf", bytes);

        var pdfUrl = await sut.UploadSpecificationPdfAsync(file, productId: 3);

        pdfUrl.Should().StartWith("/uploads/specifications/spec_product_3_").And.EndWith(".pdf");
        sut.ImageExists(pdfUrl).Should().BeTrue();
        var saved = await File.ReadAllBytesAsync(sut.GetImagePath(pdfUrl));
        saved.Should().Equal(bytes);
    }

    [Fact]
    public async Task UploadSpecificationPdfAsync_FileTooLarge_Throws()
    {
        var sut = CreateSut();
        var file = new FakeBrowserFile("spec.pdf", content: [1, 2, 3], sizeOverride: 11L * 1024 * 1024);

        var act = async () => await sut.UploadSpecificationPdfAsync(file, productId: 1);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*PDF zu gro*");
    }

    [Fact]
    public async Task UploadSpecificationPdfAsync_NonPdfExtension_Throws()
    {
        var sut = CreateSut();
        var file = new FakeBrowserFile("spec.docx", content: [1, 2, 3]);

        var act = async () => await sut.UploadSpecificationPdfAsync(file, productId: 1);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Nur PDF-Dateien*");
    }

    [Fact]
    public async Task UploadSpecificationPdfAsync_DisguisedFile_FailsSignatureValidation()
    {
        var sut = CreateSut();
        var file = new FakeBrowserFile("spec.pdf", "not a real pdf"u8.ToArray());

        var act = async () => await sut.UploadSpecificationPdfAsync(file, productId: 1);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*Ung*ltige Datei*");
    }

    [Fact]
    public async Task DeleteSpecificationPdfAsync_ExistingFile_DeletesAndReturnsTrue()
    {
        var sut = CreateSut();
        var pdfUrl = await sut.UploadSpecificationPdfAsync(new FakeBrowserFile("s.pdf", MakeFakePdfBytes()), productId: 1);

        (await sut.DeleteSpecificationPdfAsync(pdfUrl)).Should().BeTrue();
        sut.ImageExists(pdfUrl).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteSpecificationPdfAsync_NonExistentFile_ReturnsFalse()
    {
        var sut = CreateSut();

        (await sut.DeleteSpecificationPdfAsync("/uploads/specifications/missing.pdf")).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteSpecificationPdfAsync_EmptyPath_ReturnsFalse()
    {
        var sut = CreateSut();

        (await sut.DeleteSpecificationPdfAsync(string.Empty)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteSpecificationPdfAsync_LockedFile_SwallowsExceptionAndReturnsFalse()
    {
        var sut = CreateSut();
        var pdfUrl = await sut.UploadSpecificationPdfAsync(new FakeBrowserFile("s.pdf", MakeFakePdfBytes()), productId: 1);
        var physicalPath = sut.GetImagePath(pdfUrl);

        await using (new FileStream(physicalPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            (await sut.DeleteSpecificationPdfAsync(pdfUrl)).Should().BeFalse();
        }

        File.Exists(physicalPath).Should().BeTrue();
    }
}
