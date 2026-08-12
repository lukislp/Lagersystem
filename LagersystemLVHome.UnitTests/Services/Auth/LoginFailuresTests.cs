using System.Reflection;

namespace LagersystemLVHome.UnitTests.Services.Auth;

public class LoginFailuresTests
{
    private static IEnumerable<string> GetAllCodes() =>
        typeof(LoginFailures)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

    [Fact]
    public void AllCodes_AreUnique()
    {
        var codes = GetAllCodes().ToList();

        codes.Should().OnlyHaveUniqueItems();
        codes.Should().NotBeEmpty();
    }

    [Fact]
    public void AllCodes_UseLoginPrefix()
    {
        GetAllCodes().Should().OnlyContain(c => c.StartsWith("login.", StringComparison.Ordinal));
    }

    [Fact]
    public void CoreCodes_HaveExpectedValues()
    {
        LoginFailures.UserNotFound.Should().Be("login.user_not_found");
        LoginFailures.InvalidPassword.Should().Be("login.invalid_password");
        LoginFailures.AccountLocked.Should().Be("login.account_locked");
        LoginFailures.MagicLinkInvalid.Should().Be("login.magic_link_invalid");
    }
}
