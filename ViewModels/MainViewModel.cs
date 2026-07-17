using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
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

    private readonly ServerStorageService _storage;
    private readonly SshService _ssh;
    private readonly SshService _terminalSsh;
    private readonly SavedCommandService _commandService;
    private readonly DashboardDataService _dashboardData;
    private readonly FileDialogService _fileDialog;
    private readonly UpdateService _updateService;
    private readonly DispatcherTimer _monitoringTimer;
    private readonly List<double> cpuHistory = new();
    private readonly List<double> ramHistory = new();
    private readonly List<double> diskHistory = new();
    private readonly List<double> networkHistory = new();
    private readonly LocalizationService localizer = AppServices.Localizer;

    private CancellationTokenSource serverSelectionCancellation = new();
    private readonly CancellationTokenSource updateCancellation = new();
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

    private UpdateInfo? availableUpdate;
    private bool isUpdateBusy;
    private string updateStatusText = "";
    private double updateProgress;

    public UpdateInfo? AvailableUpdate
    {
        get => availableUpdate;
        private set
        {
            if (!SetProperty(ref availableUpdate, value)) return;
            OnPropertyChanged(nameof(IsUpdateAvailable));
            InstallUpdateCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsUpdateBusy
    {
        get => isUpdateBusy;
        private set
        {
            if (!SetProperty(ref isUpdateBusy, value)) return;
            CheckForUpdatesCommand.NotifyCanExecuteChanged();
            InstallUpdateCommand.NotifyCanExecuteChanged();
        }
    }

    public string UpdateStatusText
    {
        get => updateStatusText;
        private set => SetProperty(ref updateStatusText, value);
    }

    public double UpdateProgress
    {
        get => updateProgress;
        private set => SetProperty(ref updateProgress, value);
    }

    public IAsyncRelayCommand CheckForUpdatesCommand { get; }
    public IAsyncRelayCommand InstallUpdateCommand { get; }


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

    private bool isTerminalConnected;

    private string terminalSessionStatus = AppServices.Localizer.T("TerminalDisconnected");

    public bool IsTerminalConnected
    {
        get => isTerminalConnected;
        private set => SetProperty(ref isTerminalConnected, value);
    }

    public string TerminalSessionStatus
    {
        get => terminalSessionStatus;
        private set => SetProperty(ref terminalSessionStatus, value);
    }

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

    private string monitorSystemDetails = "";

    private string monitorStorageDetails = "";

    private string monitorNetworkDetails = "";

    public string MonitorSystemDetails
    {
        get => monitorSystemDetails;
        private set => SetProperty(ref monitorSystemDetails, value);
    }

    public string MonitorStorageDetails
    {
        get => monitorStorageDetails;
        private set => SetProperty(ref monitorStorageDetails, value);
    }

    public string MonitorNetworkDetails
    {
        get => monitorNetworkDetails;
        private set => SetProperty(ref monitorNetworkDetails, value);
    }

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
    public bool IsUpdateAvailable => AvailableUpdate is not null;
    public string CurrentVersionText => _updateService.CurrentVersion.ToString(3);
    public string SelectedServerTitle => SelectedServer?.Name ?? L.T("NoServerSelected");
    public string SelectedServerSubtitle => SelectedServer is null
        ? L.T("ChooseServer")
        : $"{SelectedServer.Username}@{SelectedServer.Host}:{SelectedServer.Port}";
    public string SelectedServerAddress => SelectedServer?.IpAddressDisplay ?? SelectedServer?.Host ?? "";
    public string SelectedServerOs => SelectedServer?.OsName ?? "Unknown Linux";
    public string OnlineText => L.T(SelectedServer?.IsOnline == true ? "Online" : "Offline");
    public string SelectedCommandFavoriteAction => L.T(SelectedSavedCommand?.IsFavorite == true
        ? "RemoveFromFavorites"
        : "AddToFavorites");
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

    public MainViewModel(
        ServerStorageService? storage = null,
        SshService? ssh = null,
        SshService? terminalSsh = null,
        SavedCommandService? commandService = null,
        DashboardDataService? dashboardData = null,
        FileDialogService? fileDialog = null,
        UpdateService? updateService = null)
    {
        _storage = storage ?? new ServerStorageService();
        _ssh = ssh ?? new SshService();
        _terminalSsh = terminalSsh ?? new SshService();
        _commandService = commandService ?? new SavedCommandService();
        _dashboardData = dashboardData ?? new DashboardDataService();
        _fileDialog = fileDialog ?? new FileDialogService();
        _updateService = updateService ?? new UpdateService();
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync, CanCheckForUpdates);
        InstallUpdateCommand = new AsyncRelayCommand(InstallUpdateAsync, CanInstallUpdate);
        UpdateStatusText = UpdateText($"Version {CurrentVersionText}", $"Версия {CurrentVersionText}");
        _terminalSsh.ShellOutputReceived += TerminalSsh_ShellOutputReceived;
        _monitoringTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };

        _monitoringTimer.Tick += async (_, _) => await RefreshMonitoringFromTimerAsync();
        _monitoringTimer.Start();

        _ = InitializeAsync();
    }

    private void TerminalSsh_ShellOutputReceived(object? sender, string output)
    {
        Application.Current.Dispatcher.BeginInvoke(() => AppendTerminalOutput(output));
    }

    private void AppendTerminalOutput(string output)
    {
        const int maxTerminalCharacters = 500_000;
        var builder = new StringBuilder(TerminalOutput, TerminalOutput.Length + output.Length);

        foreach (var character in output)
        {
            if (character == '\r')
            {
                var lastLineBreak = builder.Length - 1;
                while (lastLineBreak >= 0 && builder[lastLineBreak] != '\n')
                {
                    lastLineBreak--;
                }

                builder.Length = lastLineBreak + 1;
                continue;
            }

            if (character == '\b')
            {
                if (builder.Length > 0 && builder[^1] != '\n')
                {
                    builder.Length--;
                }

                continue;
            }

            if (character == '\n')
            {
                builder.Append(Environment.NewLine);
                continue;
            }

            builder.Append(character);
        }

        TerminalOutput = builder.ToString();

        if (TerminalOutput.Length > maxTerminalCharacters)
        {
            TerminalOutput = L.T("TerminalOutputTrimmed") + Environment.NewLine +
                TerminalOutput[^maxTerminalCharacters..];
        }
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
            await CheckForUpdatesCoreAsync(true);
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
        IsTerminalConnected = false;
        TerminalSessionStatus = L.T("TerminalDisconnected");
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
        OnPropertyChanged(nameof(SelectedCommandFavoriteAction));

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


    private bool CanCheckForUpdates() => !IsUpdateBusy;
    private bool CanInstallUpdate() => !IsUpdateBusy && AvailableUpdate is not null;

    private Task CheckForUpdatesAsync() => CheckForUpdatesCoreAsync(false);

    private async Task CheckForUpdatesCoreAsync(bool silent)
    {
        if (IsUpdateBusy) return;
        IsUpdateBusy = true;
        UpdateProgress = 0;
        if (!silent) UpdateStatusText = UpdateText("Checking GitHub Releases...", "Проверяем GitHub Releases...");
        try
        {
            AvailableUpdate = await _updateService.CheckForUpdateAsync(updateCancellation.Token);
            UpdateStatusText = AvailableUpdate is null
                ? UpdateText($"Version {CurrentVersionText} is up to date", $"Версия {CurrentVersionText} актуальна")
                : UpdateText($"Version {AvailableUpdate.Version.ToString(3)} is ready", $"Доступна версия {AvailableUpdate.Version.ToString(3)}");
        }
        catch (OperationCanceledException) when (updateCancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            UpdateStatusText = UpdateText($"Could not check for updates: {ex.Message}", $"Не удалось проверить обновления: {ex.Message}");
        }
        finally { IsUpdateBusy = false; }
    }

    private async Task InstallUpdateAsync()
    {
        if (AvailableUpdate is null) return;
        var version = AvailableUpdate.Version.ToString(3);
        var confirmed = ConfirmActionRequested?.Invoke(UpdateText(
            $"Download and install Server Control Center {version}? The app will restart.",
            $"Скачать и установить Server Control Center {version}? Приложение будет перезапущено.")) ?? false;
        if (!confirmed) return;

        IsUpdateBusy = true;
        var progress = new Progress<double>(value =>
        {
            UpdateProgress = value;
            UpdateStatusText = UpdateText($"Downloading update: {value:0}%", $"Скачиваем обновление: {value:0}%");
        });
        try
        {
            var installerPath = await _updateService.DownloadAndVerifyAsync(AvailableUpdate, progress, updateCancellation.Token);
            UpdateStatusText = UpdateText("Starting installer...", "Запускаем установщик...");
            UpdateService.StartInstaller(installerPath);
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException) when (updateCancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            UpdateStatusText = UpdateText($"Update failed: {ex.Message}", $"Не удалось обновить приложение: {ex.Message}");
            IsUpdateBusy = false;
        }
    }

    [RelayCommand]
    private void OpenReleasePage()
    {
        if (AvailableUpdate is not null)
            Process.Start(new ProcessStartInfo { FileName = AvailableUpdate.ReleasePageUrl.ToString(), UseShellExecute = true });
    }

    private string UpdateText(string english, string russian) =>
        string.Equals(L.LanguageCode, "ru", StringComparison.OrdinalIgnoreCase) ? russian : english;

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

    private async Task LoadActivityLogsAsync()
    {
        ActivityLogs.Clear();

        foreach (var entry in await _dashboardData.GetActivityLogsAsync())
        {
            ActivityLogs.Add(entry);
        }
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
        updateCancellation.Cancel();
        updateCancellation.Dispose();
        _terminalSsh.ShellOutputReceived -= TerminalSsh_ShellOutputReceived;
        _terminalSsh.Dispose();
    }
}
