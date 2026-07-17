using System.Text.RegularExpressions;

namespace ServerControlCenter.Services;

public static partial class SensitiveDataRedactor
{
    private const string Redacted = "[REDACTED]";

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        var redacted = AssignmentSecretRegex().Replace(value, match => $"{match.Groups[1].Value}{Redacted}");
        redacted = AuthorizationHeaderRegex().Replace(redacted, match => $"{match.Groups[1].Value}{Redacted}");
        redacted = UriCredentialRegex().Replace(redacted, match => $"{match.Groups[1].Value}{Redacted}@");
        redacted = PrivateKeyRegex().Replace(redacted, Redacted);
        return redacted;
    }

    [GeneratedRegex("(?i)(\\b(?:password|passwd|pwd|token|api[_-]?key|secret)\\s*(?:=|:)\\s*)(?:'[^']*'|\\\"[^\\\"]*\\\"|[^\\s;&|]+)")]
    private static partial Regex AssignmentSecretRegex();

    [GeneratedRegex("(?i)(\\bAuthorization\\s*:\\s*(?:Bearer|Basic)\\s+)[^\\s]+")]
    private static partial Regex AuthorizationHeaderRegex();

    [GeneratedRegex("(?i)(\\b[a-z][a-z0-9+.-]*://[^:/\\s]+:)[^@/\\s]+@")]
    private static partial Regex UriCredentialRegex();

    [GeneratedRegex("-----BEGIN [^-]*PRIVATE KEY-----.*?-----END [^-]*PRIVATE KEY-----", RegexOptions.Singleline)]
    private static partial Regex PrivateKeyRegex();
}
