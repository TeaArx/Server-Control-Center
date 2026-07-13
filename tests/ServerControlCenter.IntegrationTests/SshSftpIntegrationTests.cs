using Xunit;
using ServerControlCenter.Models;
using ServerControlCenter.Services;

namespace ServerControlCenter.IntegrationTests;

public sealed class SshSftpIntegrationTests : IDisposable
{
    private readonly SshService ssh = new();
    private readonly string host = Environment.GetEnvironmentVariable("SCC_SSH_HOST") ?? "127.0.0.1";
    private readonly int port = int.TryParse(Environment.GetEnvironmentVariable("SCC_SSH_PORT"), out var parsedPort)
        ? parsedPort
        : 2222;
    private readonly string username = Environment.GetEnvironmentVariable("SCC_SSH_USER") ?? "codex";
    private readonly string password = Environment.GetEnvironmentVariable("SCC_SSH_PASSWORD") ?? "codex-test-password";

    [Fact]
    public async Task ConnectsWithPassword()
    {
        var result = await ssh.TestConnectionAsync(PasswordServer());

        Assert.True(result.Succeeded, result.Message);
    }

    [Fact]
    public async Task ConnectsWithPrivateKey()
    {
        var keyPath = RequiredEnvironment("SCC_SSH_KEY_PATH");
        var result = await ssh.TestConnectionAsync(KeyServer(keyPath));

        Assert.True(result.Succeeded, result.Message);
    }

    [Fact]
    public async Task ReportsCommandExitCodeWithoutParsingOutputText()
    {
        var success = await ssh.RunCommandAsync(PasswordServer(), "printf command-ok");
        var failure = await ssh.RunCommandAsync(PasswordServer(), "sh -c 'echo command-failed >&2; exit 7'");

        Assert.True(success.Succeeded, success.Message);
        Assert.Equal("command-ok", success.Output);
        Assert.False(failure.Succeeded);
        Assert.Equal(7, failure.ExitCode);
        Assert.Contains("command-failed", failure.Output);
    }

    [Fact]
    public async Task CreatesUploadsDownloadsReadsSavesRenamesAndDeletes()
    {
        var server = PasswordServer();
        var remoteRoot = $"/config/scc-tests-{Guid.NewGuid():N}";
        var remoteOriginal = $"{remoteRoot}/original.txt";
        var remoteRenamed = $"{remoteRoot}/renamed.txt";
        var localRoot = Path.Combine(Path.GetTempPath(), $"scc-tests-{Guid.NewGuid():N}");
        var uploadPath = Path.Combine(localRoot, "upload.txt");
        var downloadPath = Path.Combine(localRoot, "download.txt");
        Directory.CreateDirectory(localRoot);
        await File.WriteAllTextAsync(uploadPath, "first-version");

        try
        {
            AssertSucceeded(await ssh.CreateRemoteDirectoryAsync(server, remoteRoot));
            AssertSucceeded(await ssh.UploadFileAsync(server, uploadPath, remoteOriginal));

            var files = await ssh.GetFilesAsync(server, remoteRoot);
            Assert.True(files.Succeeded, files.Message);
            Assert.Contains(files.Value!, item => item.Name == "original.txt" && !item.IsDirectory);

            var read = await ssh.ReadTextFileAsync(server, remoteOriginal);
            Assert.True(read.Succeeded, read.Message);
            Assert.Equal("first-version", read.Value);

            AssertSucceeded(await ssh.SaveTextFileAsync(server, remoteOriginal, "second-version"));
            AssertSucceeded(await ssh.RenameRemoteItemAsync(server, remoteOriginal, remoteRenamed));
            AssertSucceeded(await ssh.DownloadFileAsync(server, remoteRenamed, downloadPath));
            Assert.Equal("second-version", await File.ReadAllTextAsync(downloadPath));

            AssertSucceeded(await ssh.DeleteRemoteItemAsync(server, remoteRenamed, false));
            AssertSucceeded(await ssh.DeleteRemoteItemAsync(server, remoteRoot, true));
        }
        finally
        {
            if (Directory.Exists(localRoot))
            {
                Directory.Delete(localRoot, true);
            }
        }
    }

    [Fact]
    public async Task ReturnsFailureForUnavailableServer()
    {
        var unavailable = PasswordServer();
        unavailable.Port = 22999;
        var result = await ssh.TestConnectionAsync(unavailable);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task CancelsLongRunningCommand()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ssh.RunCommandAsync(PasswordServer(), "sleep 20", cancellation.Token));
    }

    private ServerProfile PasswordServer() => new()
    {
        Name = "Integration SSH",
        Host = host,
        Port = port,
        Username = username,
        Password = password
    };

    private ServerProfile KeyServer(string privateKeyPath) => new()
    {
        Name = "Integration SSH key",
        Host = host,
        Port = port,
        Username = username,
        PrivateKeyPath = privateKeyPath
    };

    private static string RequiredEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException($"Set {name} before running integration tests.");

    private static void AssertSucceeded(OperationResult result) => Assert.True(result.Succeeded, result.Message);

    public void Dispose() => ssh.Dispose();
}
