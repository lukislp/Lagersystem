using System.Text.RegularExpressions;

namespace LagersystemLVHome.Application.Services;

/// <summary>
/// Service for input sanitization and security validation.
/// Detects SQL injection and XSS attempts.
/// </summary>
public sealed class InputSanitizationService
{
    private readonly ILogger<InputSanitizationService> _logger;

    private static readonly Regex SqlInjectionPattern = new Regex(
        @"(\b(SELECT|INSERT|UPDATE|DELETE|DROP|CREATE|ALTER|EXEC|EXECUTE|UNION|DECLARE|CAST|CONVERT)\b)|" +
        @"(-{2})|(\/\*)|(\*\/)|" +
        @"(\bOR\b\s+\d+\s*=\s*\d+)|" +
        @"(\bAND\b\s+\d+\s*=\s*\d+)|" +
        @"(';)|" +
        @"(\bxp_)|" +
        @"(\bsp_)|" +
        @"(@@)|" +
        @"(\bCHAR\()|" +
        @"(\bWAITFOR\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex XssPattern = new Regex(
        @"(<script[^>]*>.*?</script>)|" +
        @"(<iframe[^>]*>.*?</iframe>)|" +
        @"(javascript:)|" +
        @"(on\w+\s*=)|" +
        @"(<img[^>]+src[^>]*>)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public InputSanitizationService(ILogger<InputSanitizationService> logger)
    {
        _logger = logger;
    }

    public bool IsPotentialSqlInjection(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var result = SqlInjectionPattern.IsMatch(input);

        if (result)
        {
            _logger.LogWarning("Potential SQL injection detected: {Input}",
                input.Length > 100 ? input[..100] + "..." : input);
        }

        return result;
    }

    public bool IsPotentialXss(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var result = XssPattern.IsMatch(input);

        if (result)
        {
            _logger.LogWarning("Potential XSS attack detected: {Input}",
                input.Length > 100 ? input[..100] + "..." : input);
        }

        return result;
    }

    /// <summary>
    /// Sanitizes input for legacy code paths.
    /// </summary>
    public string SanitizeInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        input = input
            .Replace("'", "''")
            .Replace("--", "")
            .Replace("/*", "")
            .Replace("*/", "")
            .Replace(";", "");

        return input;
    }

    /// <summary>
    /// Validates input and throws an exception if a threat is detected.
    /// </summary>
    public void ValidateInput(string input, string fieldName = "Input")
    {
        if (IsPotentialSqlInjection(input))
        {
            throw new SecurityException($"SQL Injection erkannt in {fieldName}");
        }

        if (IsPotentialXss(input))
        {
            throw new SecurityException($"XSS-Versuch erkannt in {fieldName}");
        }
    }

    public void ValidateInputs(Dictionary<string, string> inputs)
    {
        foreach (var input in inputs)
        {
            ValidateInput(input.Value, input.Key);
        }
    }

    /// <summary>
    /// HTML-encodes a string for safe output.
    /// </summary>
    public string HtmlEncode(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        return System.Net.WebUtility.HtmlEncode(input);
    }
}

public sealed class SecurityException : Exception
{
    public SecurityException(string message) : base(message)
    {
    }
}
