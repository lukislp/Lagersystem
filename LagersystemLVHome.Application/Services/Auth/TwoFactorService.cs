using Google.Authenticator;

namespace LagersystemLVHome.Application.Services;

public sealed class TwoFactorService : ITwoFactorService
{
    private readonly TwoFactorAuthenticator _tfa = new();

    public string GenerateSecret()
    {
        return Guid.NewGuid().ToString("N")[..16].ToUpper();
    }

    public string GenerateQrCodeUrl(string username, string secret, string issuer = "LagerSystem")
    {
        var setupInfo = _tfa.GenerateSetupCode(
            issuer,
            username,
            secret,
            false,
            300
        );

        return setupInfo.QrCodeSetupImageUrl;
    }

    public bool ValidateCode(string secret, string code)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(code))
            return false;

        code = code.Replace(" ", "").Trim();

        if (code.Length != 6)
            return false;

        return _tfa.ValidateTwoFactorPIN(secret, code);
    }

    public List<string> GenerateRecoveryCodes(int count = 10)
    {
        var codes = new List<string>();
        for (int i = 0; i < count; i++)
        {
            // Format: XXXX-XXXX (e.g. A3B7-9F2E)
            var code = Guid.NewGuid().ToString("N")[..8].ToUpper();
            var formatted = $"{code[..4]}-{code[4..8]}";
            codes.Add(formatted);
        }
        return codes;
    }

    public bool ValidateRecoveryCode(string recoveryCodesJson, string code)
    {
        if (string.IsNullOrEmpty(recoveryCodesJson) || string.IsNullOrEmpty(code))
            return false;

        try
        {
            var codes = System.Text.Json.JsonSerializer.Deserialize<List<string>>(recoveryCodesJson);
            if (codes == null)
                return false;

            var normalizedInput = code.Replace(" ", "").Replace("-", "").ToUpper();

            return codes.Any(c => c.Replace("-", "").ToUpper() == normalizedInput);
        }
        catch
        {
            return false;
        }
    }

    public string RemoveUsedRecoveryCode(string recoveryCodesJson, string code)
    {
        try
        {
            var codes = System.Text.Json.JsonSerializer.Deserialize<List<string>>(recoveryCodesJson);
            if (codes == null)
                return recoveryCodesJson;

            var normalizedInput = code.Replace(" ", "").Replace("-", "").ToUpper();
            codes.RemoveAll(c => c.Replace("-", "").ToUpper() == normalizedInput);

            return System.Text.Json.JsonSerializer.Serialize(codes);
        }
        catch
        {
            return recoveryCodesJson;
        }
    }
}
