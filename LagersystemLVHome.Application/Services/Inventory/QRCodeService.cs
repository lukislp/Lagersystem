using QRCoder;
using System.Drawing;
using System.Drawing.Imaging;

namespace LagersystemLVHome.Application.Services;

public sealed class QRCodeService : IQRCodeService
{
    public string GenerateQRCode(string content, int size = 300)
    {
        var bytes = GenerateQRCodeBytes(content, size);
        return Convert.ToBase64String(bytes);
    }

    public byte[] GenerateQRCodeBytes(string content, int size = 300)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(qrCodeData);

        // Calculate pixels per module based on the requested size.
        // QR code modules: ~25-40 depending on content length (typically ~33).
        // size = pixelsPerModule * moduleCount
        // pixelsPerModule = size / 33 (average module count)
        int pixelsPerModule = Math.Max(1, size / 33);

        return qrCode.GetGraphic(pixelsPerModule);
    }

    public string GenerateStorageLocationQRCode(int locationId, string locationCode, int size = 300)
    {
        // QR code payload: use the location code directly.
        return GenerateQRCode(locationCode, size);
    }
}
