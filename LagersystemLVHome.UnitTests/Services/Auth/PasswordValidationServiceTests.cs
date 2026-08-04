using LagersystemLVHome.Application.Services;

namespace LagersystemLVHome.UnitTests.Services.Auth;

/// <summary>
/// Pure in-memory tests for <see cref="PasswordValidationService"/>. The
/// service enforces length (8-128), case, digit, special-character rules
/// and computes a 0-100 strength score.
/// </summary>
public class PasswordValidationServiceTests
{
    private readonly PasswordValidationService _sut = new();

    [Theory]
    [InlineData("Password1!")]
    [InlineData("MyStr0ng!Pass")]
    [InlineData("Test@1234")]
    public void ValidatePassword_WithStrongPassword_ReturnsValid(string password)
    {
        var r = _sut.ValidatePassword(password);

        r.IsValid.Should().BeTrue($"errors: {string.Join(", ", r.Errors)}");
        r.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidatePassword_WithNullOrEmpty_ReturnsInvalid(string? password)
    {
        var r = _sut.ValidatePassword(password!);

        r.IsValid.Should().BeFalse();
        r.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void ValidatePassword_TooShort_ReturnsInvalid()
    {
        _sut.ValidatePassword("Ab1!").IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidatePassword_TooLong_ReturnsInvalid()
    {
        var tooLong = new string('A', 60) + new string('b', 60) + "1!" + new string('c', 20);
        _sut.ValidatePassword(tooLong).IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidatePassword_MissingUppercase_ReturnsInvalid()
    {
        var r = _sut.ValidatePassword("password1!");
        r.IsValid.Should().BeFalse();
        r.Errors.Should().ContainMatch("*Gro*buchstaben*");
    }

    [Fact]
    public void ValidatePassword_MissingLowercase_ReturnsInvalid()
    {
        var r = _sut.ValidatePassword("PASSWORD1!");
        r.IsValid.Should().BeFalse();
        r.Errors.Should().ContainMatch("*Kleinbuchstaben*");
    }

    [Fact]
    public void ValidatePassword_MissingDigit_ReturnsInvalid()
    {
        var r = _sut.ValidatePassword("Password!");
        r.IsValid.Should().BeFalse();
        r.Errors.Should().ContainMatch("*Zahl*");
    }

    [Fact]
    public void ValidatePassword_MissingSpecialChar_ReturnsInvalid()
    {
        var r = _sut.ValidatePassword("Password1");
        r.IsValid.Should().BeFalse();
        r.Errors.Should().ContainMatch("*Sonderzeichen*");
    }

    [Fact]
    public void ValidatePassword_WithMultipleIssues_ReturnsAllErrors()
    {
        // too short, no digit, no special, no uppercase
        var r = _sut.ValidatePassword("abc");

        r.IsValid.Should().BeFalse();
        r.Errors.Count.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void CalculatePasswordStrength_EmptyInput_ReturnsZero()
    {
        _sut.CalculatePasswordStrength("").Should().Be(0);
        _sut.CalculatePasswordStrength("   ").Should().Be(0);
    }

    [Fact]
    public void CalculatePasswordStrength_ClampsToHundred()
    {
        var s = _sut.CalculatePasswordStrength("VeryLongStrongP@ssword12345!Foo");
        s.Should().BeInRange(0, 100);
    }

    [Fact]
    public void ValidatePassword_StrengthLevel_TracksScore()
    {
        var weak = _sut.ValidatePassword("short");
        weak.StrengthLevel.Should().BeOneOf(
            PasswordStrengthLevel.VeryWeak, PasswordStrengthLevel.Weak);

        var strong = _sut.ValidatePassword("VeryStrong#Password9");
        strong.StrengthLevel.Should().BeOneOf(
            PasswordStrengthLevel.Strong, PasswordStrengthLevel.VeryStrong);
    }
}
