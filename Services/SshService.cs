using Renci.SshNet;
using ServerControlCenter.Models;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ServerControlCenter.Services;

public sealed class SshService : IDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ShellReadTimeout = TimeSpan.FromSeconds(8);
    private static readonly Regex AnsiRegex = new(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled);
    private static readonly Regex OscRegex = new(@"\x1B\].*?(?:\x07|\x1B\\)", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex OtherEscapeRegex = new(@"\x1B[@-_]", RegexOptions.Compiled);
    private static readonly Regex PromptLineRegex = new(@"^.*@.*[:].*[#>$]\s*$", RegexOptions.Compiled);

    private SshClient? client;
    private ShellStream? shell;
    private string? shellServerKey;

    public void ConnectShell(ServerProfile server)
    {
        var serverKey = CreateServerKey(server);

        if (client?.IsConnected == true && shell != null && shellServerKey == serverKey)
        {
            return;
        }

        DisconnectShell();
        client = CreateClient(server);
        client.Connect();
        shell = client.CreateShellStream("dumb", 120, 32, 1200, 800, 4096);
        shellServerKey = serverKey;
        _ = ReadAvailableShellOutput(TimeSpan.FromMilliseconds(600));
        shell.WriteLine("export TERM=dumb; unset PROMPT_COMMAND; PS1=; stty -echo 2>/dev/null");
        _ = ReadAvailableShellOutput(TimeSpan.FromMilliseconds(600));
    }

    public void DisconnectShell()
    {
        shell?.Dispose();
        shell = null;

        if (client?.IsConnected == true)
        {
            client.Disconnect();
        }

        client?.Dispose();
        client = null;
        shellServerKey = null;
    }

    public OperationResult SendShellCommand(string command)
    {
        if (shell == null)
        {
            return OperationResult.Failure(AppServices.Localizer.T("NoConnection"));
        }

        if (string.IsNullOrWhiteSpace(command))
        {
            return OperationResult.Failure(AppServices.Localizer.T("EnterCommand"));
        }

        shell.WriteLine(command);
        var rawOutput = ReadAvailableShellOutput(ShellReadTimeout);
        var output = CleanTerminalOutput(rawOutput, command);
        return OperationResult.Success(output, output);
    }

    public async Task<OperationResult> TestConnectionAsync(
        ServerProfile server,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var sshClient = CreateClient(server);
            await sshClient.ConnectAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var succeeded = sshClient.IsConnected;
            var message = AppServices.Localizer.T(succeeded ? "ConnectionSuccessful" : "ConnectionFailed");
            return succeeded ? OperationResult.Success(message) : OperationResult.Failure(message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OperationResult.Failure(AppServices.Localizer.Format("SshError", ex.Message));
        }
    }

    public async Task<OperationResult> RunCommandAsync(
        ServerProfile server,
        string command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return OperationResult.Failure(AppServices.Localizer.T("EnterCommand"));
        }

        try
        {
            using var sshClient = CreateClient(server);
            await sshClient.ConnectAsync(cancellationToken);
            using var sshCommand = sshClient.CreateCommand(command);
            sshCommand.CommandTimeout = CommandTimeout;
            await sshCommand.ExecuteAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var standardOutput = sshCommand.Result?.TrimEnd();
            var standardError = sshCommand.Error?.TrimEnd();
            var output = string.Join(
                Environment.NewLine,
                new[] { standardOutput, standardError }.Where(value => !string.IsNullOrWhiteSpace(value)));
            var exitCode = sshCommand.ExitStatus;

            if (exitCode != 0)
            {
                return OperationResult.Failure(
                    AppServices.Localizer.Format("CommandExitCode", exitCode ?? -1),
                    output,
                    exitCode);
            }

            return OperationResult.Success(AppServices.Localizer.T("CommandCompleted"), output, exitCode);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OperationResult.Failure(AppServices.Localizer.Format("SshError", ex.Message));
        }
    }
    public Task<OperationResult<string>> ReadTextFileAsync(
        ServerProfile server,
        string remotePath,
        CancellationToken cancellationToken = default)
    {
        return RunSftpAsync(server, sftp =>
        {
            ValidateRemotePath(remotePath);
            sftp.Connect();
            var attributes = sftp.GetAttributes(remotePath);

            if (attributes.IsDirectory)
            {
                return OperationResult<string>.Failure(Localized(
                    "File read error: selected path is a folder.",
                    "Ошибка чтения файла: выбран путь к папке."));
            }

            using var stream = new MemoryStream();
            sftp.DownloadFile(remotePath, stream);
            var content = Encoding.UTF8.GetString(stream.ToArray());
            return OperationResult<string>.Success(content, AppServices.Localizer.T("FileLoaded"), content);
        }, "File read error", "Ошибка чтения файла", cancellationToken);
    }

    public Task<OperationResult> SaveTextFileAsync(
        ServerProfile server,
        string remotePath,
        string content,
        CancellationToken cancellationToken = default)
    {
        return RunSftpAsync(server, sftp =>
        {
            ValidateRemotePath(remotePath);
            sftp.Connect();

            if (sftp.Exists(remotePath))
            {
                var backupPath = $"{remotePath}.bak-{DateTime.Now:yyyyMMddHHmmss}";
                using var backupStream = new MemoryStream();
                sftp.DownloadFile(remotePath, backupStream);
                backupStream.Position = 0;
                sftp.UploadFile(backupStream, backupPath, true);
            }

            using var contentStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            sftp.UploadFile(contentStream, remotePath, true);
            return OperationResult.Success(Localized($"File saved: {remotePath}", $"Файл сохранён: {remotePath}"));
        }, "File save error", "Ошибка сохранения файла", cancellationToken);
    }

    public Task<OperationResult> DownloadFileAsync(
        ServerProfile server,
        string remotePath,
        string localPath,
        CancellationToken cancellationToken = default)
    {
        return RunSftpAsync(server, sftp =>
        {
            ValidateRemotePath(remotePath);
            ValidateLocalPath(localPath);
            var directory = Path.GetDirectoryName(localPath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            sftp.Connect();
            using var fileStream = File.Create(localPath);
            sftp.DownloadFile(remotePath, fileStream);
            return OperationResult.Success(Localized($"File downloaded: {localPath}", $"Файл скачан: {localPath}"));
        }, "Download error", "Ошибка скачивания", cancellationToken);
    }

    public Task<OperationResult> UploadFileAsync(
        ServerProfile server,
        string localPath,
        string remotePath,
        CancellationToken cancellationToken = default)
    {
        return RunSftpAsync(server, sftp =>
        {
            ValidateRemotePath(remotePath);
            ValidateLocalPath(localPath);

            if (!File.Exists(localPath))
            {
                return OperationResult.Failure(Localized(
                    $"Upload error: local file not found: {localPath}",
                    $"Ошибка загрузки: локальный файл не найден: {localPath}"));
            }

            sftp.Connect();
            using var fileStream = File.OpenRead(localPath);
            sftp.UploadFile(fileStream, remotePath, true);
            return OperationResult.Success(Localized($"File uploaded: {remotePath}", $"Файл загружен: {remotePath}"));
        }, "Upload error", "Ошибка загрузки", cancellationToken);
    }

    public Task<OperationResult> CreateRemoteDirectoryAsync(
        ServerProfile server,
        string remotePath,
        CancellationToken cancellationToken = default)
    {
        return RunSftpAsync(server, sftp =>
        {
            ValidateRemotePath(remotePath);
            sftp.Connect();
            sftp.CreateDirectory(remotePath);
            return OperationResult.Success(Localized($"Folder created: {remotePath}", $"Папка создана: {remotePath}"));
        }, "Create folder error", "Ошибка создания папки", cancellationToken);
    }

    public Task<OperationResult> RenameRemoteItemAsync(
        ServerProfile server,
        string oldPath,
        string newPath,
        CancellationToken cancellationToken = default)
    {
        return RunSftpAsync(server, sftp =>
        {
            ValidateRemotePath(oldPath);
            ValidateRemotePath(newPath);
            sftp.Connect();
            sftp.RenameFile(oldPath, newPath);
            return OperationResult.Success(Localized($"Renamed: {newPath}", $"Переименовано: {newPath}"));
        }, "Rename error", "Ошибка переименования", cancellationToken);
    }

    public Task<OperationResult> DeleteRemoteItemAsync(
        ServerProfile server,
        string remotePath,
        bool isDirectory,
        CancellationToken cancellationToken = default)
    {
        return RunSftpAsync(server, sftp =>
        {
            ValidateRemotePath(remotePath);
            sftp.Connect();

            if (isDirectory)
            {
                DeleteDirectoryRecursive(sftp, remotePath);
            }
            else
            {
                sftp.DeleteFile(remotePath);
            }

            return OperationResult.Success(Localized($"Deleted: {remotePath}", $"Удалено: {remotePath}"));
        }, "Delete error", "Ошибка удаления", cancellationToken);
    }

    public Task<OperationResult<IReadOnlyList<RemoteFileItem>>> GetFilesAsync(
        ServerProfile server,
        string path,
        CancellationToken cancellationToken = default)
    {
        return RunSftpAsync(server, sftp =>
        {
            ValidateRemotePath(path);
            sftp.Connect();
            IReadOnlyList<RemoteFileItem> files = sftp
                .ListDirectory(path)
                .Where(x => x.Name != "." && x.Name != "..")
                .Select(x => new RemoteFileItem
                {
                    Name = x.Name,
                    FullPath = x.FullName,
                    IsDirectory = x.IsDirectory,
                    Size = x.Attributes.Size,
                    LastWriteTime = x.Attributes.LastWriteTime
                })
                .OrderByDescending(x => x.IsDirectory)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return OperationResult<IReadOnlyList<RemoteFileItem>>.Success(
                files,
                AppServices.Localizer.Format("RemoteFilesLoaded", files.Count));
        }, "SFTP error", "Ошибка SFTP", cancellationToken);
    }

    private static Task<OperationResult> RunSftpAsync(
        ServerProfile server,
        Func<SftpClient, OperationResult> action,
        string englishErrorPrefix,
        string russianErrorPrefix,
        CancellationToken cancellationToken)
    {
        return Task.Run<OperationResult>(() =>
        {
            try
            {
                using var sftp = CreateSftpClient(server);
                using var cancellationRegistration = cancellationToken.Register(sftp.Dispose);
                cancellationToken.ThrowIfCancellationRequested();
                var result = action(sftp);
                cancellationToken.ThrowIfCancellationRequested();
                return result;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            catch (Exception ex)
            {
                return OperationResult.Failure($"{Localized(englishErrorPrefix, russianErrorPrefix)}: {ex.Message}");
            }
        }, cancellationToken);
    }

    private static Task<OperationResult<T>> RunSftpAsync<T>(
        ServerProfile server,
        Func<SftpClient, OperationResult<T>> action,
        string englishErrorPrefix,
        string russianErrorPrefix,
        CancellationToken cancellationToken)
    {
        return Task.Run<OperationResult<T>>(() =>
        {
            try
            {
                using var sftp = CreateSftpClient(server);
                using var cancellationRegistration = cancellationToken.Register(sftp.Dispose);
                cancellationToken.ThrowIfCancellationRequested();
                var result = action(sftp);
                cancellationToken.ThrowIfCancellationRequested();
                return result;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            catch (Exception ex)
            {
                return OperationResult<T>.Failure($"{Localized(englishErrorPrefix, russianErrorPrefix)}: {ex.Message}");
            }
        }, cancellationToken);
    }

    private static void DeleteDirectoryRecursive(SftpClient sftp, string remotePath)
    {
        foreach (var item in sftp.ListDirectory(remotePath).Where(x => x.Name != "." && x.Name != ".."))
        {
            if (item.IsDirectory)
            {
                DeleteDirectoryRecursive(sftp, item.FullName);
            }
            else
            {
                sftp.DeleteFile(item.FullName);
            }
        }

        sftp.DeleteDirectory(remotePath);
    }

    private static SshClient CreateClient(ServerProfile server) => new(CreateConnectionInfo(server))
    {
        KeepAliveInterval = TimeSpan.FromSeconds(30)
    };

    private static SftpClient CreateSftpClient(ServerProfile server) => new(CreateConnectionInfo(server))
    {
        OperationTimeout = CommandTimeout
    };

    private static ConnectionInfo CreateConnectionInfo(ServerProfile server)
    {
        if (string.IsNullOrWhiteSpace(server.Host))
        {
            throw new InvalidOperationException(AppServices.Localizer.T("EnterHost"));
        }

        if (server.Port <= 0 || server.Port > 65535)
        {
            throw new InvalidOperationException(AppServices.Localizer.T("PortRange"));
        }

        if (string.IsNullOrWhiteSpace(server.Username))
        {
            throw new InvalidOperationException(AppServices.Localizer.T("EnterLogin"));
        }

        AuthenticationMethod authMethod;

        if (!string.IsNullOrWhiteSpace(server.PrivateKeyPath))
        {
            if (!File.Exists(server.PrivateKeyPath))
            {
                throw new InvalidOperationException(Localized(
                    $"SSH key not found: {server.PrivateKeyPath}",
                    $"SSH-ключ не найден: {server.PrivateKeyPath}"));
            }

            authMethod = new PrivateKeyAuthenticationMethod(
                server.Username,
                new PrivateKeyFile(server.PrivateKeyPath));
        }
        else
        {
            var password = SecretProtector.Unprotect(server.Password);

            if (string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException(AppServices.Localizer.T("EnterPasswordOrKey"));
            }

            authMethod = new PasswordAuthenticationMethod(server.Username, password);
        }

        return new ConnectionInfo(server.Host, server.Port, server.Username, authMethod)
        {
            Timeout = ConnectTimeout
        };
    }

    private static string CreateServerKey(ServerProfile server) => string.Join(
        '|',
        server.Host.Trim(),
        server.Port.ToString(),
        server.Username.Trim(),
        server.PrivateKeyPath?.Trim() ?? "password");

    private string ReadAvailableShellOutput(TimeSpan timeout)
    {
        if (shell == null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();
        var quietReads = 0;

        while (quietReads < 3 && stopwatch.Elapsed < timeout)
        {
            if (shell.DataAvailable)
            {
                builder.Append(shell.Read());
                quietReads = 0;
                Thread.Sleep(100);
            }
            else
            {
                quietReads++;
                Thread.Sleep(100);
            }
        }

        return builder.ToString();
    }

    private static string CleanTerminalOutput(string output, string command)
    {
        var cleaned = OscRegex.Replace(output, string.Empty);
        cleaned = AnsiRegex.Replace(cleaned, string.Empty);
        cleaned = OtherEscapeRegex.Replace(cleaned, string.Empty);
        cleaned = cleaned.Replace("\r\n", "\n").Replace("\r", "\n");

        var lines = cleaned
            .Split('\n')
            .Select(line => line.TrimEnd())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !string.Equals(line.Trim(), command.Trim(), StringComparison.Ordinal))
            .Where(line => !PromptLineRegex.IsMatch(line.Trim()))
            .ToList();

        return string.Join(Environment.NewLine, lines);
    }

    private static void ValidateRemotePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(AppServices.Localizer.T("EnterPaths"), nameof(path));
        }
    }

    private static void ValidateLocalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(AppServices.Localizer.T("EnterLocalPath"), nameof(path));
        }
    }

    private static string Localized(string english, string russian) =>
        AppServices.Localizer.LanguageCode == "ru" ? russian : english;

    public void Dispose() => DisconnectShell();
}
