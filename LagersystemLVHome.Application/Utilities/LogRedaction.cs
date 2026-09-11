using System.Text;

namespace LagersystemLVHome.Application.Utilities;

/// <summary>
/// Helpers to keep sensitive or user-controlled values out of log output:
/// PII gets masked, secrets get partially masked, and control characters
/// that could be used for log forging (fake log lines via embedded CR/LF)
/// get stripped.
/// </summary>
public static class LogRedaction
{
    private const int MaxLoggedLength = 200;

    /// <summary>
    /// Masks an e-mail address for logging, e.g. "lukas@example.com" -&gt; "l***@example.com".
    /// Returns "&lt;none&gt;" for null/empty/invalid input.
    /// </summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "<none>";

        var atIndex = email.IndexOf('@');
        if (atIndex <= 0 || atIndex == email.Length - 1)
            return "<none>";

        return $"{email[0]}***{email[atIndex..]}";
    }

    /// <summary>
    /// Masks a token/secret for logging, keeping only the first 4 characters, e.g.
    /// "abcd1234efgh" -&gt; "abcd...". Returns "&lt;none&gt;" for null/empty input.
    /// </summary>
    public static string MaskToken(string? token)
    {
        if (string.IsNullOrEmpty(token))
            return "<none>";

        return token.Length <= 4 ? "****" : $"{token[..4]}...";
    }

    /// <summary>
    /// Sanitizes a user-controlled value (IP, user agent, header, cookie value, etc.) so it
    /// is safe to embed in a log message: strips CR/LF and other control characters that
    /// could be used to forge fake log lines, and caps the length to avoid log flooding.
    /// Returns "&lt;none&gt;" for null/empty input.
    /// </summary>
    public static string ForLog(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "<none>";

        var builder = new StringBuilder(Math.Min(value.Length, MaxLoggedLength));
        foreach (var c in value)
        {
            if (builder.Length >= MaxLoggedLength)
                break;

            // Control characters (includes CR/LF) are stripped rather than replaced,
            // so an attacker can't use them to inject fake log lines or entries.
            if (!char.IsControl(c))
                builder.Append(c);
        }

        var result = builder.ToString();
        return value.Length > MaxLoggedLength ? result + "..." : result;
    }

    /// <summary>
    /// Sanitizes and masks a secret-like value that is also user-controlled (session ids,
    /// cookie values): first strips control characters via <see cref="ForLog"/>, then masks
    /// it, keeping only the first 8 characters, e.g. "abcdefgh12345678" -&gt; "abcdefgh...".
    /// Returns "&lt;none&gt;" for null/empty input.
    /// </summary>
    public static string MaskSecret(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "<none>";

        var sanitized = ForLog(value);
        return sanitized.Length <= 8 ? "****" : $"{sanitized[..8]}...";
    }
}
