using System.Text.Json;

namespace LagersystemLVHome.UnitTests.Services.Auth;

public class TwoFactorServiceTests
{
    private readonly TwoFactorService _sut = new();

    [Fact]
    public void GenerateSecret_Returns16UppercaseHexChars()
    {
        var secret = _sut.GenerateSecret();

        secret.Should().NotBeNullOrEmpty();
        secret.Length.Should().Be(16);
        secret.Should().MatchRegex("^[0-9A-F]{16}$");
    }

    [Fact]
    public void GenerateSecret_IsUniquePerCall()
    {
        var secrets = Enumerable.Range(0, 50).Select(_ => _sut.GenerateSecret()).ToHashSet();
        secrets.Count.Should().Be(50);
    }

    [Fact]
    public void GenerateQrCodeUrl_ReturnsNonEmptyDataUrl()
    {
        var url = _sut.GenerateQrCodeUrl("alice", _sut.GenerateSecret());

        url.Should().NotBeNullOrWhiteSpace();
        url.Should().StartWith("data:image");
    }

    [Theory]
    [InlineData(null, "123456")]
    [InlineData("", "123456")]
    [InlineData("ABCDEFGH12345678", null)]
    [InlineData("ABCDEFGH12345678", "")]
    [InlineData("ABCDEFGH12345678", "12345")]    // too short
    [InlineData("ABCDEFGH12345678", "1234567")]  // too long
    public void ValidateCode_InvalidInputs_ReturnsFalse(string? secret, string? code)
    {
        _sut.ValidateCode(secret!, code!).Should().BeFalse();
    }

    [Fact]
    public void ValidateCode_WrongCode_ReturnsFalse()
    {
        var secret = _sut.GenerateSecret();
        _sut.ValidateCode(secret, "000000").Should().BeFalse();
    }

    [Fact]
    public void GenerateRecoveryCodes_DefaultCount_Returns10FormattedCodes()
    {
        var codes = _sut.GenerateRecoveryCodes();

        codes.Should().HaveCount(10);
        codes.Should().OnlyContain(c => System.Text.RegularExpressions.Regex.IsMatch(c, "^[0-9A-F]{4}-[0-9A-F]{4}$"));
        codes.Distinct().Should().HaveCount(10);
    }

    [Fact]
    public void GenerateRecoveryCodes_CustomCount()
    {
        _sut.GenerateRecoveryCodes(5).Should().HaveCount(5);
        _sut.GenerateRecoveryCodes(0).Should().BeEmpty();
    }

    [Fact]
    public void ValidateRecoveryCode_AcceptsCodeWithOrWithoutHyphenOrSpaces()
    {
        var codes = _sut.GenerateRecoveryCodes(3);
        var json = JsonSerializer.Serialize(codes);
        var raw = codes[0];

        _sut.ValidateRecoveryCode(json, raw).Should().BeTrue();
        _sut.ValidateRecoveryCode(json, raw.Replace("-", "")).Should().BeTrue();
        _sut.ValidateRecoveryCode(json, raw.ToLower()).Should().BeTrue();
        _sut.ValidateRecoveryCode(json, $" {raw} ").Should().BeTrue();
    }

    [Theory]
    [InlineData(null, "AAAA-BBBB")]
    [InlineData("", "AAAA-BBBB")]
    [InlineData("[\"AAAA-BBBB\"]", null)]
    [InlineData("[\"AAAA-BBBB\"]", "")]
    [InlineData("not-json", "AAAA-BBBB")]
    public void ValidateRecoveryCode_InvalidInputs_ReturnFalse(string? json, string? code)
    {
        _sut.ValidateRecoveryCode(json!, code!).Should().BeFalse();
    }

    [Fact]
    public void ValidateRecoveryCode_UnknownCode_ReturnsFalse()
    {
        var json = JsonSerializer.Serialize(new[] { "AAAA-BBBB" });
        _sut.ValidateRecoveryCode(json, "ZZZZ-ZZZZ").Should().BeFalse();
    }

    [Fact]
    public void RemoveUsedRecoveryCode_RemovesMatchingCode()
    {
        var codes = _sut.GenerateRecoveryCodes(3);
        var json = JsonSerializer.Serialize(codes);

        var updatedJson = _sut.RemoveUsedRecoveryCode(json, codes[1]);

        var updated = JsonSerializer.Deserialize<List<string>>(updatedJson)!;
        updated.Should().HaveCount(2);
        updated.Should().NotContain(codes[1]);
    }

    [Fact]
    public void RemoveUsedRecoveryCode_InvalidJson_ReturnsOriginal()
    {
        _sut.RemoveUsedRecoveryCode("not-json", "AAAA-BBBB").Should().Be("not-json");
    }
}
