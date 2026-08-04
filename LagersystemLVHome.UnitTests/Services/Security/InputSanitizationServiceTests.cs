using LagersystemLVHome.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LagersystemLVHome.UnitTests.Services.Security;

/// <summary>
/// Pure tests for <see cref="InputSanitizationService"/>. The service
/// detects SQL-injection and XSS patterns and provides legacy-style
/// string sanitisation plus HTML-encoding.
/// </summary>
public class InputSanitizationServiceTests
{
    private readonly InputSanitizationService _sut =
        new(NullLogger<InputSanitizationService>.Instance);

    [Theory]
    [InlineData("'; DROP TABLE Users; --")]
    [InlineData("admin' --")]
    [InlineData("1 OR 1=1")]
    [InlineData("SELECT * FROM Users")]
    [InlineData("UNION SELECT password FROM")]
    [InlineData("x'; EXEC sp_executesql")]
    public void IsPotentialSqlInjection_WithMaliciousInput_ReturnsTrue(string input)
    {
        _sut.IsPotentialSqlInjection(input).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("normal text")]
    [InlineData("Apple iPhone 15 Pro")]
    [InlineData("Milk, 1 liter")]
    [InlineData("Screwdriver - small")]
    public void IsPotentialSqlInjection_WithBenignInput_ReturnsFalse(string? input)
    {
        _sut.IsPotentialSqlInjection(input!).Should().BeFalse();
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<SCRIPT SRC=evil.js></SCRIPT>")]
    [InlineData("<iframe src='http://evil'></iframe>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("javascript:alert(1)")]
    [InlineData("<body onload='evil()'>")]
    public void IsPotentialXss_WithMaliciousInput_ReturnsTrue(string input)
    {
        _sut.IsPotentialXss(input).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("plain text")]
    [InlineData("A & B")]
    public void IsPotentialXss_WithBenignInput_ReturnsFalse(string? input)
    {
        _sut.IsPotentialXss(input!).Should().BeFalse();
    }

    [Fact]
    public void SanitizeInput_EscapesSingleQuotes()
    {
        _sut.SanitizeInput("O'Brian").Should().Be("O''Brian");
    }

    [Fact]
    public void SanitizeInput_RemovesCommentMarkersAndSemicolons()
    {
        var r = _sut.SanitizeInput("x--y/*z*/;end");
        r.Should().NotContain("--")
         .And.NotContain("/*")
         .And.NotContain("*/")
         .And.NotContain(";");
    }

    [Fact]
    public void ValidateInput_WithBenignInput_DoesNotThrow()
    {
        Action act = () => _sut.ValidateInput("Apple Juice 1L", "Name");
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateInput_WithInjection_ThrowsSecurityException()
    {
        Action act = () => _sut.ValidateInput("'; DROP TABLE Users; --", "Name");
        act.Should().Throw<SecurityException>().WithMessage("*SQL Injection*Name*");
    }

    [Fact]
    public void ValidateInput_WithXss_ThrowsSecurityException()
    {
        Action act = () => _sut.ValidateInput("<script>alert(1)</script>", "Comment");
        act.Should().Throw<SecurityException>().WithMessage("*XSS*Comment*");
    }

    [Fact]
    public void ValidateInputs_ChecksAllEntries()
    {
        var inputs = new Dictionary<string, string>
        {
            ["Safe"] = "ok",
            ["Bad"] = "<script>x</script>"
        };

        Action act = () => _sut.ValidateInputs(inputs);
        act.Should().Throw<SecurityException>().WithMessage("*Bad*");
    }

    [Fact]
    public void HtmlEncode_EscapesHtmlSpecialCharacters()
    {
        _sut.HtmlEncode("<b>\"&</b>").Should().Be("&lt;b&gt;&quot;&amp;&lt;/b&gt;");
    }

    [Fact]
    public void HtmlEncode_WithEmptyInput_ReturnsInputUnchanged()
    {
        _sut.HtmlEncode("").Should().Be("");
    }
}
