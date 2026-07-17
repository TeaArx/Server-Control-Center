using System.Text.Json;
using System.IO;

namespace ServerControlCenter.Services;

public interface IKnownHostStore
{
    HostKeyVerificationResult VerifyOrTrust(string host, int port, string fingerprint);

    IReadOnlyCollection<TrustedHostKey> GetAll();

    bool Forget(string host, int port);
}

public sealed record TrustedHostKey(string Host, int Port, string Fingerprint, DateTime TrustedAtUtc);

public enum HostKeyVerificationResult
{
    Trusted,
    TrustedOnFirstUse,
    Changed
}

public sealed class KnownHostStore : IKnownHostStore
{
    public static KnownHostStore Shared { get; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object sync = new();
    private readonly string filePath;
    private Dictionary<string, TrustedHostKey>? entries;

    public KnownHostStore(string? filePath = null)
    {
        this.filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ServerControlCenter",
            "known_hosts.json");
    }

    public HostKeyVerificationResult VerifyOrTrust(string host, int port, string fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);

        lock (sync)
        {
            var knownHosts = Load();
            var key = CreateKey(host, port);

            if (knownHosts.TryGetValue(key, out var trusted))
            {
                return CryptographicEquals(trusted.Fingerprint, fingerprint)
                    ? HostKeyVerificationResult.Trusted
                    : HostKeyVerificationResult.Changed;
            }

            knownHosts[key] = new TrustedHostKey(host.Trim(), port, fingerprint, DateTime.UtcNow);
            Save(knownHosts);
            return HostKeyVerificationResult.TrustedOnFirstUse;
        }
    }

    public IReadOnlyCollection<TrustedHostKey> GetAll()
    {
        lock (sync)
        {
            return Load().Values.OrderBy(x => x.Host, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Port).ToArray();
        }
    }

    public bool Forget(string host, int port)
    {
        lock (sync)
        {
            var knownHosts = Load();

            if (!knownHosts.Remove(CreateKey(host, port)))
            {
                return false;
            }

            Save(knownHosts);
            return true;
        }
    }

    private Dictionary<string, TrustedHostKey> Load()
    {
        if (entries is not null)
        {
            return entries;
        }

        if (!File.Exists(filePath))
        {
            entries = new(StringComparer.OrdinalIgnoreCase);
            return entries;
        }

        try
        {
            var items = JsonSerializer.Deserialize<List<TrustedHostKey>>(File.ReadAllText(filePath), JsonOptions) ?? [];
            entries = items.ToDictionary(x => CreateKey(x.Host, x.Port), StringComparer.OrdinalIgnoreCase);
            return entries;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Trusted SSH host key store cannot be read: {filePath}. Connections are blocked until it is repaired.",
                ex);
        }
    }

    private void Save(Dictionary<string, TrustedHostKey> knownHosts)
    {
        var directory = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = filePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(knownHosts.Values, JsonOptions));
        File.Move(temporaryPath, filePath, overwrite: true);
    }

    private static string CreateKey(string host, int port) => $"{host.Trim().ToLowerInvariant()}:{port}";

    private static bool CryptographicEquals(string left, string right)
    {
        var leftBytes = System.Text.Encoding.UTF8.GetBytes(left);
        var rightBytes = System.Text.Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
            System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
