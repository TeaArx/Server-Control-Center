using System.IO;
using ServerControlCenter.Services;
using Xunit;

namespace ServerControlCenter.UnitTests;

public sealed class SecurityServicesTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(Path.GetTempPath(), $"scc-unit-{Guid.NewGuid():N}");

    [Fact]
    public void KnownHostsUsesTofuAndRejectsChangedKeys()
    {
        var store = new KnownHostStore(Path.Combine(tempDirectory, "known_hosts.json"));

        Assert.Equal(HostKeyVerificationResult.TrustedOnFirstUse, store.VerifyOrTrust("example.test", 22, "SHA256:first"));
        Assert.Equal(HostKeyVerificationResult.Trusted, store.VerifyOrTrust("EXAMPLE.TEST", 22, "SHA256:first"));
        Assert.Equal(HostKeyVerificationResult.Changed, store.VerifyOrTrust("example.test", 22, "SHA256:changed"));
        Assert.Single(store.GetAll());
    }

    [Fact]
    public void RedactorRemovesCommonSecretForms()
    {
        var value = "password=hunter2 token: abc123 Authorization: Bearer xyz https://user:secret@example.test";
        var result = SensitiveDataRedactor.Redact(value);

        Assert.DoesNotContain("hunter2", result);
        Assert.DoesNotContain("abc123", result);
        Assert.DoesNotContain("xyz", result);
        Assert.DoesNotContain(":secret@", result);
        Assert.Contains("[REDACTED]", result);
    }

    [Fact]
    public void CorruptProtectedSecretIsNotReportedAsMissing()
    {
        var result = SecretProtector.TryUnprotect("dpapi:not-base64");

        Assert.Equal(SecretProtectionStatus.Unreadable, result.Status);
        Assert.Null(result.Value);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
