using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using ServerControlCenter.Models;
using ServerControlCenter.Services;
using ServerControlCenter.Views;

namespace ServerControlCenter.ViewModels;

public partial class MainViewModel
{
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
            MonitorSystemDetails = L.T("MonitoringLoading");
            MonitorStorageDetails = L.T("MonitoringLoading");
            MonitorNetworkDetails = L.T("MonitoringLoading");

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
                "echo __UPTIME__; awk '{print int($1)}' /proc/uptime; " +
                "echo __SYSTEM_DETAILS__; printf 'Hostname: '; hostname; uname -srmo; " +
                "printf 'CPU cores: '; (nproc 2>/dev/null || getconf _NPROCESSORS_ONLN 2>/dev/null || echo n/a); " +
                "awk -F: '/model name/ {gsub(/^[ \\t]+/, \"\", $2); print \"CPU: \" $2; exit}' /proc/cpuinfo; " +
                "echo __STORAGE_DETAILS__; df -hT / | tail -n 1; " +
                "echo __NETWORK_DETAILS__; (ip -brief address show up 2>/dev/null || hostname -I 2>/dev/null || true); " +
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
                MonitorSystemDetails = output;
                MonitorStorageDetails = output;
                MonitorNetworkDetails = output;
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
            MonitorUptimeShort = FormatUptime(ExtractMonitoringSection(output, "UPTIME"));
            MonitorSystemDetails = ExtractMonitoringSection(output, "SYSTEM_DETAILS");
            MonitorStorageDetails = ExtractMonitoringSection(output, "STORAGE_DETAILS");
            MonitorNetworkDetails = ExtractMonitoringSection(output, "NETWORK_DETAILS");
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

    private string FormatUptime(string rawSeconds)
    {
        if (!long.TryParse(rawSeconds.Trim(), out var totalSeconds) || totalSeconds < 0)
        {
            return rawSeconds;
        }

        var duration = TimeSpan.FromSeconds(totalSeconds);
        var weeks = duration.Days / 7;
        var days = duration.Days % 7;
        var isRussian = string.Equals(L.LanguageCode, "ru", StringComparison.OrdinalIgnoreCase);
        var parts = new List<string>(3);

        if (weeks > 0) parts.Add(isRussian ? $"{weeks} нед." : $"{weeks}w");
        if (days > 0) parts.Add(isRussian ? $"{days} д." : $"{days}d");
        if (duration.Hours > 0) parts.Add(isRussian ? $"{duration.Hours} ч." : $"{duration.Hours}h");
        if (duration.Minutes > 0 && parts.Count < 3) parts.Add(isRussian ? $"{duration.Minutes} мин." : $"{duration.Minutes}m");

        return parts.Count == 0
            ? (isRussian ? "< 1 мин." : "< 1m")
            : string.Join(" ", parts.Take(3));
    }

    private async Task RefreshMonitoringFromTimerAsync()
    {
        if (SelectedServer is null)
        {
            return;
        }

        await RefreshMonitoringAsync();
    }
}
