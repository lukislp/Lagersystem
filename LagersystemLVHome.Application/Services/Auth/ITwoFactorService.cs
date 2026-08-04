using Google.Authenticator;

namespace LagersystemLVHome.Application.Services;

public interface ITwoFactorService
{
    string GenerateSecret();
    string GenerateQrCodeUrl(string username, string secret, string issuer = "LagerSystem");
    bool ValidateCode(string secret, string code);
    List<string> GenerateRecoveryCodes(int count = 10);
    bool ValidateRecoveryCode(string recoveryCodesJson, string code);
    string RemoveUsedRecoveryCode(string recoveryCodesJson, string code);
}
