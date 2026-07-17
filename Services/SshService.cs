using Renci.SshNet;
using Renci.SshNet.Common;
using ServerControlCenter.Models;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

namespace ServerControlCenter.Services;

public sealed class SshService : IDisposable
{
    public const long MaxEditableTextFileBytes = 2 * 1024 * 1024;

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);
    private static readonly Regex AnsiRegex = new(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled);
    private static readonly Regex OscRegex = new(@"\x1B\].*?(?:\x07|\x1B\\)", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex OtherEscapeRegex = new(@"\x1B[@-_]", RegexOptions.Compiled);
    private static readonly Regex PromptLineRegex = new(@"^.*@.*[:].*[#>$]\s*$", RegexOptions.Compiled);

    private SshClient? client;
    private ShellStream? shell;
    private string? shellServerKey;
    private readonly IKnownHostStore knownHosts;

    public SshService(IKnownHostStore? knownHosts = null)
    {
        this.knownHosts = knownHosts ?? KnownHostStore.Shared;
    }

    public event EventHandler<string>? ShellOutputReceived;

    public bool IsShellConnected => client?.IsConnected == true && shell != null;

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
        shell = client.CreateShellStream("dumb", 160, 40, 1600, 1000, 8192);
        shellServerKey = serverKey;
        _ = ReadAvailableShellOutput(TimeSpan.FromMilliseconds(600));
        shell.WriteLine("export TERM=dumb; unset PROMPT_COMMAND");
        _ = ReadAvailableShellOutput(TimeSpan.FromMilliseconds(600));
        shell.DataReceived += Shell_DataReceived;
    }

    public void DisconnectShell()
    {
        if (shell != null)
        {
            shell.DataReceived -= Shell_DataReceived;
        }

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

    public OperationResult SendShellInput(string input, bool appendNewLine = true)
    {
        if (!IsShellConnected || shell == null)
        {
            return OperationResult.Failure(AppServices.Localizer.T("NoConnection"));
        }

        if (string.IsNullOrEmpty(input) && appendNewLine)
        {
            shell.WriteLine(string.Empty);
            shell.Flush();
            return OperationResult.Success(AppServices.Localizer.T("TerminalInputSent"));
        }

        if (appendNewLine)
        {
            shell.WriteLine(input);
        }
        else
        {
            shell.Write(input);
        }

        shell.Flush();
        return OperationResult.Success(AppServices.Localizer.T("TerminalInputSent"));
    }

    public OperationResult SendShellControl(byte controlCode)
    {
        if (!IsShellConnected || shell == null)
        {
            return OperationResult.Failure(AppServices.Localizer.T("NoConnection"));
        }

        shell.Write([controlCode], 0, 1);
        shell.Flush();
        return OperationResult.Success(AppServices.Localizer.T("TerminalInputSent"));
    }

    private void Shell_DataReceived(object? sender, ShellDataEventArgs e)
    {
        var output = SanitizeTerminalChunk(Encoding.UTF8.GetString(e.Data));
        if (!string.IsNullOrEmpty(output))
        {
            ShellOutputReceived?.Invoke(this, output);
        }
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

            if (attributes.Size > MaxEditableTextFileBytes)
            {
                return OperationResult<string>.Failure(Localized(
                    $"File is too large for the text editor ({attributes.Size:N0} bytes; limit {MaxEditableTextFileBytes:N0}).",
                    $"Файл слишком большой для текстового редактора ({attributes.Size:N0} байт; лимит {MaxEditableTextFileBytes:N0})."));
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

            using var contentStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            ReplaceRemoteFileSafely(sftp, remotePath, contentStream, createBackup: true, cancellationToken);
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
            var temporaryPath = localPath + $".scc-tmp-{Guid.NewGuid():N}";

            try
            {
                using (var fileStream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    sftp.DownloadFile(remotePath, fileStream, downloaded => cancellationToken.ThrowIfCancellationRequested());
                    fileStream.Flush(flushToDisk: true);
                }

                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporaryPath, localPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

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
            ReplaceRemoteFileSafely(sftp, remotePath, fileStream, createBackup: true, cancellationToken);
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
            EnsureRemotePathCanBeMutated(remotePath, "create");
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
            EnsureRemotePathCanBeMutated(oldPath, "rename");
            EnsureRemotePathCanBeMutated(newPath, "rename to");
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
            EnsureRemotePathCanBeMutated(remotePath, "delete");
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

    public Task<OperationResult<RemoteDeletePreview>> PreviewDeleteRemoteItemAsync(
        ServerProfile server,
        string remotePath,
        CancellationToken cancellationToken = default)
    {
        return RunSftpAsync(server, sftp =>
        {
            ValidateRemotePath(remotePath);
            EnsureRemotePathCanBeMutated(remotePath, "delete");
            sftp.Connect();
            var attributes = sftp.GetAttributes(remotePath);
            var preview = attributes.IsDirectory
                ? BuildDeletePreview(sftp, remotePath, cancellationToken)
                : new RemoteDeletePreview(remotePath, 1, 0, attributes.Size);
            return OperationResult<RemoteDeletePreview>.Success(
                preview,
                Localized("Delete preview created.", "Предпросмотр удаления готов."));
        }, "Delete preview error", "Ошибка предпросмотра удаления", cancellationToken);
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

    private Task<OperationResult> RunSftpAsync(
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

    private Task<OperationResult<T>> RunSftpAsync<T>(
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
            if (item.IsDirectory && !item.IsSymbolicLink)
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

    private static RemoteDeletePreview BuildDeletePreview(
        SftpClient sftp,
        string remotePath,
        CancellationToken cancellationToken)
    {
        var fileCount = 0;
        var directoryCount = 1;
        long totalBytes = 0;
        var pending = new Stack<string>();
        pending.Push(remotePath);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var item in sftp.ListDirectory(pending.Pop()).Where(x => x.Name != "." && x.Name != ".."))
            {
                if (item.IsDirectory && !item.IsSymbolicLink)
                {
                    directoryCount++;
                    pending.Push(item.FullName);
                }
                else
                {
                    fileCount++;
                    totalBytes += item.Attributes.Size;
                }
            }
        }

        return new RemoteDeletePreview(remotePath, fileCount, directoryCount, totalBytes);
    }

    private SshClient CreateClient(ServerProfile server)
    {
        var sshClient = new SshClient(CreateConnectionInfo(server))
        {
            KeepAliveInterval = TimeSpan.FromSeconds(30)
        };
        ConfigureHostKeyValidation(sshClient, server);
        return sshClient;
    }

    private SftpClient CreateSftpClient(ServerProfile server)
    {
        var sftpClient = new SftpClient(CreateConnectionInfo(server))
        {
            OperationTimeout = CommandTimeout
        };
        ConfigureHostKeyValidation(sftpClient, server);
        return sftpClient;
    }

    private void ConfigureHostKeyValidation(BaseClient clientToConfigure, ServerProfile server)
    {
        clientToConfigure.HostKeyReceived += (_, args) =>
        {
            var fingerprint = "SHA256:" + Convert.ToBase64String(SHA256.HashData(args.HostKey)).TrimEnd('=');
            args.CanTrust = knownHosts.VerifyOrTrust(server.Host, server.Port, fingerprint) != HostKeyVerificationResult.Changed;
        };
    }

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

    private static string SanitizeTerminalChunk(string output)
    {
        var cleaned = OscRegex.Replace(output, string.Empty);
        cleaned = AnsiRegex.Replace(cleaned, string.Empty);
        cleaned = OtherEscapeRegex.Replace(cleaned, string.Empty);
        return cleaned.Replace("\r\n", "\n");
    }

    private static void ValidateRemotePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(AppServices.Localizer.T("EnterPaths"), nameof(path));
        }

        _ = ApplicationRules.NormalizeRemotePath(path);
    }

    private static void EnsureRemotePathCanBeMutated(string remotePath, string operation)
    {
        if (ApplicationRules.IsProtectedRemotePath(remotePath))
        {
            throw new InvalidOperationException($"Refusing to {operation} protected remote path: {remotePath}");
        }
    }

    private static void ReplaceRemoteFileSafely(
        SftpClient sftp,
        string remotePath,
        Stream content,
        bool createBackup,
        CancellationToken cancellationToken)
    {
        EnsureRemotePathCanBeMutated(remotePath, "overwrite");
        var temporaryPath = $"{remotePath}.scc-tmp-{Guid.NewGuid():N}";
        var backupPath = $"{remotePath}.bak-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        var originalExists = sftp.Exists(remotePath);
        string? localBackupPath = null;

        try
        {
            sftp.UploadFile(content, temporaryPath, canOverride: false, uploaded =>
            {
                cancellationToken.ThrowIfCancellationRequested();
            });
            cancellationToken.ThrowIfCancellationRequested();

            if (originalExists)
            {
                if (createBackup)
                {
                    localBackupPath = Path.Combine(Path.GetTempPath(), $"scc-backup-{Guid.NewGuid():N}.tmp");

                    using (var backupStream = new FileStream(
                               localBackupPath,
                               FileMode.CreateNew,
                               FileAccess.ReadWrite,
                               FileShare.None,
                               bufferSize: 81920,
                               FileOptions.DeleteOnClose))
                    {
                        sftp.DownloadFile(remotePath, backupStream, downloaded => cancellationToken.ThrowIfCancellationRequested());
                        backupStream.Position = 0;
                        sftp.UploadFile(backupStream, backupPath, canOverride: false, uploaded => cancellationToken.ThrowIfCancellationRequested());

                        sftp.DeleteFile(remotePath);

                        try
                        {
                            sftp.RenameFile(temporaryPath, remotePath);
                            RotateRemoteBackups(sftp, remotePath, keep: 5);
                        }
                        catch
                        {
                            backupStream.Position = 0;

                            if (!sftp.Exists(remotePath))
                            {
                                sftp.UploadFile(backupStream, remotePath, canOverride: false);
                            }

                            throw;
                        }
                    }

                    return;
                }

                sftp.DeleteFile(remotePath);
            }

            try
            {
                sftp.RenameFile(temporaryPath, remotePath);
                RotateRemoteBackups(sftp, remotePath, keep: 5);
            }
            catch
            {
                throw;
            }
        }
        finally
        {
            if (sftp.IsConnected && sftp.Exists(temporaryPath))
            {
                sftp.DeleteFile(temporaryPath);
            }

            if (localBackupPath is not null && File.Exists(localBackupPath))
            {
                File.Delete(localBackupPath);
            }
        }
    }

    private static void RotateRemoteBackups(SftpClient sftp, string remotePath, int keep)
    {
        var parentPath = ApplicationRules.GetRemoteParentPath(remotePath);
        var fileName = remotePath[(remotePath.LastIndexOf('/') + 1)..];
        var backupPrefix = fileName + ".bak-";

        foreach (var backup in sftp.ListDirectory(parentPath)
                     .Where(x => !x.IsDirectory && x.Name.StartsWith(backupPrefix, StringComparison.Ordinal))
                     .OrderByDescending(x => x.Attributes.LastWriteTimeUtc)
                     .Skip(keep))
        {
            sftp.DeleteFile(backup.FullName);
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
