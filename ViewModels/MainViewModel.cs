using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServerControlCenter.Models;
using ServerControlCenter.Services;
using ServerControlCenter.Views;

namespace ServerControlCenter.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{


    private readonly ServerStorageService _storage = new();

    private readonly SshService _ssh = new();

    private readonly SavedCommandService _commandService = new();

    private readonly FileDialogService _fileDialog = new();

    private readonly DispatcherTimer _monitoringTimer;

    private bool isMonitoringRefreshing;

    public event Func<string, bool>? ConfirmCommandRequested;
    public event Func<string, bool>? ConfirmActionRequested;

    public ObservableCollection<ServerProfile> Servers { get; } = new();

    public ObservableCollection<ServerProfile> FilteredServers { get; } = new();

    public ObservableCollection<RemoteFileItem> RemoteFiles { get; } = new();

    public ObservableCollection<SavedCommand> SavedCommands { get; } = new();

    private readonly SshService _terminalSsh = new();

    [ObservableProperty]
    private string remoteFolderPath = "/var/www";

    [ObservableProperty]
    private RemoteFileItem? selectedRemoteFile;

    [ObservableProperty]
    private SavedCommand? selectedSavedCommand;

    [ObservableProperty]
    private string commandTitle = "";

    [ObservableProperty]
    private string commandText = "";

    [ObservableProperty]
    private string sshOutput = "";

    [ObservableProperty]
    private string customCommand = "";

    [ObservableProperty]
    private string password = "";

    [ObservableProperty]
    private ServerProfile? selectedServer;

    [ObservableProperty]
    private string serverSearchText = "";

    [ObservableProperty]
    private string name = "";

    [ObservableProperty]
    private string host = "";

    [ObservableProperty]
    private int port = 22;

    [ObservableProperty]
    private string username = "";

    [ObservableProperty]
    private string privateKeyPath = "";

    [ObservableProperty]
    private string notes = "";

    [ObservableProperty]
    private string logPath = "/var/log/nginx/error.log";

    [ObservableProperty]
    private string logContent = "";

    [ObservableProperty]
    private string monitorUptime = "";

    [ObservableProperty]
    private string monitorMemory = "";

    [ObservableProperty]
    private string monitorDisk = "";

    [ObservableProperty]
    private string monitorProcesses = "";

    [ObservableProperty]
    private string monitorLoad = "";

    [ObservableProperty]
    private string monitorCpu = "";

    [ObservableProperty]
    private string monitorRam = "";

    [ObservableProperty]
    private string monitorDiskUsage = "";

    [ObservableProperty]
    private string monitorUptimeShort = "";

    [ObservableProperty]
    private double monitorLoadPercent;

    [ObservableProperty]
    private double monitorCpuPercent;

    [ObservableProperty]
    private double monitorRamPercent;

    [ObservableProperty]
    private double monitorDiskPercent;

    [ObservableProperty]
    private string terminalOutput = "";

    [ObservableProperty]
    private string terminalCommand = "";

    [ObservableProperty]
    private string remoteFilePath = "/var/log/nginx/error.log";

    [ObservableProperty]
    private string localFilePath = "C:\\temp\\error.log";

    [ObservableProperty]
    private string sftpOutput = "";

    [ObservableProperty]
    private string autoMonitoringButtonText = "Авто: вкл";

    [ObservableProperty]
    private string monitoringStatus = "Автообновление каждые 30 секунд.";

    public MainViewModel()
    {
        _monitoringTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };

        _monitoringTimer.Tick += async (_, _) => await RefreshMonitoringFromTimerAsync();
        _monitoringTimer.Start();

