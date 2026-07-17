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

        TerminalCommand = SelectedSavedCommand.Command;
        await SendTerminalCommandAsync();
    }

    [RelayCommand]
    private async Task RunFavoriteCommandAsync(SavedCommand? command)
    {
        if (command is null)
        {
            SshOutput = L.T("ChooseSavedCommand");
            return;
        }

        SelectedSavedCommand = command;
        CloseOverlayPanelsCore();
        TerminalCommand = command.Command;
        await SendTerminalCommandAsync();
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

    private async Task LoadSavedCommandsAsync()
    {
        var selectedId = SelectedSavedCommand?.Id;
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

        SelectedSavedCommand = selectedId.HasValue
            ? SavedCommands.FirstOrDefault(x => x.Id == selectedId.Value)
            : SelectedSavedCommand;
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
}
