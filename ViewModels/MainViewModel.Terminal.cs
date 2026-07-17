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
    private void ClearTerminal()
    {
        TerminalOutput = SelectedServer is null
            ? L.T("TerminalWelcome")
            : L.Format("ConnectedTarget", $"{SelectedServer.Username}@{SelectedServer.Host}") + Environment.NewLine;
    }
    [RelayCommand(CanExecute = nameof(CanSendTerminalCommand))]
    private async Task SendTerminalCommandAsync()
    {
        var server = SelectedServer;

        if (server is null)
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

        var input = TerminalCommand;
        var version = serverSelectionVersion;
        TerminalCommand = "";

        try
        {
            if (!_terminalSsh.IsShellConnected)
            {
                TerminalSessionStatus = L.T("TerminalConnecting");
                AppendTerminalOutput($"{Environment.NewLine}{L.Format("TerminalConnectingTo", server.Name)}{Environment.NewLine}");
                await Task.Run(() => _terminalSsh.ConnectShell(server));

                if (!IsCurrentServer(server, version))
                {
                    _terminalSsh.DisconnectShell();
                    return;
                }

                IsTerminalConnected = true;
                TerminalSessionStatus = L.T("TerminalInteractiveReady");
            }

            var result = _terminalSsh.SendShellInput(input);
            if (!result.Succeeded)
            {
                AppendTerminalOutput($"{Environment.NewLine}{result.Message}{Environment.NewLine}");
            }

            await LogActivityAsync(
                L.T("Terminal"),
                server.Name,
                result.Succeeded ? L.T("TerminalInputSent") : result.Message,
                result.Succeeded ? "info" : "error");
        }
        catch (Exception ex)
        {
            server.IsOnline = false;
            IsTerminalConnected = false;
            TerminalSessionStatus = L.T("TerminalDisconnected");
            AppendTerminalOutput($"{Environment.NewLine}{L.Format("TerminalError", ex.Message)}{Environment.NewLine}");
            await LogActivityAsync(L.T("Terminal"), server.Name, ex.Message, "error");
        }
    }

    [RelayCommand]
    private void InterruptTerminalCommand()
    {
        var result = _terminalSsh.SendShellControl(0x03);
        if (!result.Succeeded)
        {
            AppendTerminalOutput($"{Environment.NewLine}{result.Message}{Environment.NewLine}");
            IsTerminalConnected = false;
            TerminalSessionStatus = L.T("TerminalDisconnected");
        }
    }

    [RelayCommand]
    private void DisconnectTerminal()
    {
        _terminalSsh.DisconnectShell();
        IsTerminalConnected = false;
        TerminalSessionStatus = L.T("TerminalDisconnected");
        AppendTerminalOutput($"{Environment.NewLine}{L.T("TerminalDisconnectedMessage")}{Environment.NewLine}");
    }

    [RelayCommand]
    private void OpenSystemTerminal()
    {
        if (SelectedServer is null)
        {
            return;
        }

        var arguments = new List<string>
        {
            "ssh",
            "-p",
            SelectedServer.Port.ToString()
        };

        if (!string.IsNullOrWhiteSpace(SelectedServer.PrivateKeyPath))
        {
            arguments.Add("-i");
            arguments.Add(SelectedServer.PrivateKeyPath);
        }

        arguments.Add($"{SelectedServer.Username}@{SelectedServer.Host}");

        try
        {
            StartTerminalProcess("wt.exe", arguments);
            TerminalSessionStatus = L.T("SystemTerminalOpened");
        }
        catch
        {
            try
            {
                StartTerminalProcess("cmd.exe", ["/k", .. arguments]);
                TerminalSessionStatus = L.T("SystemTerminalOpened");
            }
            catch (Exception ex)
            {
                TerminalSessionStatus = L.Format("SystemTerminalError", ex.Message);
            }
        }
    }

    private static void StartTerminalProcess(string fileName, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process.Start(startInfo);
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
}