        _ = InitializeAsync();
    }


    private async Task InitializeAsync()
    {
        try
        {
            await LoadServersAsync();
            await LoadSavedCommandsAsync();

            if (SelectedServer is null)
            {
                SelectedServer = FilteredServers.FirstOrDefault();
            }

            if (SelectedServer is not null)
            {
                _ = RefreshMonitoringAsync();
            }
        }
        catch (Exception ex)
        {
            SshOutput = $"Ошибка инициализации: {ex.Message}";
            SftpOutput = SshOutput;
        }
    }
    [RelayCommand]
    private void PickLocalFile()
    {
        var file = _fileDialog.PickFile();

        if (!string.IsNullOrWhiteSpace(file))
        {
            LocalFilePath = file;
        }
    }

    [RelayCommand]
    private void PickSaveFile()
    {
        var file = _fileDialog.PickSaveFile();

        if (!string.IsNullOrWhiteSpace(file))
        {
            LocalFilePath = file;
        }
    }

    [RelayCommand]
    private void PickLocalFolder()
    {
        var folder = _fileDialog.PickFolder();

        if (!string.IsNullOrWhiteSpace(folder))
        {
            LocalFilePath = folder;
        }
    }

    [RelayCommand]
    private void OpenLocalFolder()
    {
        var path = LocalFilePath?.Trim();

        if (string.IsNullOrWhiteSpace(path))
        {
            SftpOutput = "Укажи локальный путь.";
            return;
        }

        var folderPath = Directory.Exists(path)
            ? path
            : Path.GetDirectoryName(path);

        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            SftpOutput = "Локальная папка не найдена.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folderPath,
                UseShellExecute = true
            });

            SftpOutput = $"Открыта папка: {folderPath}";
        }
        catch (Exception ex)
        {
            SftpOutput = $"Не удалось открыть папку: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DownloadFileAsync()
    {
        if (SelectedServer is null)
        {
            SftpOutput = "Сначала выбери сервер.";
            return;
        }

        if (string.IsNullOrWhiteSpace(RemoteFilePath) ||
            string.IsNullOrWhiteSpace(LocalFilePath))
        {
            SftpOutput = "Укажи путь на сервере и локальный путь.";
            return;
        }

        var targetLocalPath = ResolveDownloadLocalPath(LocalFilePath, RemoteFilePath);
        LocalFilePath = targetLocalPath;

        SftpOutput = "Скачивание...";

        SftpOutput = await _ssh.DownloadFileAsync(
            SelectedServer,
            RemoteFilePath,
            targetLocalPath
        );
    }

    private static string ResolveDownloadLocalPath(string localPath, string remotePath)
    {
        if (!Directory.Exists(localPath))
        {
            return localPath;
        }

        var remoteFileName = remotePath
            .TrimEnd('/')
            .Split('/')
            .LastOrDefault();

        if (string.IsNullOrWhiteSpace(remoteFileName))
        {
            return localPath;
        }

        return Path.Combine(localPath, remoteFileName);
    }

    partial void OnServerSearchTextChanged(string value)
    {
        ApplyServerFilter();
    }


    partial void OnSelectedServerChanged(ServerProfile? value)
    {
        _terminalSsh.DisconnectShell();
        TerminalOutput = "";

        if (value is null)
        {
            return;
        }

        MonitoringStatus = $"Выбран: {value.Name}";

        if (_monitoringTimer.IsEnabled)
        {
            _ = RefreshMonitoringAsync();
        }
    }
    partial void OnSelectedSavedCommandChanged(SavedCommand? value)
    {
        if (value is null)
        {
            return;
        }

        CommandTitle = value.Title;
        CommandText = value.Command;
    }
    partial void OnSelectedRemoteFileChanged(RemoteFileItem? value)
    {
        if (value == null)
            return;

        if (!value.IsDirectory)
        {
            RemoteFilePath = value.FullPath;
        }
    }

    [RelayCommand]
    private async Task EditServerAsync()
    {
        if (SelectedServer is null)
        {
            SshOutput = "Выбери сервер для редактирования.";
            return;
        }

        var viewModel = new ServerEditViewModel(SelectedServer);
        var window = new ServerEditWindow(viewModel)
        {
            Owner = Application.Current.MainWindow
        };

        window.ShowDialog();

        if (!viewModel.IsSaved)
            return;

        await _storage.UpdateAsync(SelectedServer);
        await LoadServersAsync();

        SshOutput = "Сервер обновлён.";
    }

    [RelayCommand]
    private async Task UploadFileAsync()
    {
        if (SelectedServer is null)
        {
            SftpOutput = "Сначала выбери сервер.";
            return;
        }

        if (string.IsNullOrWhiteSpace(LocalFilePath) ||
            string.IsNullOrWhiteSpace(RemoteFilePath))
        {
            SftpOutput = "Укажи локальный путь и путь на сервере.";
            return;
        }

        SftpOutput = "Загрузка...";

        SftpOutput = await _ssh.UploadFileAsync(
            SelectedServer,
            LocalFilePath,
            RemoteFilePath
        );
    }

    public async Task UploadLocalFileToCurrentFolderAsync(string localPath)
    {
        if (SelectedServer is null)
        {
            SftpOutput = "Сначала выбери сервер.";
            return;
        }

        if (!File.Exists(localPath))
        {
            SftpOutput = $"Локальный файл не найден: {localPath}";
            return;
        }

        var remotePath = CombineRemotePath(
            RemoteFolderPath,
            Path.GetFileName(localPath)
        );

        LocalFilePath = localPath;
        RemoteFilePath = remotePath;
        SftpOutput = $"Загрузка: {Path.GetFileName(localPath)}";

        SftpOutput = await _ssh.UploadFileAsync(
            SelectedServer,
            localPath,
            remotePath
        );

        await LoadRemoteFilesAsync();
    }

    private static string GetRemoteParentPath(string remotePath)
    {
        return ApplicationRules.GetRemoteParentPath(remotePath);
    }

    private static bool ValidateRemoteItemName(string name, out string error)
    {
        return ApplicationRules.ValidateRemoteItemName(name, out error);
    }
    private static string CombineRemotePath(string remoteFolderPath, string fileName)
    {
        return ApplicationRules.CombineRemotePath(remoteFolderPath, fileName);
    }

    [RelayCommand]
    private async Task RefreshMonitoringAsync()
    {
        if (isMonitoringRefreshing)
        {
            return;
        }

        if (SelectedServer is null)
        {
            SshOutput = "Сначала выбери сервер.";
            MonitoringStatus = "Сервер не выбран.";
            return;
        }

        try
        {
            isMonitoringRefreshing = true;
            MonitoringStatus = $"Обновление: {SelectedServer.Name}";

            MonitorCpu = "Загрузка...";
            MonitorLoad = "Загрузка...";
            MonitorRam = "Загрузка...";
            MonitorDiskUsage = "Загрузка...";
            MonitorUptimeShort = "Загрузка...";
            MonitorProcesses = "Загрузка...";

            var monitoringCommand =
                "echo __CPU__; " +
                "read cpu user nice system idle iowait irq softirq steal guest guest_nice < /proc/stat; " +
                "idle1=$((idle+iowait)); total1=$((user+nice+system+idle+iowait+irq+softirq+steal)); " +
                "sleep 1; " +
                "read cpu user nice system idle iowait irq softirq steal guest guest_nice < /proc/stat; " +
                "idle2=$((idle+iowait)); total2=$((user+nice+system+idle+iowait+irq+softirq+steal)); " +
                "dt=$((total2-total1)); di=$((idle2-idle1)); " +
                "if [ \"$dt\" -gt 0 ]; then cpu_pct=$(( (100 * ($dt - $di)) / $dt )); else cpu_pct=0; fi; " +
                "printf \"CPU: %s%% used\\nPERCENT:%s\\n\" \"$cpu_pct\" \"$cpu_pct\"; " +
                "echo __LOAD__; " +
                "cpu_count=$(nproc 2>/dev/null || getconf _NPROCESSORS_ONLN 2>/dev/null || echo 1); " +
                "cat /proc/loadavg | awk -v cpus=\"$cpu_count\" '{printf \"Load: %s / %s CPU\\n\", $1, cpus}'; " +
                "echo __RAM__; " +
                "free -m | awk '/Mem:/ {printf \"RAM: %d MB / %d MB\\nPERCENT:%d\\n\", $3, $2, ($3 / $2) * 100}'; " +
                "echo __DISK__; " +
                "df -P -m / | awk 'NR==2 {gsub(\"%\", \"\", $5); printf \"Disk /: %d MB / %d MB\\nPERCENT:%d\\n\", $3, $2, $5}'; " +
                "echo __UPTIME__; " +
                "uptime -p; " +
                "echo __PROCESSES__; " +
                "ps -eo pid,comm,%cpu,%mem --sort=-%cpu | head -12";

            var output = await _ssh.RunCommandAsync(SelectedServer, monitoringCommand);

            if (!IsSuccessfulSshResult(output))
            {
                MonitorCpu = output;
                MonitorLoad = output;
                MonitorRam = output;
                MonitorDiskUsage = output;
                MonitorUptimeShort = output;
                MonitorProcesses = output;
                MonitorCpuPercent = 0;
                MonitorLoadPercent = 0;
                MonitorRamPercent = 0;
                MonitorDiskPercent = 0;
                SelectedServer.IsOnline = false;
                MonitoringStatus = "Ошибка обновления мониторинга.";
                return;
            }

            var cpuSection = ExtractMonitoringSection(output, "CPU");
            var loadSection = ExtractMonitoringSection(output, "LOAD");
            var ramSection = ExtractMonitoringSection(output, "RAM");
            var diskSection = ExtractMonitoringSection(output, "DISK");

            MonitorCpuPercent = ExtractPercentValue(cpuSection);
            MonitorLoadPercent = ExtractPercentValue(loadSection);
            MonitorRamPercent = ExtractPercentValue(ramSection);
            MonitorDiskPercent = ExtractPercentValue(diskSection);

            MonitorCpu = RemovePercentLine(cpuSection);
            MonitorLoad = RemovePercentLine(loadSection);
            MonitorRam = RemovePercentLine(ramSection);
            MonitorDiskUsage = RemovePercentLine(diskSection);
            MonitorUptimeShort = ExtractMonitoringSection(output, "UPTIME");
            MonitorProcesses = ExtractMonitoringSection(output, "PROCESSES");

            SelectedServer.IsOnline = true;
            MonitoringStatus = $"Обновлено: {DateTime.Now:HH:mm:ss}";
        }
        finally
        {
            isMonitoringRefreshing = false;
        }
    }

    [RelayCommand]
    private async Task ToggleAutoMonitoringAsync()
    {
        if (_monitoringTimer.IsEnabled)
        {
            _monitoringTimer.Stop();
            AutoMonitoringButtonText = "Авто: выкл";
            MonitoringStatus = "Автообновление остановлено.";
            return;
        }

        _monitoringTimer.Start();
        AutoMonitoringButtonText = "Авто: вкл";
        MonitoringStatus = "Автообновление каждые 30 секунд.";

        await RefreshMonitoringAsync();
    }

    private async Task RefreshMonitoringFromTimerAsync()
    {
        if (SelectedServer is null)
        {
            return;
        }

        await RefreshMonitoringAsync();
    }
    private async Task LoadSavedCommandsAsync()
    {
        SavedCommands.Clear();

        var commands = await _commandService.GetAllAsync();

        if (commands.Count == 0)
        {
            commands = GetDefaultSavedCommands();
            await _commandService.AddRangeAsync(commands);
        }

        foreach (var command in commands)
        {
            SavedCommands.Add(command);
        }
    }

    private static List<SavedCommand> GetDefaultSavedCommands()
    {
        return new List<SavedCommand>
        {
            new()
            {
                Title = "Docker: контейнеры",
                Command = "docker ps --format 'table {{.Names}}\\t{{.Status}}\\t{{.Ports}}'"
            },
            new()
            {
                Title = "Docker: ресурсы",
                Command = "docker stats --no-stream"
            },
            new()
            {
                Title = "Nginx: ошибки",
                Command = "journalctl -u nginx -p warning -n 80 --no-pager"
            },
            new()
            {
                Title = "Nginx: статус",
                Command = "systemctl status nginx --no-pager"
            },
            new()
            {
                Title = "Диски: крупные папки /var",
                Command = "du -h --max-depth=1 /var 2>/dev/null | sort -h"
            },
            new()
            {
                Title = "Диски: свободное место",
                Command = "df -h"
            },
            new()
            {
                Title = "Сеть: слушающие порты",
                Command = "ss -tulpen 2>/dev/null || netstat -tulpen"
            },
            new()
            {
                Title = "Система: failed services",
                Command = "systemctl --failed --no-pager"
            },
            new()
            {
                Title = "Система: journal ошибки",
                Command = "journalctl -p err -n 80 --no-pager"
            },
            new()
            {
                Title = "Система: нагрузка",
                Command = "top -b -n 1 | head -20"
            },
            new()
            {
                Title = "Система: сведения",
                Command = "uname -a; cat /etc/os-release 2>/dev/null"
            },
            new()
            {
                Title = "Система: uptime",
                Command = "uptime"
            }
        };
    }
    [RelayCommand]
    private async Task AddSavedCommandAsync()
    {
        if (string.IsNullOrWhiteSpace(CommandTitle) ||
            string.IsNullOrWhiteSpace(CommandText))
        {
            SshOutput = "Заполни название и команду.";
            return;
        }

        var command = new SavedCommand
        {
            Title = CommandTitle,
            Command = CommandText
        };

        await _commandService.AddAsync(command);

        CommandTitle = "";
        CommandText = "";

        await LoadSavedCommandsAsync();
    }

    [RelayCommand]
    private async Task UpdateSavedCommandAsync()
    {
        if (SelectedSavedCommand is null)
        {
            SshOutput = "Выбери команду для изменения.";
            return;
        }

        if (string.IsNullOrWhiteSpace(CommandTitle) ||
            string.IsNullOrWhiteSpace(CommandText))
        {
            SshOutput = "Заполни название и команду.";
            return;
        }

        SelectedSavedCommand.Title = CommandTitle.Trim();
        SelectedSavedCommand.Command = CommandText.Trim();

        await _commandService.UpdateAsync(SelectedSavedCommand);
        await LoadSavedCommandsAsync();

        SshOutput = "Команда обновлена.";
    }
    [RelayCommand]
    private async Task DeleteSavedCommandAsync()
    {
        if (SelectedSavedCommand is null)
        {
            SshOutput = "Выбери команду для удаления.";
            return;
        }

        await _commandService.DeleteAsync(SelectedSavedCommand);
        await LoadSavedCommandsAsync();
    }

    [RelayCommand]
    private async Task RunSavedCommandAsync()
    {
        if (SelectedSavedCommand is null)
        {
            SshOutput = "Выбери сохранённую команду.";
            return;
        }

        await RunServerCommandAsync(SelectedSavedCommand.Command);
    }



    private async Task LoadServersAsync()
    {
        Servers.Clear();

        var servers = await _storage.GetAllAsync();

        foreach (var server in servers)
        {
            Servers.Add(server);
        }

        ApplyServerFilter();
    }

    private void ApplyServerFilter()
    {
        FilteredServers.Clear();

        var query = ServerSearchText.Trim();
        var servers = string.IsNullOrWhiteSpace(query)
            ? Servers
            : Servers.Where(server => MatchesServerSearch(server, query));

        foreach (var server in servers)
        {
            FilteredServers.Add(server);
        }

        if (SelectedServer is not null && !FilteredServers.Contains(SelectedServer))
        {
            SelectedServer = null;
        }
    }

    private static bool MatchesServerSearch(ServerProfile server, string query)
    {
        return Contains(server.Name, query) ||
            Contains(server.Host, query) ||
            Contains(server.Username, query) ||
            Contains(server.Notes, query);
    }

    private static bool Contains(string? value, string query)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
    [RelayCommand]
    private async Task LoadLogAsync()
    {
        if (SelectedServer is null)
        {
            LogContent = "Выберите сервер.";
            return;
        }

        if (string.IsNullOrWhiteSpace(LogPath))
        {
            LogContent = "Укажите путь к логу.";
            return;
        }

        LogContent = "Загрузка...";

        var safeLogPath = QuoteShellArgument(LogPath);

        LogContent = await _ssh.RunCommandAsync(
            SelectedServer,
            $"tail -n 100 {safeLogPath}"
        );
    }


    [RelayCommand]
    private void UseNginxLog()
    {
        LogPath = "/var/log/nginx/error.log";
    }

    [RelayCommand]
    private void UseSyslog()
    {
        LogPath = "/var/log/syslog";
    }

    [RelayCommand]
    private void UseAuthLog()
    {
        LogPath = "/var/log/auth.log";
    }

    [RelayCommand]
    private async Task RunCustomCommandAsync()
    {
        if (string.IsNullOrWhiteSpace(CustomCommand))
        {
            SshOutput = "Введите команду.";
            return;
        }

        await RunServerCommandAsync(CustomCommand);
    }

    [RelayCommand]
    private async Task AddServerAsync()
    {
        var server = new ServerProfile
        {
            Port = 22
        };

        var viewModel = new ServerEditViewModel(server, "Добавление сервера");
        var window = new ServerEditWindow(viewModel)
        {
            Owner = Application.Current.MainWindow
        };

        window.ShowDialog();

        if (!viewModel.IsSaved)
            return;

        await _storage.AddAsync(server);
        await LoadServersAsync();

        SelectedServer = Servers.FirstOrDefault(x => x.Id == server.Id);

        SshOutput = "Сервер добавлен.";
    }

    [RelayCommand]
    private async Task DeleteServerAsync()
    {
        if (SelectedServer is null)
        {
            return;
        }

        var serverName = SelectedServer.Name;
        var confirmed = ConfirmActionRequested?.Invoke(
            $"Удалить сервер {serverName} из списка?"
        ) ?? false;

        if (!confirmed)
        {
            return;
        }

        await _storage.DeleteAsync(SelectedServer);
        await LoadServersAsync();

        SshOutput = $"Сервер удалён: {serverName}";
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (SelectedServer is null)
        {
            SshOutput = "Сначала выбери сервер.";
            return;
        }

        SshOutput = $"Проверка SSH: {SelectedServer.Name}";

        var result = await _ssh.TestConnectionAsync(SelectedServer);

        SshOutput = result;
        SelectedServer.IsOnline = result.Equals(
            "Подключение успешно.",
            StringComparison.OrdinalIgnoreCase
        );
    }
    private async Task RunServerCommandAsync(string command)
    {
        if (SelectedServer is null)
        {
            SshOutput = "Сначала выбери сервер.";
            return;
        }

        if (RequiresCommandConfirmation(command))
        {
            var confirmed = ConfirmCommandRequested?.Invoke(command) ?? false;

            if (!confirmed)
            {
                SshOutput = "Выполнение опасной команды отменено.";
                return;
            }
        }

        SshOutput = $"Выполняется команда: {command}";

        SshOutput = await _ssh.RunCommandAsync(SelectedServer, command);
        SelectedServer.IsOnline = IsSuccessfulSshResult(SshOutput);
    }
    [RelayCommand]
    private async Task RunUptimeAsync()
    {
        await RunServerCommandAsync("uptime");
        MonitorUptimeShort = ExtractMonitoringSection(SshOutput, "UPTIME");
    }

    [RelayCommand]
    private async Task RunDiskAsync()
    {
        await RunServerCommandAsync("df -h");

        var diskSection = ExtractMonitoringSection(SshOutput, "DISK");
        MonitorDiskPercent = ExtractPercentValue(diskSection);
        MonitorDiskUsage = RemovePercentLine(diskSection);
    }

    [RelayCommand]
    private async Task RunMemoryAsync()
    {
        await RunServerCommandAsync("free -h");

        var ramSection = ExtractMonitoringSection(SshOutput, "RAM");
        MonitorRamPercent = ExtractPercentValue(ramSection);
        MonitorRam = RemovePercentLine(ramSection);
    }

    [RelayCommand]
    private async Task RunProcessesAsync()
    {
        await RunServerCommandAsync("ps aux --sort=-%cpu | head -20");
    }

    [RelayCommand]
    private async Task LoadRemoteFilesAsync()
    {
        if (SelectedServer is null)
        {
            SftpOutput = "Выберите сервер.";
            return;
        }

        RemoteFiles.Clear();

        try
        {
            var files = await _ssh.GetFilesAsync(
                SelectedServer,
                RemoteFolderPath
            );

            foreach (var file in files)
            {
                RemoteFiles.Add(file);
            }

            SftpOutput = $"Загружено: {files.Count}";
            SelectedServer.IsOnline = true;
        }
        catch (Exception ex)
        {
            SftpOutput = ex.Message;
        }
    }

    [RelayCommand]
    private async Task OpenRemoteFolderAsync()
    {
        if (SelectedRemoteFile is null)
        {
            return;
        }

        if (!SelectedRemoteFile.IsDirectory)
        {
            return;
        }

        RemoteFolderPath = SelectedRemoteFile.FullPath;

        await LoadRemoteFilesAsync();
    }


    [RelayCommand]
    private async Task CheckAllServersAsync()
    {
        using var concurrency = new SemaphoreSlim(5);

        var checks = Servers.Select(async server =>
        {
            await concurrency.WaitAsync();

            try
            {
                var result = await _ssh.RunCommandAsync(server, "echo ok");
                server.IsOnline = result.Trim().Equals("ok", StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                concurrency.Release();
            }
        });

        await Task.WhenAll(checks);
        OnPropertyChanged(nameof(Servers));
    }

    public async Task CreateRemoteFolderAsync(string folderName)
    {
        if (SelectedServer is null)
        {
            SftpOutput = "Сначала выбери сервер.";
            return;
        }

        if (!ValidateRemoteItemName(folderName, out var error))
        {
            SftpOutput = error;
            return;
        }

        var remotePath = CombineRemotePath(RemoteFolderPath, folderName.Trim());

        SftpOutput = "Создание папки...";
        SftpOutput = await _ssh.CreateRemoteDirectoryAsync(SelectedServer, remotePath);

        await LoadRemoteFilesAsync();
    }

    public async Task RenameSelectedRemoteItemAsync(string newName)
    {
        if (SelectedServer is null)
        {
            SftpOutput = "Сначала выбери сервер.";
            return;
        }

        if (SelectedRemoteFile is null)
        {
            SftpOutput = "Выбери файл или папку для переименования.";
            return;
        }

        if (!ValidateRemoteItemName(newName, out var error))
        {
            SftpOutput = error;
            return;
        }

        var newPath = CombineRemotePath(
            GetRemoteParentPath(SelectedRemoteFile.FullPath),
            newName.Trim()
        );

        SftpOutput = "Переименование...";
        SftpOutput = await _ssh.RenameRemoteItemAsync(
            SelectedServer,
            SelectedRemoteFile.FullPath,
            newPath
        );

        await LoadRemoteFilesAsync();
    }
    [RelayCommand]
    private async Task DeleteSelectedRemoteItemAsync()
    {
        if (SelectedServer is null)
        {
            SftpOutput = "Сначала выбери сервер.";
            return;
        }

        if (SelectedRemoteFile is null)
        {
            SftpOutput = "Выбери файл или папку для удаления.";
            return;
        }

        SftpOutput = "Удаление...";

        SftpOutput = await _ssh.DeleteRemoteItemAsync(
            SelectedServer,
            SelectedRemoteFile.FullPath,
            SelectedRemoteFile.IsDirectory
        );

        await LoadRemoteFilesAsync();
    }
    [RelayCommand]
    private void EditRemoteFile()
    {
        if (SelectedServer is null)
        {
            SftpOutput = "Сначала выбери сервер.";
            return;
        }

        if (SelectedRemoteFile is null)
        {
            SftpOutput = "Выбери файл для редактирования.";
            return;
        }

        if (SelectedRemoteFile.IsDirectory)
        {
            SftpOutput = "Папку нельзя открыть в редакторе.";
            return;
        }

        var viewModel = new RemoteFileEditorViewModel(
            SelectedServer,
            SelectedRemoteFile.FullPath,
            _ssh
        );

        var window = new RemoteFileEditorWindow(viewModel)
        {
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
    }
    [RelayCommand]
    private async Task OpenSelectedRemoteItemAsync()
    {
        if (SelectedRemoteFile is null)
            return;

        if (SelectedRemoteFile.IsDirectory)
        {
            RemoteFolderPath = SelectedRemoteFile.FullPath;
            await LoadRemoteFilesAsync();
            return;
        }

        RemoteFilePath = SelectedRemoteFile.FullPath;
        SftpOutput = $"Выбран файл: {SelectedRemoteFile.FullPath}";
    }

    private static bool RequiresCommandConfirmation(string command)
    {
        return ApplicationRules.RequiresCommandConfirmation(command);
    }
    private static string ExtractMonitoringSection(string output, string sectionName)
    {
        return ApplicationRules.ExtractMonitoringSection(output, sectionName);
    }

    private static double ExtractPercentValue(string section)
    {
        return ApplicationRules.ExtractPercentValue(section);
    }

    private static string RemovePercentLine(string section)
    {
        return ApplicationRules.RemovePercentLine(section);
    }

    private static bool IsMonitoringMarker(string line)
    {
        var value = line.Trim();

        return value.StartsWith("__", StringComparison.Ordinal) &&
            value.EndsWith("__", StringComparison.Ordinal);
    }
    private static string QuoteShellArgument(string value)
    {
        return $"'{value.Replace("'", "'\\''")}'";
    }

    private static bool IsSuccessfulSshResult(string result)
    {
        return ApplicationRules.IsSuccessfulSshResult(result);
    }

    [RelayCommand]
    private async Task GoBackRemoteFolderAsync()
    {
        if (string.IsNullOrWhiteSpace(RemoteFolderPath))
        {
            RemoteFolderPath = "/";
            await LoadRemoteFilesAsync();
            return;
        }

        var currentPath = RemoteFolderPath.TrimEnd('/');

        if (currentPath == "")
        {
            currentPath = "/";
        }

        if (currentPath == "/")
        {
            RemoteFolderPath = "/";
            await LoadRemoteFilesAsync();
            return;
        }

        var lastSlashIndex = currentPath.LastIndexOf('/');

        if (lastSlashIndex <= 0)
        {
            RemoteFolderPath = "/";
        }
        else
        {
            RemoteFolderPath = currentPath[..lastSlashIndex];
        }

        await LoadRemoteFilesAsync();
    }

    [RelayCommand]
    private async Task SendTerminalCommandAsync()
    {
        if (SelectedServer is null)
        {
            TerminalOutput += "\nОшибка: сначала выбери сервер.\n";
            return;
        }

        if (string.IsNullOrWhiteSpace(TerminalCommand))
        {
            return;
        }

        if (RequiresCommandConfirmation(TerminalCommand))
        {
            var confirmed = ConfirmCommandRequested?.Invoke(TerminalCommand) ?? false;

            if (!confirmed)
            {
                TerminalOutput += "\nВыполнение опасной команды отменено.\n";
                return;
            }
        }

        var command = TerminalCommand;

        try
        {
            _terminalSsh.ConnectShell(SelectedServer);

            var result = await Task.Run(() =>
                _terminalSsh.SendShellCommand(command));

            TerminalOutput +=
                $"\n> {command}\n{result}\n";

            TerminalCommand = "";
        }
        catch (Exception ex)
        {
            SelectedServer.IsOnline = false;
            TerminalOutput +=
                $"\nОшибка: {ex.Message}\n";
        }
    }

    public void Dispose()
    {
        _monitoringTimer.Stop();
        _terminalSsh.Dispose();
    }
}

























