using System.Security.Cryptography;
using System.Text;

namespace ServerControlCenter.Services;

public static class SecretProtector
{
    private const string Prefix = "dpapi:";

    public static string? Protect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (IsProtected(value))
        {
            return value;
        }

        var plainBytes = Encoding.UTF8.GetBytes(value);
        var protectedBytes = ProtectedData.Protect(
            plainBytes,
            optionalEntropy: null,
            scope: DataProtectionScope.CurrentUser
        );

        return Prefix + Convert.ToBase64String(protectedBytes);
    }

    public static string? Unprotect(string? value)
    {
        var result = TryUnprotect(value);
        return result.Status == SecretProtectionStatus.Available ? result.Value : null;
    }

    public static SecretProtectionResult TryUnprotect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new(SecretProtectionStatus.Missing, null);
        }

        if (!IsProtected(value))
        {
            return new(SecretProtectionStatus.Available, value);
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(value[Prefix.Length..]);
            var plainBytes = ProtectedData.Unprotect(
                protectedBytes,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser
            );

            return new(SecretProtectionStatus.Available, Encoding.UTF8.GetString(plainBytes));
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return new(SecretProtectionStatus.Unreadable, null);
        }
    }

    public static bool IsProtected(string value)
    {
        return value.StartsWith(Prefix, StringComparison.Ordinal);
    }
}

public enum SecretProtectionStatus
{
    Missing,
    Available,
    Unreadable
}

public sealed record SecretProtectionResult(SecretProtectionStatus Status, string? Value);
