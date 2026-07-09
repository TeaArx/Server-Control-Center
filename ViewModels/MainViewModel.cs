using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
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
    private const int MetricHistoryLimit = 26;

    private readonly ServerStorageService _storage = new();
    private readonly SshService _ssh = new();
    private readonly SshService _terminalSsh = new();
    private readonly SavedCommandService _commandService = new();
    private readonly DashboardDataService _dashboardData = new();
    private readonly FileDialogService _fileDialog = new();
    private readonly DispatcherTimer _monitoringTimer;
    private readonly List<double> cpuHistory = new();
    private readonly List<double> ramHistory = new();
    private readonly List<double> diskHistory = new();
    private readonly List<double> networkHistory = new();
    private readonly LocalizationService localizer = AppServices.Localizer;

    private bool isMonitoringRefreshing;

    public event Func<string, bool>? ConfirmCommandRequested;
    public event Func<string, bool>? ConfirmActionRequested;

    public ObservableCollection<ServerProfile> Servers { get; } = new();
    public ObservableCollection<ServerProfile> FilteredServers { get; } = new();
    public ObservableCollection<ServerProfile> FavoriteServers { get; } = new();
    public ObservableCollection<DashboardGroup> Groups { get; } = new();
    public ObservableCollection<RemoteFileItem> RemoteFiles { get; } = new();
    public ObservableCollection<SavedCommand> SavedCommands { get; } = new();
    public ObservableCollection<SavedCommand> FavoriteCommands { get; } = new();
    public ObservableCollection<RecentLogEntry> RecentLogs { get; } = new();
    public ObservableCollection<ActivityLogEntry> ActivityLogs { get; } = new();

    [ObservableProperty]
    private ServerProfile? selectedServer;

    [ObservableProperty]
    private string selectedGroupName = "All Servers";

    [ObservableProperty]
    private string serverSearchText = "";

    [ObservableProperty]
    private RemoteFileItem? selectedRemoteFile;

    [ObservableProperty]
    private SavedCommand? selectedSavedCommand;

    [ObservableProperty]
    private AppSettings appSettings = new();

    [ObservableProperty]
    private UserProfile userProfile = new();

    [ObservableProperty]
    private string overlayTitle = "";

    [ObservableProperty]
    private bool isProfilePanelOpen;

    [ObservableProperty]
    private bool isSettingsPanelOpen;

    [ObservableProperty]
    private bool isFavoritesPanelOpen;

    [ObservableProperty]
    private bool isActivityPanelOpen;

    [ObservableProperty]
    private string remoteFolderPath = "/var/www";

    [ObservableProperty]
    private string remoteFilePath = "/var/log/nginx/error.log";

    [ObservableProperty]
    private string localFilePath = "C:\\temp\\error.log";

    [ObservableProperty]
    private string logPath = "/var/log/nginx/error.log";

    [ObservableProperty]
    private string logContent = "";

    [ObservableProperty]
    private string commandTitle = "";

    [ObservableProperty]
    private string commandText = "";

    [ObservableProperty]
    private string customCommand = "";

    [ObservableProperty]
    private string sshOutput = "";

    [ObservableProperty]
    private string terminalOutput = AppServices.Localizer.T("TerminalWelcome");

    [ObservableProperty]
    private string terminalCommand = "";

    [ObservableProperty]
    private string sftpOutput = "";

    [ObservableProperty]
    private string monitorCpu = "";

    [ObservableProperty]
    private string monitorRam = "";

    [ObservableProperty]
    private string monitorDiskUsage = "";

    [ObservableProperty]
    private string monitorUptimeShort = "n/a";

    [ObservableProperty]
    private string monitorProcesses = "";

    [ObservableProperty]
    private string monitorLoad = "";

    [ObservableProperty]
    private string monitorNetwork = "";

    [ObservableProperty]
    private double monitorCpuPercent;

    [ObservableProperty]
    private double monitorRamPercent;

    [ObservableProperty]
    private double monitorDiskPercent;

    [ObservableProperty]
    private double monitorLoadPercent;

    [ObservableProperty]
    private double monitorNetworkPercent;

    [ObservableProperty]
    private string cpuSparkline = "▁▁▁▁▁▁▁▁";

    [ObservableProperty]
    private string ramSparkline = "▁▁▁▁▁▁▁▁";

    [ObservableProperty]
    private string diskSparkline = "▁▁▁▁▁▁▁▁";

    [ObservableProperty]
    private string networkSparkline = "▁▂▃▂▁▃▂▁";

    [ObservableProperty]
    private string autoMonitoringButtonText = AppServices.Localizer.T("AutoOn");

    [ObservableProperty]
    private string monitoringStatus = AppServices.Localizer.T("AutoRefreshEvery");

    public LocalizationService L => localizer;
    public IReadOnlyList<LanguageOption> LanguageOptions => localizer.Languages;
    public string SelectedServerTitle => SelectedServer?.Name ?? L.T("NoServerSelected");
    public string SelectedServerSubtitle => SelectedServer is null
        ? L.T("ChooseServer")
        : $"{SelectedServer.Username}@{SelectedServer.Host}:{SelectedServer.Port}";
    public string SelectedServerAddress => SelectedServer?.IpAddressDisplay ?? SelectedServer?.Host ?? "";
    public string SelectedServerOs => SelectedServer?.OsName ?? "Ubuntu 22.04";
    public string OnlineText => SelectedServer?.IsOnline == true ? "Online" : "Offline";
    public int ServerCount => Servers.Count;
    public string RemoteFilesSummary => L.LanguageCode == "ru"
        ? $"{RemoteFiles.Count(x => x.IsDirectory)} папок, {RemoteFiles.Count(x => !x.IsDirectory)} файлов"
        : $"{RemoteFiles.Count(x => x.IsDirectory)} folders, {RemoteFiles.Count(x => !x.IsDirectory)} files";
    public double[] CpuHistory { get; private set; } = [];
    public double[] RamHistory { get; private set; } = [];
    public double[] DiskHistory { get; private set; } = [];
    public double[] NetworkHistory { get; private set; } = [];

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
            AppSettings = await _dashboardData.GetSettingsAsync();
            L.SetLanguage(AppSettings.LanguageCode);
            UserProfile = await _dashboardData.GetProfileAsync();
            RemoteFolderPath = AppSettings.DefaultRemoteFolder;
            LogPath = AppSettings.DefaultLogPath;
            RemoteFilePath = AppSettings.DefaultLogPath;
            _monitoringTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(AppSettings.MonitoringIntervalSeconds, 5, 3600));

            if (!AppSettings.AutoRefreshMonitoring)
            {
                _monitoringTimer.Stop();
                AutoMonitoringButtonText = L.T("AutoOff");
            }

            await LoadServersAsync();
            await LoadSavedCommandsAsync();
            await LoadActivityLogsAsync();

            SelectedServer ??= FilteredServers.FirstOrDefault();

            if (SelectedServer is not null)
            {
                _ = RefreshMonitoringAsync();
                _ = LoadRemoteFilesAsync();
                _ = LoadLogAsync();
            }
        }
        catch (Exception ex)
        {
            SshOutput = L.Format("InitializationError", ex.Message);
            SftpOutput = SshOutput;
        }
    }

    partial void OnServerSearchTextChanged(string value) => ApplyServerFilter();

    partial void OnSelectedGroupNameChanged(string value) => ApplyServerFilter();

    partial void OnSelectedServerChanged(ServerProfile? value)
    {
        _terminalSsh.DisconnectShell();
        OnPropertyChanged(nameof(SelectedServerTitle));
        OnPropertyChanged(nameof(SelectedServerSubtitle));
        OnPropertyChanged(nameof(SelectedServerAddress));
        OnPropertyChanged(nameof(SelectedServerOs));
        OnPropertyChanged(nameof(OnlineText));

        if (value is null)
        {
            return;
        }

        MonitoringStatus = value.Name;
        TerminalOutput = $"Connected target: {value.Username}@{value.Host}\n";

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
        if (value is { IsDirectory: false })
        {
            RemoteFilePath = value.FullPath;
        }
    }

    [RelayCommand]
    private void SelectGroup(string? groupName)
    {
        SelectedGroupName = string.IsNullOrWhiteSpace(groupName) ? "All Servers" : groupName;
        RefreshGroupSelection();
    }

    [RelayCommand]
    private async Task ToggleSelectedServerFavoriteAsync()
    {
        if (SelectedServer is null)
        {
            return;
        }

        SelectedServer.IsFavorite = !SelectedServer.IsFavorite;
        await _storage.UpdateAsync(SelectedServer);
        await LogActivityAsync(L.T("Favorites"), SelectedServer.Name, SelectedServer.IsFavorite ? L.T("Favorite") : L.T("ToggleFavorite"));
        RefreshDashboardCollections();
    }

    [RelayCommand]
    private async Task ToggleSavedCommandFavoriteAsync()
    {
        if (SelectedSavedCommand is null)
        {
            SshOutput = L.T("ChooseCommand");
            return;
        }

        SelectedSavedCommand.IsFavorite = !SelectedSavedCommand.IsFavorite;
        await _commandService.UpdateAsync(SelectedSavedCommand);
        await LogActivityAsync(L.T("Favorites"), SelectedSavedCommand.Title, SelectedSavedCommand.IsFavorite ? L.T("Favorite") : L.T("ToggleFavorite"));
        await LoadSavedCommandsAsync();
    }

    [RelayCommand]
    private void OpenProfilePanel()
    {
        CloseOverlayPanels();
        OverlayTitle = L.T("Profile");
        IsProfilePanelOpen = true;
    }

    [RelayCommand]
    private void OpenSettingsPanel()
    {
        CloseOverlayPanels();
        OverlayTitle = L.T("Settings");
        IsSettingsPanelOpen = true;
    }

    [RelayCommand]
    private void OpenFavoritesPanel()
    {
        CloseOverlayPanels();
        OverlayTitle = L.T("Favorites");
        IsFavoritesPanelOpen = true;
    }

    [RelayCommand]
    private async Task OpenActivityPanelAsync()
    {
        CloseOverlayPanels();
        OverlayTitle = L.T("ActivityLog");
        await LoadActivityLogsAsync();
        IsActivityPanelOpen = true;
    }

    [RelayCommand]
    private void CloseOverlayPanels() => CloseOverlayPanelsCore();

    private void CloseOverlayPanelsCore()
    {
        IsProfilePanelOpen = false;
        IsSettingsPanelOpen = false;
        IsFavoritesPanelOpen = false;
        IsActivityPanelOpen = false;
        OverlayTitle = "";
    }

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        UserProfile.DisplayName = string.IsNullOrWhiteSpace(UserProfile.DisplayName) ? "Admin" : UserProfile.DisplayName.Trim();
        UserProfile.Initials = CreateInitials(UserProfile.DisplayName, UserProfile.Initials);
        await _dashboardData.SaveProfileAsync(UserProfile);
        await LogActivityAsync(L.T("Profile"), UserProfile.DisplayName, L.T("ProfileUpdated"));
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        AppSettings.MonitoringIntervalSeconds = Math.Clamp(AppSettings.MonitoringIntervalSeconds, 5, 3600);
        AppSettings.DefaultRemoteFolder = string.IsNullOrWhiteSpace(AppSettings.DefaultRemoteFolder) ? "/var/www" : AppSettings.DefaultRemoteFolder.Trim();
        AppSettings.DefaultLogPath = string.IsNullOrWhiteSpace(AppSettings.DefaultLogPath) ? "/var/log/nginx/error.log" : AppSettings.DefaultLogPath.Trim();
        AppSettings.LanguageCode = string.Equals(AppSettings.LanguageCode, "ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en";
        await _dashboardData.SaveSettingsAsync(AppSettings);
        L.SetLanguage(AppSettings.LanguageCode);

        RemoteFolderPath = AppSettings.DefaultRemoteFolder;
        LogPath = AppSettings.DefaultLogPath;
        _monitoringTimer.Interval = TimeSpan.FromSeconds(AppSettings.MonitoringIntervalSeconds);

        if (AppSettings.AutoRefreshMonitoring && !_monitoringTimer.IsEnabled)
        {
            _monitoringTimer.Start();
        }
        else if (!AppSettings.AutoRefreshMonitoring && _monitoringTimer.IsEnabled)
        {
            _monitoringTimer.Stop();
        }

        AutoMonitoringButtonText = _monitoringTimer.IsEnabled ? L.T("AutoOn") : L.T("AutoOff");
        OnLocalizedTextChanged();
        await LogActivityAsync(L.T("Settings"), "Dashboard", L.T("SettingsSaved"));
    }

    private void OnLocalizedTextChanged()
    {
        OnPropertyChanged(nameof(SelectedServerTitle));
        OnPropertyChanged(nameof(SelectedServerSubtitle));
        OnPropertyChanged(nameof(RemoteFilesSummary));
        OnPropertyChanged(nameof(AutoMonitoringButtonText));
        OnPropertyChanged(nameof(MonitoringStatus));
    }

    [RelayCommand]
    private async Task ClearActivityLogsAsync()
    {
        var confirmed = ConfirmActionRequested?.Invoke("Очистить журнал действий?") ?? false;
        if (!confirmed)
        {
            return;
        }

        await _dashboardData.ClearActivityLogsAsync();
        ActivityLogs.Clear();
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
            SftpOutput = L.T("EnterLocalPath");
            return;
        }

        var folderPath = Directory.Exists(path) ? path : Path.GetDirectoryName(path);

        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            SftpOutput = L.T("LocalFolderNotFound");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folderPath,
                UseShellExecute = true
            });

            SftpOutput = L.Format("OpenedFolder", folderPath);
        }
        catch (Exception ex)
        {
            SftpOutput = L.Format("OpenFolderFailed", ex.Message);
        }
    }

    [RelayCommand]
    private async Task AddServerAsync()
    {
        var server = new ServerProfile
        {
            Port = 22,
            GroupName = SelectedGroupName == "All Servers" ? "Production" : SelectedGroupName
        };

        var viewModel = new ServerEditViewModel(server, L.T("WindowServerAddTitle"));
        var window = new ServerEditWindow(viewModel)
        {
            Owner = Application.Current.MainWindow
        };

        window.ShowDialog();

        if (!viewModel.IsSaved)
        {
            return;
        }

        try
        {
            await _storage.AddAsync(server);
            await LogActivityAsync(L.T("Servers"), server.Name, L.T("ServerAdded"));
            await LoadServersAsync();
            SelectedServer = Servers.FirstOrDefault(x => x.Id == server.Id);
            SshOutput = L.T("ServerAdded");
        }
        catch (Exception ex)
        {
            SshOutput = $"Не удалось сохранить сервер: {ex.Message}";
            TerminalOutput += $"\n{L.Format("ServerSaveError", ex.Message)}\n";
        }
    }

    [RelayCommand]
    private async Task EditServerAsync()
    {
        if (SelectedServer is null)
        {
            SshOutput = L.T("ChooseServerToEdit");
            return;
        }

        var viewModel = new ServerEditViewModel(SelectedServer);
        var window = new ServerEditWindow(viewModel)
        {
            Owner = Application.Current.MainWindow
        };

        window.ShowDialog();

        if (!viewModel.IsSaved)
        {
            return;
        }

        try
        {
            await _storage.UpdateAsync(SelectedServer);
            await LogActivityAsync(L.T("Servers"), SelectedServer.Name, L.T("ServerUpdated"));
            await LoadServersAsync();
            SshOutput = L.T("ServerUpdated");
        }
        catch (Exception ex)
        {
            SshOutput = $"Не удалось обновить сервер: {ex.Message}";
            TerminalOutput += $"\n{L.Format("ServerUpdateError", ex.Message)}\n";
        }
    }

    [RelayCommand]
    private async Task DeleteServerAsync()
    {
        if (SelectedServer is null)
        {
            return;
        }

        var serverName = SelectedServer.Name;
        var confirmed = ConfirmActionRequested?.Invoke($"{L.T("Delete")} {serverName}?") ?? false;

        if (!confirmed)
        {
            return;
        }

        await _storage.DeleteAsync(SelectedServer);
        await LogActivityAsync(L.T("Servers"), serverName, L.Format("ServerDeleted", serverName), "warn");
        await LoadServersAsync();
        SelectedServer = FilteredServers.FirstOrDefault();
        SshOutput = L.Format("ServerDeleted", serverName);
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (SelectedServer is null)
        {
            SshOutput = L.T("ChooseServerFirst");
            return;
        }

        SshOutput = $"{L.T("TestSsh")}: {SelectedServer.Name}";
        var result = await _ssh.TestConnectionAsync(SelectedServer);

        SshOutput = result;
        SelectedServer.IsOnline = result.Equals("Подключение успешно.", StringComparison.OrdinalIgnoreCase);
        OnPropertyChanged(nameof(OnlineText));
        await LogActivityAsync("SSH", SelectedServer.Name, result, SelectedServer.IsOnline ? "info" : "error");
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
        await LogActivityAsync("SSH", "All Servers", L.T("CheckAllServers"));
        OnPropertyChanged(nameof(Servers));
        OnPropertyChanged(nameof(OnlineText));
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
            SshOutput = L.T("ChooseServerFirst");
            MonitoringStatus = L.T("ServerNotSelected");
            return;
        }

        try
        {
            isMonitoringRefreshing = true;
            MonitoringStatus = L.Format("MonitoringRefreshing", SelectedServer.Name);
            MonitorCpu = L.T("MonitoringLoading");
            MonitorLoad = L.T("MonitoringLoading");
            MonitorNetwork = L.T("MonitoringLoading");
            MonitorRam = L.T("MonitoringLoading");
            MonitorDiskUsage = L.T("MonitoringLoading");
            MonitorUptimeShort = L.T("MonitoringLoading");
            MonitorProcesses = L.T("MonitoringLoading");

            var monitoringCommand =
                "echo __CPU__; " +
                "read cpu user nice system idle iowait irq softirq steal guest guest_nice < /proc/stat; " +
                "idle1=$((idle+iowait)); total1=$((user+nice+system+idle+iowait+irq+softirq+steal)); " +
                "net1=$(awk 'NR>2 {gsub(\":\",\"\",$1); if ($1!=\"lo\") {rx+=$2; tx+=$10}} END {print rx+0, tx+0}' /proc/net/dev); " +
                "sleep 1; " +
                "net2=$(awk 'NR>2 {gsub(\":\",\"\",$1); if ($1!=\"lo\") {rx+=$2; tx+=$10}} END {print rx+0, tx+0}' /proc/net/dev); " +
                "read cpu user nice system idle iowait irq softirq steal guest guest_nice < /proc/stat; " +
                "idle2=$((idle+iowait)); total2=$((user+nice+system+idle+iowait+irq+softirq+steal)); " +
                "dt=$((total2-total1)); di=$((idle2-idle1)); " +
                "if [ \"$dt\" -gt 0 ]; then cpu_pct=$(( (100 * ($dt - $di)) / $dt )); else cpu_pct=0; fi; " +
                "printf \"CPU: %s%% used\\nPERCENT:%s\\n\" \"$cpu_pct\" \"$cpu_pct\"; " +
                "echo __LOAD__; " +
                "cpu_count=$(nproc 2>/dev/null || getconf _NPROCESSORS_ONLN 2>/dev/null || echo 1); " +
                "cat /proc/loadavg | awk -v cpus=\"$cpu_count\" '{pct=int(($1 / cpus) * 100); if (pct > 100) pct=100; printf \"Load: %s / %s CPU\\nPERCENT:%d\\n\", $1, cpus, pct}'; " +
                "echo __RAM__; " +
                "free -m | awk '/Mem:/ {printf \"RAM: %d MB / %d MB\\nPERCENT:%d\\n\", $3, $2, ($3 / $2) * 100}'; " +
                "echo __DISK__; " +
                "df -P -m / | awk 'NR==2 {gsub(\"%\", \"\", $5); printf \"Disk /: %d MB / %d MB\\nPERCENT:%d\\n\", $3, $2, $5}'; " +
                "echo __NETWORK__; " +
                "rx1=$(echo \"$net1\" | awk '{print $1}'); tx1=$(echo \"$net1\" | awk '{print $2}'); rx2=$(echo \"$net2\" | awk '{print $1}'); tx2=$(echo \"$net2\" | awk '{print $2}'); " +
                "awk -v rx1=\"$rx1\" -v tx1=\"$tx1\" -v rx2=\"$rx2\" -v tx2=\"$tx2\" 'BEGIN {rx=(rx2-rx1)*8/1000000; tx=(tx2-tx1)*8/1000000; total=rx+tx; pct=int(total); if (pct > 100) pct=100; printf \"RX %.1f Mbps / TX %.1f Mbps\\nPERCENT:%d\\n\", rx, tx, pct}'; " +
                "echo __UPTIME__; uptime -p; " +
                "echo __PROCESSES__; ps -eo pid,comm,%cpu,%mem --sort=-%cpu | head -12";

            var output = await _ssh.RunCommandAsync(SelectedServer, monitoringCommand);

            if (!IsSuccessfulSshResult(output))
            {
                MonitorCpu = output;
                MonitorLoad = output;
                MonitorNetwork = output;
                MonitorRam = output;
                MonitorDiskUsage = output;
                MonitorUptimeShort = output;
                MonitorProcesses = output;
                MonitorCpuPercent = 0;
                MonitorLoadPercent = 0;
                MonitorNetworkPercent = 0;
                MonitorRamPercent = 0;
                MonitorDiskPercent = 0;
                SelectedServer.IsOnline = false;
                MonitoringStatus = L.T("MonitoringError");
                OnPropertyChanged(nameof(OnlineText));
                return;
            }

            var cpuSection = ExtractMonitoringSection(output, "CPU");
            var loadSection = ExtractMonitoringSection(output, "LOAD");
            var ramSection = ExtractMonitoringSection(output, "RAM");
            var diskSection = ExtractMonitoringSection(output, "DISK");
            var networkSection = ExtractMonitoringSection(output, "NETWORK");

            MonitorCpuPercent = ExtractPercentValue(cpuSection);
            MonitorLoadPercent = ExtractPercentValue(loadSection);
            MonitorRamPercent = ExtractPercentValue(ramSection);
            MonitorDiskPercent = ExtractPercentValue(diskSection);
            MonitorNetworkPercent = ExtractPercentValue(networkSection);

            MonitorCpu = RemovePercentLine(cpuSection);
            MonitorLoad = RemovePercentLine(loadSection);
            MonitorRam = RemovePercentLine(ramSection);
            MonitorDiskUsage = RemovePercentLine(diskSection);
            MonitorNetwork = RemovePercentLine(networkSection);
            MonitorUptimeShort = ExtractMonitoringSection(output, "UPTIME");
            MonitorProcesses = ExtractMonitoringSection(output, "PROCESSES");

            AppendMetricHistory();
            SelectedServer.IsOnline = true;
            MonitoringStatus = L.Format("MonitoringUpdated", DateTime.Now.ToString("HH:mm:ss"));
            OnPropertyChanged(nameof(OnlineText));
        }
        finally
        {
            isMonitoringRefreshing = false;
        }
    }

    [RelayCommand]
    private async Task ToggleAutoMonitoringAsync()
    {
        AppSettings.AutoRefreshMonitoring = !_monitoringTimer.IsEnabled;

        if (_monitoringTimer.IsEnabled)
        {
            _monitoringTimer.Stop();
            AutoMonitoringButtonText = L.T("AutoOff");
            MonitoringStatus = L.T("AutoRefreshStopped");
            await SaveSettingsAsync();
            return;
        }

        _monitoringTimer.Start();
        AutoMonitoringButtonText = L.T("AutoOn");
        MonitoringStatus = L.T("AutoRefreshEvery");
        await SaveSettingsAsync();
        await RefreshMonitoringAsync();
    }

    [RelayCommand]
    private async Task LoadLogAsync()
    {
        if (SelectedServer is null)
        {
            LogContent = L.T("ChooseServer");
            return;
        }

        if (string.IsNullOrWhiteSpace(LogPath))
        {
            LogContent = L.T("EnterLogPath");
            return;
        }

        LogContent = L.T("LogLoading");
        var safeLogPath = QuoteShellArgument(LogPath);
        LogContent = await _ssh.RunCommandAsync(SelectedServer, $"tail -n 100 {safeLogPath}");
        ParseRecentLogs(LogContent);
        await LogActivityAsync("Логи", SelectedServer.Name, $"Прочитан лог {LogPath}");
    }

    [RelayCommand]
    private void UseNginxLog() => LogPath = "/var/log/nginx/error.log";

    [RelayCommand]
    private void UseSyslog() => LogPath = "/var/log/syslog";

    [RelayCommand]
    private void UseAuthLog() => LogPath = "/var/log/auth.log";

    [RelayCommand]
    private async Task AddSavedCommandAsync()
    {
        if (string.IsNullOrWhiteSpace(CommandTitle) || string.IsNullOrWhiteSpace(CommandText))
        {
            SshOutput = "Заполни название и команду.";
            return;
        }

        var command = new SavedCommand
        {
            Title = CommandTitle.Trim(),
            Command = CommandText.Trim(),
            Category = "Custom",
            SortOrder = SavedCommands.Count + 1
        };

        await _commandService.AddAsync(command);
        await LogActivityAsync(L.T("Commands"), command.Title, L.T("CommandAdded"));
        CommandTitle = "";
        CommandText = "";
        await LoadSavedCommandsAsync();
    }

    [RelayCommand]
    private async Task UpdateSavedCommandAsync()
    {
        if (SelectedSavedCommand is null)
        {
            SshOutput = L.T("ChooseCommand");
            return;
        }

        if (string.IsNullOrWhiteSpace(CommandTitle) || string.IsNullOrWhiteSpace(CommandText))
        {
            SshOutput = "Заполни название и команду.";
            return;
        }

        SelectedSavedCommand.Title = CommandTitle.Trim();
        SelectedSavedCommand.Command = CommandText.Trim();
        await _commandService.UpdateAsync(SelectedSavedCommand);
        await LogActivityAsync(L.T("Commands"), SelectedSavedCommand.Title, L.T("CommandUpdated"));
        await LoadSavedCommandsAsync();
        SshOutput = L.T("CommandUpdated");
    }

    [RelayCommand]
    private async Task DeleteSavedCommandAsync()
    {
        if (SelectedSavedCommand is null)
        {
            SshOutput = L.T("ChooseCommand");
            return;
        }

        var title = SelectedSavedCommand.Title;
        await _commandService.DeleteAsync(SelectedSavedCommand);
        await LogActivityAsync(L.T("Commands"), title, L.T("CommandDeleted"), "warn");
        await LoadSavedCommandsAsync();
    }

    [RelayCommand]
    private async Task RunSavedCommandAsync()
    {
        if (SelectedSavedCommand is null)
        {
            SshOutput = L.T("ChooseSavedCommand");
            return;
        }

        await RunServerCommandAsync(SelectedSavedCommand.Command);
        await LogActivityAsync(L.T("Commands"), SelectedSavedCommand.Title, L.T("Run"));
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
    private async Task RunUptimeAsync() => await RunServerCommandAsync("uptime");

    [RelayCommand]
    private async Task RunDiskAsync() => await RunServerCommandAsync("df -h");

    [RelayCommand]
    private async Task RunMemoryAsync() => await RunServerCommandAsync("free -h");

    [RelayCommand]
    private async Task RunProcessesAsync() => await RunServerCommandAsync("ps aux --sort=-%cpu | head -20");

    [RelayCommand]
    private void ClearTerminal()
    {
        TerminalOutput = SelectedServer is null
            ? L.T("TerminalWelcome")
            : $"Connected target: {SelectedServer.Username}@{SelectedServer.Host}\n";
    }
    [RelayCommand]
    private async Task SendTerminalCommandAsync()
    {
        if (SelectedServer is null)
        {
            TerminalOutput += $"\n{L.T("ChooseServerFirst")}\n";
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
                TerminalOutput += $"\n{L.T("DangerousCommandCancelled")}\n";
                return;
            }
        }

        var command = TerminalCommand;

        try
        {
            _terminalSsh.ConnectShell(SelectedServer);
            var result = await Task.Run(() => _terminalSsh.SendShellCommand(command));
            TerminalOutput += $"\nroot@{SelectedServer.Name}:~# {command}\n{result}\n";
            TerminalCommand = "";
            await LogActivityAsync("Терминал", SelectedServer.Name, command);
        }
        catch (Exception ex)
        {
            SelectedServer.IsOnline = false;
            TerminalOutput += $"\n{L.Format("TerminalError", ex.Message)}\n";
            await LogActivityAsync("Терминал", SelectedServer.Name, ex.Message, "error");
        }
    }

    [RelayCommand]
    private async Task DownloadSelectedRemoteFileAsync()
    {
        if (SelectedServer is null)
        {
            SftpOutput = L.T("ChooseServerFirst");
            return;
        }

        if (SelectedRemoteFile is null)
        {
            SftpOutput = "Выбери файл на сервере для скачивания.";
            return;
        }

        if (SelectedRemoteFile.IsDirectory)
        {
            SftpOutput = "Папку скачать нельзя. Выбери файл.";
            return;
        }

        var localPath = _fileDialog.PickSaveFile();

        if (string.IsNullOrWhiteSpace(localPath))
        {
            return;
        }

        RemoteFilePath = SelectedRemoteFile.FullPath;
        LocalFilePath = localPath;

        SftpOutput = L.T("DownloadInProgress");
        SftpOutput = await _ssh.DownloadFileAsync(
            SelectedServer,
            SelectedRemoteFile.FullPath,
            localPath
        );

        await LogActivityAsync("SFTP", SelectedServer.Name, $"Скачивание: {SelectedRemoteFile.FullPath}");
    }

    [RelayCommand]
    private async Task LoadRemoteFilesAsync()
    {
        if (SelectedServer is null)
        {
            SftpOutput = L.T("ChooseServer");
            return;
        }

        RemoteFiles.Clear();

        try
        {
            var files = await _ssh.GetFilesAsync(SelectedServer, RemoteFolderPath);

            foreach (var file in files)
            {
                RemoteFiles.Add(file);
            }

            SftpOutput = L.Format("RemoteFilesLoaded", files.Count);
            SelectedServer.IsOnline = true;
            OnPropertyChanged(nameof(RemoteFilesSummary));
        }
        catch (Exception ex)
        {
            SftpOutput = ex.Message;
        }
    }

    [RelayCommand]
    private async Task GoRootRemoteFolderAsync()
    {
        RemoteFolderPath = "/";
        await LoadRemoteFilesAsync();
    }

    [RelayCommand]
    private async Task GoBackRemoteFolderAsync()
    {
        if (string.IsNullOrWhiteSpace(RemoteFolderPath) || RemoteFolderPath.TrimEnd('/') == "")
        {
            RemoteFolderPath = "/";
            await LoadRemoteFilesAsync();
            return;
        }

        var currentPath = RemoteFolderPath.TrimEnd('/');

        if (currentPath == "/")
        {
            await LoadRemoteFilesAsync();
            return;
        }

        var lastSlashIndex = currentPath.LastIndexOf('/');
        RemoteFolderPath = lastSlashIndex <= 0 ? "/" : currentPath[..lastSlashIndex];
        await LoadRemoteFilesAsync();
    }

    [RelayCommand]
    private async Task OpenSelectedRemoteItemAsync()
    {
        if (SelectedRemoteFile is null)
        {
            return;
        }

        if (SelectedRemoteFile.IsDirectory)
        {
            RemoteFolderPath = SelectedRemoteFile.FullPath;
            await LoadRemoteFilesAsync();
            return;
        }

        RemoteFilePath = SelectedRemoteFile.FullPath;
        SftpOutput = L.Format("SelectedFile", SelectedRemoteFile.FullPath);
    }

    [RelayCommand]
    private void EditRemoteFile()
    {
        if (SelectedServer is null)
        {
            SftpOutput = L.T("ChooseServerFirst");
            return;
        }

        if (SelectedRemoteFile is null)
        {
            SftpOutput = L.T("ChooseFileForEdit");
            return;
        }

        if (SelectedRemoteFile.IsDirectory)
        {
            SftpOutput = "Папку нельзя открыть в редакторе.";
            return;
        }

        var viewModel = new RemoteFileEditorViewModel(SelectedServer, SelectedRemoteFile.FullPath, _ssh);
        var window = new RemoteFileEditorWindow(viewModel)
        {
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    [RelayCommand]
    private async Task DownloadFileAsync()
    {
        if (SelectedServer is null)
        {
            SftpOutput = L.T("ChooseServerFirst");
            return;
        }

        if (string.IsNullOrWhiteSpace(RemoteFilePath) || string.IsNullOrWhiteSpace(LocalFilePath))
        {
            SftpOutput = L.T("EnterPaths");
            return;
        }

        var targetLocalPath = ResolveDownloadLocalPath(LocalFilePath, RemoteFilePath);
        LocalFilePath = targetLocalPath;
        SftpOutput = L.T("DownloadInProgress");
        SftpOutput = await _ssh.DownloadFileAsync(SelectedServer, RemoteFilePath, targetLocalPath);
        await LogActivityAsync("SFTP", SelectedServer.Name, $"Скачивание: {RemoteFilePath}");
    }

    [RelayCommand]
    private async Task UploadFileAsync()
    {
        if (SelectedServer is null)
        {
            SftpOutput = L.T("ChooseServerFirst");
            return;
        }

        if (string.IsNullOrWhiteSpace(LocalFilePath) || string.IsNullOrWhiteSpace(RemoteFilePath))
        {
            SftpOutput = L.T("EnterRemoteAndLocalPath");
            return;
        }

        SftpOutput = L.T("UploadInProgress");
        SftpOutput = await _ssh.UploadFileAsync(SelectedServer, LocalFilePath, RemoteFilePath);
        await LogActivityAsync("SFTP", SelectedServer.Name, L.Format("UploadingFile", RemoteFilePath));
    }

    public async Task UploadLocalFileToCurrentFolderAsync(string localPath)
    {
        if (SelectedServer is null)
        {
            SftpOutput = L.T("ChooseServerFirst");
            return;
        }

        if (!File.Exists(localPath))
        {
            SftpOutput = L.Format("LocalFileNotFound", localPath);
            return;
        }

        var remotePath = CombineRemotePath(RemoteFolderPath, Path.GetFileName(localPath));
        LocalFilePath = localPath;
        RemoteFilePath = remotePath;
        SftpOutput = L.Format("UploadingFile", Path.GetFileName(localPath));
        SftpOutput = await _ssh.UploadFileAsync(SelectedServer, localPath, remotePath);
        await LogActivityAsync("SFTP", SelectedServer.Name, $"Drag&drop загрузка: {remotePath}");
        await LoadRemoteFilesAsync();
    }

    public async Task CreateRemoteFolderAsync(string folderName)
    {
        if (SelectedServer is null)
        {
            SftpOutput = L.T("ChooseServerFirst");
            return;
        }

        if (!ValidateRemoteItemName(folderName, out var error))
        {
            SftpOutput = error;
            return;
        }

        var remotePath = CombineRemotePath(RemoteFolderPath, folderName.Trim());
        SftpOutput = L.T("FolderCreating");
        SftpOutput = await _ssh.CreateRemoteDirectoryAsync(SelectedServer, remotePath);
        await LogActivityAsync("SFTP", SelectedServer.Name, $"{L.T("CreateFolder")}: {remotePath}");
        await LoadRemoteFilesAsync();
    }

    public async Task RenameSelectedRemoteItemAsync(string newName)
    {
        if (SelectedServer is null)
        {
            SftpOutput = L.T("ChooseServerFirst");
            return;
        }

        if (SelectedRemoteFile is null)
        {
            SftpOutput = L.T("ChooseFileOrFolderForRename");
            return;
        }

        if (!ValidateRemoteItemName(newName, out var error))
        {
            SftpOutput = error;
            return;
        }

        var newPath = CombineRemotePath(GetRemoteParentPath(SelectedRemoteFile.FullPath), newName.Trim());
        SftpOutput = L.T("Renaming");
        SftpOutput = await _ssh.RenameRemoteItemAsync(SelectedServer, SelectedRemoteFile.FullPath, newPath);
        await LogActivityAsync("SFTP", SelectedServer.Name, $"Переименовано: {newPath}");
        await LoadRemoteFilesAsync();
    }

    [RelayCommand]
    private async Task DeleteSelectedRemoteItemAsync()
    {
        if (SelectedServer is null)
        {
            SftpOutput = L.T("ChooseServerFirst");
            return;
        }

        if (SelectedRemoteFile is null)
        {
            SftpOutput = L.T("ChooseFileOrFolderForDelete");
            return;
        }

        SftpOutput = L.T("Delete");
        SftpOutput = await _ssh.DeleteRemoteItemAsync(SelectedServer, SelectedRemoteFile.FullPath, SelectedRemoteFile.IsDirectory);
        await LogActivityAsync("SFTP", SelectedServer.Name, $"{L.T("Delete")}: {SelectedRemoteFile.FullPath}", "warn");
        await LoadRemoteFilesAsync();
    }

    private async Task RunServerCommandAsync(string command)
    {
        if (SelectedServer is null)
        {
            SshOutput = L.T("ChooseServerFirst");
            return;
        }

        if (RequiresCommandConfirmation(command))
        {
            var confirmed = ConfirmCommandRequested?.Invoke(command) ?? false;
            if (!confirmed)
            {
                SshOutput = L.T("DangerousCommandCancelled");
                return;
            }
        }

        SshOutput = L.Format("RunningCommand", command);
        SshOutput = await _ssh.RunCommandAsync(SelectedServer, command);
        TerminalOutput += $"\nroot@{SelectedServer.Name}:~# {command}\n{SshOutput}\n";
        SelectedServer.IsOnline = IsSuccessfulSshResult(SshOutput);
        OnPropertyChanged(nameof(OnlineText));
        await LogActivityAsync("SSH", SelectedServer.Name, command, SelectedServer.IsOnline ? "info" : "error");
    }

    private async Task LoadServersAsync()
    {
        var selectedId = SelectedServer?.Id;
        Servers.Clear();

        var servers = await _storage.GetAllAsync();

        foreach (var server in servers)
        {
            if (string.IsNullOrWhiteSpace(server.GroupName))
            {
                server.GroupName = "Production";
            }

            Servers.Add(server);
        }

        ApplyServerFilter();
        RefreshDashboardCollections();
        SelectedServer = selectedId.HasValue
            ? Servers.FirstOrDefault(x => x.Id == selectedId.Value) ?? FilteredServers.FirstOrDefault()
            : SelectedServer;
    }

    private async Task LoadSavedCommandsAsync()
    {
        SavedCommands.Clear();
        FavoriteCommands.Clear();

        var commands = await _commandService.GetAllAsync();

        if (commands.Count == 0)
        {
            commands = GetDefaultSavedCommands();
            await _commandService.AddRangeAsync(commands);
        }

        foreach (var command in commands)
        {
            SavedCommands.Add(command);

            if (command.IsFavorite)
            {
                FavoriteCommands.Add(command);
            }
        }
    }

    private async Task LoadActivityLogsAsync()
    {
        ActivityLogs.Clear();

        foreach (var entry in await _dashboardData.GetActivityLogsAsync())
        {
            ActivityLogs.Add(entry);
        }
    }

    private void ApplyServerFilter()
    {
        FilteredServers.Clear();

        var query = ServerSearchText.Trim();
        var servers = Servers.AsEnumerable();

        if (!string.Equals(SelectedGroupName, "All Servers", StringComparison.OrdinalIgnoreCase))
        {
            servers = servers.Where(server => string.Equals(server.GroupName, SelectedGroupName, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            servers = servers.Where(server => MatchesServerSearch(server, query));
        }

        foreach (var server in servers)
        {
            FilteredServers.Add(server);
        }

        if (SelectedServer is not null && !FilteredServers.Contains(SelectedServer))
        {
            SelectedServer = FilteredServers.FirstOrDefault();
        }
    }

    private void RefreshDashboardCollections()
    {
        FavoriteServers.Clear();

        foreach (var server in Servers.Where(x => x.IsFavorite))
        {
            FavoriteServers.Add(server);
        }

        Groups.Clear();
        Groups.Add(new DashboardGroup
        {
            Name = "All Servers",
            Count = Servers.Count,
            IsSelected = SelectedGroupName == "All Servers"
        });

        foreach (var group in Servers.GroupBy(x => string.IsNullOrWhiteSpace(x.GroupName) ? "Production" : x.GroupName).OrderBy(x => x.Key))
        {
            Groups.Add(new DashboardGroup
            {
                Name = group.Key,
                Count = group.Count(),
                IsSelected = string.Equals(group.Key, SelectedGroupName, StringComparison.OrdinalIgnoreCase)
            });
        }

        foreach (var defaultGroup in new[] { "Production", "Staging", "Development" })
        {
            if (Groups.All(x => !string.Equals(x.Name, defaultGroup, StringComparison.OrdinalIgnoreCase)))
            {
                Groups.Add(new DashboardGroup
                {
                    Name = defaultGroup,
                    Count = 0,
                    IsSelected = string.Equals(defaultGroup, SelectedGroupName, StringComparison.OrdinalIgnoreCase)
                });
            }
        }

        RefreshGroupSelection();
        OnPropertyChanged(nameof(ServerCount));
    }

    private void RefreshGroupSelection()
    {
        foreach (var group in Groups)
        {
            group.IsSelected = string.Equals(group.Name, SelectedGroupName, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void AppendMetricHistory()
    {
        AppendMetricValue(cpuHistory, MonitorCpuPercent);
        AppendMetricValue(ramHistory, MonitorRamPercent);
        AppendMetricValue(diskHistory, MonitorDiskPercent);
        AppendMetricValue(networkHistory, MonitorNetworkPercent);

        SeedMetricHistory(cpuHistory);
        SeedMetricHistory(ramHistory);
        SeedMetricHistory(diskHistory);
        SeedMetricHistory(networkHistory);

        CpuSparkline = BuildSparkline(cpuHistory);
        RamSparkline = BuildSparkline(ramHistory);
        DiskSparkline = BuildSparkline(diskHistory);
        NetworkSparkline = BuildSparkline(networkHistory);
        CpuHistory = cpuHistory.ToArray();
        RamHistory = ramHistory.ToArray();
        DiskHistory = diskHistory.ToArray();
        NetworkHistory = networkHistory.ToArray();
        OnPropertyChanged(nameof(CpuHistory));
        OnPropertyChanged(nameof(RamHistory));
        OnPropertyChanged(nameof(DiskHistory));
        OnPropertyChanged(nameof(NetworkHistory));
    }

    private static void AppendMetricValue(List<double> history, double value)
    {
        history.Add(Math.Clamp(value, 0, 100));

        if (history.Count > MetricHistoryLimit)
        {
            history.RemoveAt(0);
        }
    }

    private static void SeedMetricHistory(List<double> history)
    {
        if (history.Count != 1)
        {
            return;
        }

        var value = history[0];
        while (history.Count < 12)
        {
            history.Add(value);
        }
    }

    private static string BuildSparkline(IEnumerable<double> values)
    {
        const string levels = "▁▂▃▄▅▆▇█";
        var result = string.Concat(values.Select(value =>
        {
            var index = (int)Math.Round(Math.Clamp(value, 0, 100) / 100 * (levels.Length - 1));
            return levels[index];
        }));

        return string.IsNullOrWhiteSpace(result) ? "▁▁▁▁▁▁▁▁" : result;
    }

    private void ParseRecentLogs(string content)
    {
        RecentLogs.Clear();

        var lines = content
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .TakeLast(8)
            .Reverse();

        foreach (var line in lines)
        {
            RecentLogs.Add(ParseRecentLogLine(line));
        }
    }

    private static RecentLogEntry ParseRecentLogLine(string line)
    {
        var levelMatch = Regex.Match(line, @"\[(?<level>error|warn|warning|info|notice)\]", RegexOptions.IgnoreCase);
        var level = levelMatch.Success
            ? NormalizeLogLevel(levelMatch.Groups["level"].Value)
            : line.Contains("error", StringComparison.OrdinalIgnoreCase)
            ? "error"
            : line.Contains("warn", StringComparison.OrdinalIgnoreCase)
                ? "warn"
                : "info";

        var timeMatch = Regex.Match(line, @"\b\d{2}:\d{2}:\d{2}\b");
        var serviceMatch = Regex.Match(line, @"\b(nginx|php-fpm|systemd|kernel|ssh|sshd|docker)\b", RegexOptions.IgnoreCase);
        var nginxRequestMatch = Regex.Match(line, "request: \"(?<method>[A-Z]+) (?<path>[^\\s\"]+)", RegexOptions.IgnoreCase);
        var missingFileMatch = Regex.Match(line, "open\\(\\) \"(?<path>[^\"]+)\" failed \\(2: No such file or directory\\)", RegexOptions.IgnoreCase);

        if (nginxRequestMatch.Success || missingFileMatch.Success)
        {
            var request = nginxRequestMatch.Success
                ? $"{nginxRequestMatch.Groups["method"].Value} {nginxRequestMatch.Groups["path"].Value}"
                : missingFileMatch.Groups["path"].Value;

            return new RecentLogEntry
            {
                Time = timeMatch.Success ? timeMatch.Value : DateTime.Now.ToString("HH:mm:ss"),
                Service = "nginx",
                Level = level,
                Message = missingFileMatch.Success ? $"{request} -> файл не найден" : request
            };
        }

        var message = line.Length > 96 ? $"{line[..96]}..." : line;

        return new RecentLogEntry
        {
            Time = timeMatch.Success ? timeMatch.Value : DateTime.Now.ToString("HH:mm:ss"),
            Service = serviceMatch.Success ? serviceMatch.Value.ToLowerInvariant() : "system",
            Level = level,
            Message = message
        };
    }

    private static string NormalizeLogLevel(string value)
    {
        return value.Equals("warning", StringComparison.OrdinalIgnoreCase) ? "warn" : value.ToLowerInvariant();
    }

    private async Task LogActivityAsync(string action, string target, string details = "", string level = "info")
    {
        try
        {
            await _dashboardData.AddActivityAsync(action, target, details, level);
            await LoadActivityLogsAsync();
        }
        catch (Exception ex)
        {
            TerminalOutput += $"\nActivity log unavailable: {ex.Message}\n";
        }
    }

    private async Task RefreshMonitoringFromTimerAsync()
    {
        if (SelectedServer is null)
        {
            return;
        }

        await RefreshMonitoringAsync();
    }

    private static List<SavedCommand> GetDefaultSavedCommands()
    {
        return new List<SavedCommand>
        {
            new() { Title = "Update System", Command = "apt update && apt upgrade -y", Category = "System", IsFavorite = true, SortOrder = 10 },
            new() { Title = "Restart Nginx", Command = "systemctl restart nginx", Category = "Nginx", IsFavorite = true, SortOrder = 20 },
            new() { Title = "Restart PHP-FPM", Command = "systemctl restart php8.1-fpm", Category = "PHP", SortOrder = 30 },
            new() { Title = "Clear Cache", Command = "rm -rf /var/cache/*", Category = "System", SortOrder = 40 },
            new() { Title = "Check Disk Space", Command = "df -h", Category = "Disk", IsFavorite = true, SortOrder = 50 },
            new() { Title = "Docker: контейнеры", Command = "docker ps --format 'table {{.Names}}\\t{{.Status}}\\t{{.Ports}}'", Category = "Docker", SortOrder = 60 },
            new() { Title = "Docker: ресурсы", Command = "docker stats --no-stream", Category = "Docker", SortOrder = 70 },
            new() { Title = "Система: failed services", Command = "systemctl --failed --no-pager", Category = "System", SortOrder = 80 },
            new() { Title = "Система: journal ошибки", Command = "journalctl -p err -n 80 --no-pager", Category = "Logs", SortOrder = 90 },
            new() { Title = "Система: сведения", Command = "uname -a; cat /etc/os-release 2>/dev/null", Category = "System", SortOrder = 100 }
        };
    }

    [RelayCommand]
    private async Task UploadFileToCurrentFolderAsync()
    {
        if (SelectedServer is null)
        {
            SftpOutput = L.T("ChooseServerFirst");
            return;
        }

        var localPath = _fileDialog.PickFile();

        if (string.IsNullOrWhiteSpace(localPath))
        {
            return;
        }

        if (!File.Exists(localPath))
        {
            SftpOutput = L.Format("LocalFileNotFound", localPath);
            return;
        }

        var remotePath = CombineRemotePath(RemoteFolderPath, Path.GetFileName(localPath));

        LocalFilePath = localPath;
        RemoteFilePath = remotePath;

        SftpOutput = L.T("UploadInProgress");
        SftpOutput = await _ssh.UploadFileAsync(SelectedServer, localPath, remotePath);

        await LogActivityAsync("SFTP", SelectedServer.Name, $"Загрузка: {remotePath}");
        await LoadRemoteFilesAsync();
    }
    private static bool MatchesServerSearch(ServerProfile server, string query)
    {
        return Contains(server.Name, query) ||
            Contains(server.Host, query) ||
            Contains(server.Username, query) ||
            Contains(server.Notes, query) ||
            Contains(server.GroupName, query) ||
            Contains(server.OsName, query) ||
            Contains(server.IpAddressDisplay, query);
    }

    private static bool Contains(string? value, string query)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveDownloadLocalPath(string localPath, string remotePath)
    {
        if (!Directory.Exists(localPath))
        {
            return localPath;
        }

        var remoteFileName = remotePath.TrimEnd('/').Split('/').LastOrDefault();
        return string.IsNullOrWhiteSpace(remoteFileName) ? localPath : Path.Combine(localPath, remoteFileName);
    }

    private static string CreateInitials(string displayName, string fallback)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return string.IsNullOrWhiteSpace(fallback) ? "AD" : fallback.Trim().ToUpperInvariant();
        }

        var initials = string.Concat(displayName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(2)
            .Select(x => char.ToUpperInvariant(x[0])));

        return string.IsNullOrWhiteSpace(initials) ? "AD" : initials;
    }

    private static bool RequiresCommandConfirmation(string command) => ApplicationRules.RequiresCommandConfirmation(command);
    private static string ExtractMonitoringSection(string output, string sectionName) => ApplicationRules.ExtractMonitoringSection(output, sectionName);
    private static double ExtractPercentValue(string section) => ApplicationRules.ExtractPercentValue(section);
    private static string RemovePercentLine(string section) => ApplicationRules.RemovePercentLine(section);
    private static bool IsSuccessfulSshResult(string result) => ApplicationRules.IsSuccessfulSshResult(result);
    private static string GetRemoteParentPath(string remotePath) => ApplicationRules.GetRemoteParentPath(remotePath);
    private static bool ValidateRemoteItemName(string name, out string error) => ApplicationRules.ValidateRemoteItemName(name, out error);
    private static string CombineRemotePath(string remoteFolderPath, string fileName) => ApplicationRules.CombineRemotePath(remoteFolderPath, fileName);
    private static string QuoteShellArgument(string value) => $"'{value.Replace("'", "'\\''")}'";

    public void Dispose()
    {
        _monitoringTimer.Stop();
        _terminalSsh.Dispose();
    }
}
