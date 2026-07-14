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

    private CancellationTokenSource serverSelectionCancellation = new();
    private CancellationTokenSource? commandCancellation;
    private int serverSelectionVersion;

    [ObservableProperty]
    private bool isMonitoringLoading;

    [ObservableProperty]
    private bool isFilesLoading;

    [ObservableProperty]
    private bool isLogLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCurrentOperationCommand))]
    private bool isCommandRunning;

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
    private string overlayTitle = "";

    [ObservableProperty]
    private string monitoringIntervalInput = "30";

    [ObservableProperty]
    private string settingsStatusMessage = "";


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
    public string OnlineText => L.T(SelectedServer?.IsOnline == true ? "Online" : "Offline");
    public int ServerCount => Servers.Count;
    public bool HasFilteredServers => FilteredServers.Count > 0;
    public bool HasRemoteFiles => RemoteFiles.Count > 0;
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
            MonitoringIntervalInput = AppSettings.MonitoringIntervalSeconds.ToString();
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
        }
        catch (Exception ex)
        {
            SshOutput = L.Format("InitializationError", ex.Message);
            SftpOutput = SshOutput;
        }
    }

    private async Task LoadSelectedServerDataAsync(ServerProfile server, int version, CancellationToken cancellationToken)
    {
        try
        {
            await Task.WhenAll(RefreshMonitoringAsync(), LoadRemoteFilesAsync(), LoadLogAsync());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newly selected server owns the UI now.
        }

        if (!IsCurrentServer(server, version))
        {
            return;
        }
    }

    partial void OnServerSearchTextChanged(string value) => ApplyServerFilter();

    partial void OnSelectedGroupNameChanged(string value) => ApplyServerFilter();

    partial void OnSelectedServerChanged(ServerProfile? value)
    {
        serverSelectionCancellation.Cancel();
        serverSelectionCancellation.Dispose();
        serverSelectionCancellation = new CancellationTokenSource();
        serverSelectionVersion++;
        commandCancellation?.Cancel();
        IsMonitoringLoading = false;
        IsFilesLoading = false;
        IsLogLoading = false;
        _terminalSsh.DisconnectShell();
        SelectedRemoteFile = null;
        RemoteFiles.Clear();
        LogContent = "";
        SftpOutput = value is null ? L.T("ChooseServer") : L.T("OpenFilesHint");
        OnPropertyChanged(nameof(SelectedServerTitle));
        OnPropertyChanged(nameof(SelectedServerSubtitle));
        OnPropertyChanged(nameof(SelectedServerAddress));
        OnPropertyChanged(nameof(SelectedServerOs));
        OnPropertyChanged(nameof(OnlineText));
        OnPropertyChanged(nameof(RemoteFilesSummary));
        OnPropertyChanged(nameof(HasRemoteFiles));
        NotifySelectedServerCommands();

        if (value is null)
        {
            return;
        }

        MonitoringStatus = value.Name;
        TerminalOutput = L.Format("ConnectedTarget", $"{value.Username}@{value.Host}") + Environment.NewLine;
        _ = LoadSelectedServerDataAsync(value, serverSelectionVersion, serverSelectionCancellation.Token);
    }

    partial void OnSelectedSavedCommandChanged(SavedCommand? value)
    {
        NotifySelectedSavedCommandCommands();

        if (value is null)
        {
            return;
        }

        CommandTitle = value.Title;
        CommandText = value.Command;
    }

    partial void OnSelectedRemoteFileChanged(RemoteFileItem? value)
    {
        NotifySelectedRemoteFileCommands();

        if (value is { IsDirectory: false })
        {
            RemoteFilePath = value.FullPath;
        }
    }

    partial void OnTerminalCommandChanged(string value) => SendTerminalCommandCommand.NotifyCanExecuteChanged();

    private bool CanUseSelectedServer() => SelectedServer is not null;

    private bool CanCheckServers() => Servers.Count > 0;

    private bool CanUseSelectedSavedCommand() => SelectedSavedCommand is not null;

    private bool CanUseSelectedRemoteItem() => SelectedServer is not null && SelectedRemoteFile is not null;

    private bool CanUseSelectedRemoteFile() =>
        SelectedServer is not null && SelectedRemoteFile is { IsDirectory: false };

    private bool CanSendTerminalCommand() =>
        SelectedServer is not null && !string.IsNullOrWhiteSpace(TerminalCommand);

    private void NotifySelectedServerCommands()
    {
        ToggleSelectedServerFavoriteCommand.NotifyCanExecuteChanged();
        EditServerCommand.NotifyCanExecuteChanged();
        DeleteServerCommand.NotifyCanExecuteChanged();
        TestConnectionCommand.NotifyCanExecuteChanged();
        RefreshMonitoringCommand.NotifyCanExecuteChanged();
        LoadLogCommand.NotifyCanExecuteChanged();
        UseNginxLogCommand.NotifyCanExecuteChanged();
        UseSyslogCommand.NotifyCanExecuteChanged();
        UseAuthLogCommand.NotifyCanExecuteChanged();
        LoadRemoteFilesCommand.NotifyCanExecuteChanged();
        GoRootRemoteFolderCommand.NotifyCanExecuteChanged();
        GoBackRemoteFolderCommand.NotifyCanExecuteChanged();
        UploadFileToCurrentFolderCommand.NotifyCanExecuteChanged();
        SendTerminalCommandCommand.NotifyCanExecuteChanged();
        NotifySelectedRemoteFileCommands();
    }

    private void NotifySelectedRemoteFileCommands()
    {
        OpenSelectedRemoteItemCommand.NotifyCanExecuteChanged();
        EditRemoteFileCommand.NotifyCanExecuteChanged();
        DownloadSelectedRemoteFileCommand.NotifyCanExecuteChanged();
        DeleteSelectedRemoteItemCommand.NotifyCanExecuteChanged();
    }

    private void NotifySelectedSavedCommandCommands()
    {
        ToggleSavedCommandFavoriteCommand.NotifyCanExecuteChanged();
        UpdateSavedCommandCommand.NotifyCanExecuteChanged();
        DeleteSavedCommandCommand.NotifyCanExecuteChanged();
        RunSavedCommandCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void SelectGroup(string? groupName)
    {
        SelectedGroupName = string.IsNullOrWhiteSpace(groupName) ? "All Servers" : groupName;
        RefreshGroupSelection();
    }

    [RelayCommand(CanExecute = nameof(CanUseSelectedServer))]
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

    [RelayCommand(CanExecute = nameof(CanUseSelectedSavedCommand))]
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
        IsSettingsPanelOpen = false;
        IsFavoritesPanelOpen = false;
        IsActivityPanelOpen = false;
        OverlayTitle = "";
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        SettingsStatusMessage = "";

        if (!int.TryParse(MonitoringIntervalInput, out var interval) || interval < 5 || interval > 3600)
        {
            SettingsStatusMessage = L.T("MonitoringIntervalValidation");
            return;
        }

        var remoteFolder = AppSettings.DefaultRemoteFolder?.Trim() ?? "";
        var logPath = AppSettings.DefaultLogPath?.Trim() ?? "";

        if (!IsValidRemotePath(remoteFolder) || !IsValidRemotePath(logPath))
        {
            SettingsStatusMessage = L.T("RemotePathValidation");
            return;
        }

        AppSettings.MonitoringIntervalSeconds = interval;
        AppSettings.DefaultRemoteFolder = remoteFolder;
        AppSettings.DefaultLogPath = logPath;
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
        SettingsStatusMessage = L.T("SettingsSaved");
        await LogActivityAsync(L.T("Settings"), "Dashboard", L.T("SettingsSaved"));
    }
    private void OnLocalizedTextChanged()
    {
        OnPropertyChanged(nameof(SelectedServerTitle));
        OnPropertyChanged(nameof(SelectedServerSubtitle));
        OnPropertyChanged(nameof(RemoteFilesSummary));
        OnPropertyChanged(nameof(AutoMonitoringButtonText));
        OnPropertyChanged(nameof(MonitoringStatus));
        RefreshDashboardCollections();
    }
    [RelayCommand]
    private async Task ClearActivityLogsAsync()
    {
        var confirmed = ConfirmActionRequested?.Invoke(L.T("ClearActivityPrompt")) ?? false;
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
            SshOutput = L.Format("ServerSaveError", ex.Message);
            TerminalOutput += $"\n{L.Format("ServerSaveError", ex.Message)}\n";
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseSelectedServer))]
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
            SshOutput = L.Format("ServerUpdateError", ex.Message);
            TerminalOutput += $"\n{L.Format("ServerUpdateError", ex.Message)}\n";
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseSelectedServer))]
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

    [RelayCommand(CanExecute = nameof(CanUseSelectedServer))]
    private async Task TestConnectionAsync()
    {
        if (SelectedServer is null)
        {
            SshOutput = L.T("ChooseServerFirst");
            return;
        }

        SshOutput = $"{L.T("TestSsh")}: {SelectedServer.Name}";
        var result = await _ssh.TestConnectionAsync(SelectedServer);

        SshOutput = result.Message;
        SelectedServer.IsOnline = result.Succeeded;
        OnPropertyChanged(nameof(OnlineText));
        await LogActivityAsync("SSH", SelectedServer.Name, result.Message, SelectedServer.IsOnline ? "info" : "error");
    }

    [RelayCommand(CanExecute = nameof(CanCheckServers))]
    private async Task CheckAllServersAsync()
    {
        using var concurrency = new SemaphoreSlim(5);

        var checks = Servers.Select(async server =>
        {
            await concurrency.WaitAsync();

            try
            {
                var result = await _ssh.RunCommandAsync(server, "echo ok");
                server.IsOnline = result.Succeeded;
            }
            finally
            {
                concurrency.Release();
            }
        });

        await Task.WhenAll(checks);
        await LogActivityAsync("SSH", L.T("AllServers"), L.T("CheckAllServers"));
        OnPropertyChanged(nameof(Servers));
        OnPropertyChanged(nameof(OnlineText));
    }

    [RelayCommand(CanExecute = nameof(CanUseSelectedServer))]
    private async Task RefreshMonitoringAsync()
    {
        if (IsMonitoringLoading)
        {
            return;
        }

        var server = SelectedServer;

        if (server is null)
        {
            SshOutput = L.T("ChooseServerFirst");
            MonitoringStatus = L.T("ServerNotSelected");
            return;
        }

        var version = serverSelectionVersion;
        var cancellationToken = serverSelectionCancellation.Token;

        try
        {
            IsMonitoringLoading = true;
            MonitoringStatus = L.Format("MonitoringRefreshing", server.Name);
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

            var result = await _ssh.RunCommandAsync(server, monitoringCommand, cancellationToken);
            if (!IsCurrentServer(server, version))
            {
                return;
            }

            var output = string.IsNullOrWhiteSpace(result.Output) ? result.Message : result.Output;

            if (!result.Succeeded)
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
                server.IsOnline = false;
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
            server.IsOnline = true;
            MonitoringStatus = L.Format("MonitoringUpdated", DateTime.Now.ToString("HH:mm:ss"));
            OnPropertyChanged(nameof(OnlineText));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            if (!IsCurrentServer(server, version))
            {
                return;
            }

            server.IsOnline = false;
            MonitoringStatus = L.Format("MonitoringErrorDetails", ex.Message);
            SshOutput = MonitoringStatus;
            OnPropertyChanged(nameof(OnlineText));
        }
        finally
        {
            if (IsCurrentServer(server, version))
            {
                IsMonitoringLoading = false;
            }
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

    [RelayCommand(CanExecute = nameof(CanUseSelectedServer))]
    private async Task LoadLogAsync()
    {
        var server = SelectedServer;

        if (server is null)
        {
            LogContent = L.T("ChooseServer");
            return;
        }

        if (IsLogLoading)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(LogPath))
        {
            LogContent = L.T("EnterLogPath");
            return;
        }

        var version = serverSelectionVersion;
        var cancellationToken = serverSelectionCancellation.Token;
        var requestedPath = LogPath;

        try
        {
            IsLogLoading = true;
            LogContent = L.T("LogLoading");
            var safeLogPath = QuoteShellArgument(requestedPath);
            var result = await _ssh.RunCommandAsync(server, $"tail -n 100 {safeLogPath}", cancellationToken);

            if (!IsCurrentServer(server, version))
            {
                return;
            }

            LogContent = string.IsNullOrWhiteSpace(result.Output) ? result.Message : result.Output;

            if (result.Succeeded)
            {
                ParseRecentLogs(LogContent);
                await LogActivityAsync(L.T("Logs"), server.Name, L.Format("LogRead", requestedPath));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        finally
        {
            if (IsCurrentServer(server, version))
            {
                IsLogLoading = false;
            }
        }
    }
    [RelayCommand(CanExecute = nameof(CanUseSelectedServer))]
    private async Task UseNginxLogAsync()
    {
        LogPath = "/var/log/nginx/error.log";
        await LoadLogAsync();
    }

    [RelayCommand(CanExecute = nameof(CanUseSelectedServer))]
    private async Task UseSyslogAsync()
    {
        LogPath = "/var/log/syslog";
        await LoadLogAsync();
    }

    [RelayCommand(CanExecute = nameof(CanUseSelectedServer))]
    private async Task UseAuthLogAsync()
    {
        LogPath = "/var/log/auth.log";
        await LoadLogAsync();
    }

    [RelayCommand]
    private async Task AddSavedCommandAsync()
    {
        if (string.IsNullOrWhiteSpace(CommandTitle) || string.IsNullOrWhiteSpace(CommandText))
        {
            SshOutput = L.T("FillCommandFields");
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

    [RelayCommand(CanExecute = nameof(CanUseSelectedSavedCommand))]
    private async Task UpdateSavedCommandAsync()
    {
        if (SelectedSavedCommand is null)
        {
            SshOutput = L.T("ChooseCommand");
            return;
        }

        if (string.IsNullOrWhiteSpace(CommandTitle) || string.IsNullOrWhiteSpace(CommandText))
        {
            SshOutput = L.T("FillCommandFields");
            return;
        }

        SelectedSavedCommand.Title = CommandTitle.Trim();
        SelectedSavedCommand.Command = CommandText.Trim();
        await _commandService.UpdateAsync(SelectedSavedCommand);
        await LogActivityAsync(L.T("Commands"), SelectedSavedCommand.Title, L.T("CommandUpdated"));
        await LoadSavedCommandsAsync();
        SshOutput = L.T("CommandUpdated");
    }

    [RelayCommand(CanExecute = nameof(CanUseSelectedSavedCommand))]
    private async Task DeleteSavedCommandAsync()
    {
        if (SelectedSavedCommand is null)
        {
            SshOutput = L.T("ChooseCommand");
            return;
        }

        var title = SelectedSavedCommand.Title;
        var confirmed = ConfirmActionRequested?.Invoke(L.Format("DeleteCommandPrompt", title)) ?? false;

        if (!confirmed)
        {
            return;
        }

        await _commandService.DeleteAsync(SelectedSavedCommand);
        await LogActivityAsync(L.T("Commands"), title, L.T("CommandDeleted"), "warn");
        await LoadSavedCommandsAsync();
    }

    [RelayCommand(CanExecute = nameof(CanUseSelectedSavedCommand))]
    private async Task RunSavedCommandAsync()
    {
        if (SelectedSavedCommand is null)
        {
            SshOutput = L.T("ChooseSavedCommand");
            return;
        }

        await RunServerCommandAsync(SelectedSavedCommand.Command);
    }

    [RelayCommand]
    private async Task RunCustomCommandAsync()
    {
        if (string.IsNullOrWhiteSpace(CustomCommand))
        {
            SshOutput = L.T("EnterCommand");
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
            : L.Format("ConnectedTarget", $"{SelectedServer.Username}@{SelectedServer.Host}") + Environment.NewLine;
    }
    [RelayCommand(CanExecute = nameof(CanSendTerminalCommand))]
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
            var output = string.IsNullOrWhiteSpace(result.Output) ? result.Message : result.Output;
            TerminalOutput += $"\nroot@{SelectedServer.Name}:~# {command}\n{output}\n";
            TerminalCommand = "";
            await LogActivityAsync(
                L.T("Terminal"),
                SelectedServer.Name,
                result.Succeeded ? command : result.Message,
                result.Succeeded ? "info" : "error");
        }
        catch (Exception ex)
        {
            SelectedServer.IsOnline = false;
            TerminalOutput += $"\n{L.Format("TerminalError", ex.Message)}\n";
            await LogActivityAsync(L.T("Terminal"), SelectedServer.Name, ex.Message, "error");
        }
    }


    [RelayCommand(CanExecute = nameof(CanUseSelectedRemoteFile))]
    private async Task DownloadSelectedRemoteFileAsync()
    {


        if (SelectedServer is null)
        {
            SftpOutput = L.T("ChooseServerFirst");
            return;
        }

        if (SelectedRemoteFile is null)
        {
            SftpOutput = L.T("ChooseRemoteFileToDownload");
            return;
        }

        if (SelectedRemoteFile.IsDirectory)
        {
            SftpOutput = L.T("CannotDownloadFolder");
            return;
        }

        var fileName = Path.GetFileName(SelectedRemoteFile.FullPath);

        var localPath = _fileDialog.PickSaveFile(fileName);

        if (string.IsNullOrWhiteSpace(localPath))
        {
            return;
        }

        RemoteFilePath = SelectedRemoteFile.FullPath;
        LocalFilePath = localPath;

        SftpOutput = L.T("DownloadInProgress");
        var remotePath = SelectedRemoteFile.FullPath;
        var result = await _ssh.DownloadFileAsync(SelectedServer, remotePath, localPath);
        SftpOutput = result.Message;
        await LogOperationAsync("SFTP", SelectedServer.Name, result, L.Format("DownloadActivity", remotePath));
    }

    [RelayCommand(CanExecute = nameof(CanUseSelectedServer))]
    private async Task LoadRemoteFilesAsync()
    {
        var server = SelectedServer;

        if (server is null)
        {
            SftpOutput = L.T("ChooseServer");
            return;
        }

        if (IsFilesLoading)
        {
            return;
        }

        var version = serverSelectionVersion;
        var cancellationToken = serverSelectionCancellation.Token;
        var requestedPath = RemoteFolderPath;

        try
        {
            IsFilesLoading = true;
            SftpOutput = L.T("FilesLoading");
            var result = await _ssh.GetFilesAsync(server, requestedPath, cancellationToken);

            if (!IsCurrentServer(server, version))
            {
                return;
            }

            SelectedRemoteFile = null;
            RemoteFiles.Clear();

            if (!result.Succeeded || result.Value is null)
            {
                SftpOutput = result.Message;
                server.IsOnline = false;
                return;
            }

            foreach (var file in result.Value)
            {
                RemoteFiles.Add(file);
            }

            SftpOutput = result.Message;
            server.IsOnline = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        finally
        {
            if (IsCurrentServer(server, version))
            {
                IsFilesLoading = false;
                OnPropertyChanged(nameof(RemoteFilesSummary));
                OnPropertyChanged(nameof(HasRemoteFiles));
                OnPropertyChanged(nameof(OnlineText));
            }
        }
    }
    [RelayCommand(CanExecute = nameof(CanUseSelectedServer))]
    private async Task GoRootRemoteFolderAsync()
    {
        RemoteFolderPath = "/";
        await LoadRemoteFilesAsync();
    }

    [RelayCommand(CanExecute = nameof(CanUseSelectedServer))]
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

    [RelayCommand(CanExecute = nameof(CanUseSelectedRemoteItem))]
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

    [RelayCommand(CanExecute = nameof(CanUseSelectedRemoteFile))]
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
            SftpOutput = L.T("CannotEditFolder");
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
        var result = await _ssh.DownloadFileAsync(SelectedServer, RemoteFilePath, targetLocalPath);
        SftpOutput = result.Message;
        await LogOperationAsync("SFTP", SelectedServer.Name, result, L.Format("DownloadActivity", RemoteFilePath));
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
        var result = await _ssh.UploadFileAsync(SelectedServer, LocalFilePath, RemoteFilePath);
        SftpOutput = result.Message;
        await LogOperationAsync("SFTP", SelectedServer.Name, result, L.Format("UploadingFile", RemoteFilePath));
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
        var result = await _ssh.UploadFileAsync(SelectedServer, localPath, remotePath);
        SftpOutput = result.Message;
        await LogOperationAsync("SFTP", SelectedServer.Name, result, L.Format("DragDropUploadActivity", remotePath));

        if (result.Succeeded)
        {
            await LoadRemoteFilesAsync();
        }
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
        var result = await _ssh.CreateRemoteDirectoryAsync(SelectedServer, remotePath);
        SftpOutput = result.Message;
        await LogOperationAsync("SFTP", SelectedServer.Name, result, $"{L.T("CreateFolder")}: {remotePath}");

        if (result.Succeeded)
        {
            await LoadRemoteFilesAsync();
        }
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
        var result = await _ssh.RenameRemoteItemAsync(SelectedServer, SelectedRemoteFile.FullPath, newPath);
        SftpOutput = result.Message;
        await LogOperationAsync("SFTP", SelectedServer.Name, result, L.Format("RenameActivity", newPath));

        if (result.Succeeded)
        {
            await LoadRemoteFilesAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseSelectedRemoteItem))]
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
        var remotePath = SelectedRemoteFile.FullPath;
        var result = await _ssh.DeleteRemoteItemAsync(SelectedServer, remotePath, SelectedRemoteFile.IsDirectory);
        SftpOutput = result.Message;
        await LogOperationAsync("SFTP", SelectedServer.Name, result, $"{L.T("Delete")}: {remotePath}", "warn");

        if (result.Succeeded)
        {
            await LoadRemoteFilesAsync();
        }
    }

    private async Task RunServerCommandAsync(string command)
    {
        var server = SelectedServer;

        if (server is null)
        {
            SshOutput = L.T("ChooseServerFirst");
            return;
        }

        if (IsCommandRunning)
        {
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

        var version = serverSelectionVersion;
        commandCancellation?.Dispose();
        commandCancellation = CancellationTokenSource.CreateLinkedTokenSource(serverSelectionCancellation.Token);
        var cancellation = commandCancellation;

        try
        {
            IsCommandRunning = true;
            SshOutput = L.Format("RunningCommand", command);
            var result = await _ssh.RunCommandAsync(server, command, cancellation.Token);

            if (!IsCurrentServer(server, version))
            {
                return;
            }

            var output = string.IsNullOrWhiteSpace(result.Output) ? result.Message : result.Output;
            SshOutput = result.Succeeded ? output : $"{result.Message}{Environment.NewLine}{output}".Trim();
            TerminalOutput += $"\nroot@{server.Name}:~# {command}\n{SshOutput}\n";
            server.IsOnline = result.Succeeded;
            OnPropertyChanged(nameof(OnlineText));
            await LogOperationAsync("SSH", server.Name, result, command);
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentServer(server, version))
            {
                SshOutput = L.T("OperationCancelled");
            }
        }
        finally
        {
            if (ReferenceEquals(commandCancellation, cancellation))
            {
                commandCancellation = null;
            }

            cancellation.Dispose();
            IsCommandRunning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsCommandRunning))]
    private void CancelCurrentOperation() => commandCancellation?.Cancel();
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

        CheckAllServersCommand.NotifyCanExecuteChanged();

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

        OnPropertyChanged(nameof(HasFilteredServers));

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
            DisplayName = L.T("AllServers"),
            Count = Servers.Count,
            IsSelected = SelectedGroupName == "All Servers"
        });

        foreach (var group in Servers.GroupBy(x => string.IsNullOrWhiteSpace(x.GroupName) ? "Production" : x.GroupName).OrderBy(x => x.Key))
        {
            Groups.Add(new DashboardGroup
            {
                Name = group.Key,
                DisplayName = group.Key,
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
                    DisplayName = defaultGroup,
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

    private Task LogOperationAsync(
        string action,
        string target,
        OperationResult result,
        string successDetails,
        string successLevel = "info") =>
        LogActivityAsync(
            action,
            target,
            result.Succeeded ? successDetails : result.Message,
            result.Succeeded ? successLevel : "error");

    private async Task LogActivityAsync(string action, string target, string details = "", string level = "info")
    {
        try
        {
            await _dashboardData.AddActivityAsync(action, target, details, level);
            await LoadActivityLogsAsync();
        }
        catch (Exception ex)
        {
            TerminalOutput += $"\n{L.Format("ActivityLogUnavailable", ex.Message)}\n";
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

    [RelayCommand(CanExecute = nameof(CanUseSelectedServer))]
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
        var result = await _ssh.UploadFileAsync(SelectedServer, localPath, remotePath);
        SftpOutput = result.Message;
        await LogOperationAsync("SFTP", SelectedServer.Name, result, L.Format("UploadActivity", remotePath));

        if (result.Succeeded)
        {
            await LoadRemoteFilesAsync();
        }
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

    private bool IsCurrentServer(ServerProfile server, int version) =>
        version == serverSelectionVersion && ReferenceEquals(SelectedServer, server);

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

    private static bool IsValidRemotePath(string path) =>
        !string.IsNullOrWhiteSpace(path) && path.StartsWith("/", StringComparison.Ordinal);

    private static bool RequiresCommandConfirmation(string command) => ApplicationRules.RequiresCommandConfirmation(command);
    private static string ExtractMonitoringSection(string output, string sectionName) => ApplicationRules.ExtractMonitoringSection(output, sectionName);
    private static double ExtractPercentValue(string section) => ApplicationRules.ExtractPercentValue(section);
    private static string RemovePercentLine(string section) => ApplicationRules.RemovePercentLine(section);

    private static string GetRemoteParentPath(string remotePath) => ApplicationRules.GetRemoteParentPath(remotePath);
    private static bool ValidateRemoteItemName(string name, out string error) => ApplicationRules.ValidateRemoteItemName(name, out error);
    private static string CombineRemotePath(string remoteFolderPath, string fileName) => ApplicationRules.CombineRemotePath(remoteFolderPath, fileName);
    private static string QuoteShellArgument(string value) => $"'{value.Replace("'", "'\\''")}'";

    public void Dispose()
    {
        _monitoringTimer.Stop();
        serverSelectionCancellation.Cancel();
        serverSelectionCancellation.Dispose();
        commandCancellation?.Cancel();
        commandCancellation?.Dispose();
        _terminalSsh.Dispose();
    }
}
