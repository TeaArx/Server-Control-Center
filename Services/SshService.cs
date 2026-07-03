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

    public string SendShellCommand(string command)
    {
        if (shell == null)
        {
            return "Нет подключения";
        }

        if (string.IsNullOrWhiteSpace(command))
        {
            return string.Empty;
        }

        shell.WriteLine(command);

        var rawOutput = ReadAvailableShellOutput(ShellReadTimeout);
        return CleanTerminalOutput(rawOutput, command);
    }

    public Task<string> TestConnectionAsync(ServerProfile server)
    {
        return RunSafeAsync(server, client =>
        {
            client.Connect();

            return client.IsConnected
                ? "Подключение успешно."
                : "Не удалось подключиться.";
        });
    }

    public Task<string> RunCommandAsync(ServerProfile server, string command)
    {
        return RunSafeAsync(server, client =>
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return string.Empty;
            }

            client.Connect();

            using var sshCommand = client.CreateCommand(command);
            sshCommand.CommandTimeout = CommandTimeout;

            var output = sshCommand.Execute();

            if (!string.IsNullOrWhiteSpace(sshCommand.Error))
            {
                return sshCommand.Error;
            }

            if (sshCommand.ExitStatus != 0 && string.IsNullOrWhiteSpace(output))
            {
                return $"Команда завершилась с кодом {sshCommand.ExitStatus}.";
            }

            return output;
        });
    }

    private static Task<string> RunSafeAsync(ServerProfile server, Func<SshClient, string> action)
    {
        return Task.Run(() =>
        {
            try
            {
                using var client = CreateClient(server);
                return action(client);
            }
            catch (Exception ex)
            {
                return $"Ошибка SSH: {ex.Message}";
            }
        });
    }

    private static SshClient CreateClient(ServerProfile server)
    {
        var sshClient = new SshClient(CreateConnectionInfo(server))
        {
            KeepAliveInterval = TimeSpan.FromSeconds(30)
        };

        return sshClient;
    }

    public Task<string> ReadTextFileAsync(ServerProfile server, string remotePath)
    {
        return Task.Run(() =>
        {
            try
            {
                ValidateRemotePath(remotePath);

                using var sftp = CreateSftpClient(server);
                sftp.Connect();

                var attributes = sftp.GetAttributes(remotePath);
                if (attributes.IsDirectory)
                {
                    return "Ошибка чтения файла: выбран путь к папке.";
                }

                using var stream = new MemoryStream();
                sftp.DownloadFile(remotePath, stream);

                return Encoding.UTF8.GetString(stream.ToArray());
            }
            catch (Exception ex)
            {
                return $"Ошибка чтения файла: {ex.Message}";
            }
        });
    }

    public Task<string> SaveTextFileAsync(ServerProfile server, string remotePath, string content)
    {
        return Task.Run(() =>
        {
            try
            {
                ValidateRemotePath(remotePath);

                using var sftp = CreateSftpClient(server);
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

                return $"Файл сохранён: {remotePath}";
            }
            catch (Exception ex)
            {
                return $"Ошибка сохранения файла: {ex.Message}";
            }
        });
    }

    public Task<string> DownloadFileAsync(ServerProfile server, string remotePath, string localPath)
    {
        return Task.Run(() =>
        {
            try
            {
                ValidateRemotePath(remotePath);
                ValidateLocalPath(localPath);

                var directory = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var sftp = CreateSftpClient(server);
                sftp.Connect();

                using var fileStream = File.Create(localPath);
                sftp.DownloadFile(remotePath, fileStream);

                return $"Файл скачан: {localPath}";
            }
            catch (Exception ex)
            {
                return $"Ошибка скачивания: {ex.Message}";
            }
        });
    }

    public Task<string> UploadFileAsync(ServerProfile server, string localPath, string remotePath)
    {
        return Task.Run(() =>
        {
            try
            {
                ValidateRemotePath(remotePath);
                ValidateLocalPath(localPath);

                if (!File.Exists(localPath))
                {
                    return $"Ошибка загрузки: локальный файл не найден: {localPath}";
                }

                using var sftp = CreateSftpClient(server);
                sftp.Connect();

                using var fileStream = File.OpenRead(localPath);
                sftp.UploadFile(fileStream, remotePath, true);

                return $"Файл загружен: {remotePath}";
            }
            catch (Exception ex)
            {
                return $"Ошибка загрузки: {ex.Message}";
            }
        });
    }

    public Task<string> CreateRemoteDirectoryAsync(ServerProfile server, string remotePath)
    {
        return Task.Run(() =>
        {
            try
            {
                ValidateRemotePath(remotePath);

                using var sftp = CreateSftpClient(server);
                sftp.Connect();
                sftp.CreateDirectory(remotePath);

                return $"Папка создана: {remotePath}";
            }
            catch (Exception ex)
            {
                return $"Ошибка создания папки: {ex.Message}";
            }
        });
    }

    public Task<string> RenameRemoteItemAsync(ServerProfile server, string oldPath, string newPath)
    {
        return Task.Run(() =>
        {
            try
            {
                ValidateRemotePath(oldPath);
                ValidateRemotePath(newPath);

                using var sftp = CreateSftpClient(server);
                sftp.Connect();
                sftp.RenameFile(oldPath, newPath);

                return $"Переименовано: {newPath}";
            }
            catch (Exception ex)
            {
                return $"Ошибка переименования: {ex.Message}";
            }
        });
    }

    public Task<string> DeleteRemoteItemAsync(ServerProfile server, string remotePath, bool isDirectory)
    {
        return Task.Run(() =>
        {
            try
            {
                ValidateRemotePath(remotePath);

                using var sftp = CreateSftpClient(server);
                sftp.Connect();

                if (isDirectory)
                {
                    DeleteDirectoryRecursive(sftp, remotePath);
                }
                else
                {
                    sftp.DeleteFile(remotePath);
                }

                return $"Удалено: {remotePath}";
            }
            catch (Exception ex)
            {
                return $"Ошибка удаления: {ex.Message}";
            }
        });
    }

    public Task<List<RemoteFileItem>> GetFilesAsync(ServerProfile server, string path)
    {
        return Task.Run(() =>
        {
            ValidateRemotePath(path);

            using var sftp = CreateSftpClient(server);
            sftp.Connect();

            return sftp
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
        });
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

    private static SftpClient CreateSftpClient(ServerProfile server)
    {
        return new SftpClient(CreateConnectionInfo(server))
        {
            OperationTimeout = CommandTimeout
        };
    }

    private static ConnectionInfo CreateConnectionInfo(ServerProfile server)
    {
        if (string.IsNullOrWhiteSpace(server.Host))
        {
            throw new InvalidOperationException("Укажи IP или Host.");
        }

        if (server.Port <= 0 || server.Port > 65535)
        {
            throw new InvalidOperationException("Порт должен быть от 1 до 65535.");
        }

        if (string.IsNullOrWhiteSpace(server.Username))
        {
            throw new InvalidOperationException("Укажи логин.");
        }

        AuthenticationMethod authMethod;

        if (!string.IsNullOrWhiteSpace(server.PrivateKeyPath))
        {
            if (!File.Exists(server.PrivateKeyPath))
            {
                throw new InvalidOperationException($"SSH-ключ не найден: {server.PrivateKeyPath}");
            }

            authMethod = new PrivateKeyAuthenticationMethod(
                server.Username,
                new PrivateKeyFile(server.PrivateKeyPath)
            );
        }
        else
        {
            var password = SecretProtector.Unprotect(server.Password);

            if (string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException("Укажи SSH-ключ или пароль.");
            }

            authMethod = new PasswordAuthenticationMethod(server.Username, password);
        }

        return new ConnectionInfo(
            server.Host,
            server.Port,
            server.Username,
            authMethod
        )
        {
            Timeout = ConnectTimeout
        };
    }

    private static string CreateServerKey(ServerProfile server)
    {
        return string.Join(
            '|',
            server.Host.Trim(),
            server.Port.ToString(),
            server.Username.Trim(),
            server.PrivateKeyPath?.Trim() ?? "password"
        );
    }

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
            throw new ArgumentException("Укажи путь на сервере.", nameof(path));
        }
    }

    private static void ValidateLocalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Укажи локальный путь.", nameof(path));
        }
    }

    public void Dispose()
    {
        DisconnectShell();
    }
}
