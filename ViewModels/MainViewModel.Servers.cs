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
}
