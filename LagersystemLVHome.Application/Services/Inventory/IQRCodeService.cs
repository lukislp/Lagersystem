namespace LagersystemLVHome.Application.Services;

public interface IQRCodeService
{
    string GenerateQRCode(string content, int size = 300);

    byte[] GenerateQRCodeBytes(string content, int size = 300);

    string GenerateStorageLocationQRCode(int locationId, string locationCode, int size = 300);
}
