namespace LagersystemLVHome.UnitTests.Services.Auth;

public class TwoFactorMethodsTests
{
    [Fact]
    public void Constants_HaveExpectedValues()
    {
        TwoFactorMethods.Authenticator.Should().Be("Authenticator");
        TwoFactorMethods.EmailOtp.Should().Be("EmailOtp");
    }

    [Theory]
    [InlineData("Authenticator", true)]
    [InlineData("EmailOtp", true)]
    [InlineData("authenticator", false)] // case-sensitive
    [InlineData("Sms", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsKnown_MatchesOnlyKnownCanonicalValues(string? method, bool expected)
    {
        TwoFactorMethods.IsKnown(method).Should().Be(expected);
    }
}
