using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServerControlCenter.Models;
using ServerControlCenter.Services;

namespace ServerControlCenter.ViewModels;

public partial class ServerEditViewModel : ObservableObject
{
    private readonly FileDialogService _fileDialog = new();
    private readonly SshService _ssh = new();
    private readonly LocalizationService localizer = AppServices.Localizer;

    public ServerProfile Server { get; }

    public string WindowTitle { get; }

    [ObservableProperty]
    private string name = "";

    [ObservableProperty]
    private string host = "";

    [ObservableProperty]
    private int port = 22;

    [ObservableProperty]
    private string username = "";

    [ObservableProperty]
    private string password = "";

    [ObservableProperty]
    private string privateKeyPath = "";

    [ObservableProperty]
    private string notes = "";

    [ObservableProperty]
    private string groupName = "Production";

    [ObservableProperty]
    private string osName = "";

    [ObservableProperty]
    private string ipAddressDisplay = "";

    [ObservableProperty]
    private bool isFavorite;

    [ObservableProperty]
    private string statusMessage = "";

    public bool IsSaved { get; private set; }

    public event Action? RequestClose;

    public ServerEditViewModel(ServerProfile server, string? windowTitle = null)
    {
        Server = server;
        WindowTitle = windowTitle ?? localizer.T("WindowServerEditTitle");

        Name = server.Name;
        Host = server.Host;
        Port = server.Port;
        Username = server.Username;
        Password = server.Password ?? "";
        PrivateKeyPath = server.PrivateKeyPath ?? "";
        Notes = server.Notes ?? "";
        GroupName = string.IsNullOrWhiteSpace(server.GroupName) ? "Production" : server.GroupName;
        OsName = server.OsName ?? "";
        IpAddressDisplay = server.IpAddressDisplay ?? "";
        IsFavorite = server.IsFavorite;
    }

    [RelayCommand]
    private void PickPrivateKey()
    {
        var file = _fileDialog.PickFile();

        if (!string.IsNullOrWhiteSpace(file))
        {
            PrivateKeyPath = file;
        }
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (!ValidateFields())
        {
            return;
        }

        StatusMessage = localizer.T("TestSsh");

        var tempServer = CreateServerFromFields();

        var result = await _ssh.TestConnectionAsync(tempServer);
        StatusMessage = result.Message;
    }

    [RelayCommand]
    private void Save()
    {
        if (!ValidateFields())
        {
            return;
        }

        Server.Name = Name.Trim();
        Server.Host = Host.Trim();
        Server.Port = Port;
        Server.Username = Username.Trim();
        Server.Password = string.IsNullOrWhiteSpace(Password) ? null : Password;
        Server.PrivateKeyPath = string.IsNullOrWhiteSpace(PrivateKeyPath) ? null : PrivateKeyPath;
        Server.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes;
        Server.GroupName = string.IsNullOrWhiteSpace(GroupName) ? "Production" : GroupName.Trim();
        Server.OsName = string.IsNullOrWhiteSpace(OsName) ? null : OsName.Trim();
        Server.IpAddressDisplay = string.IsNullOrWhiteSpace(IpAddressDisplay) ? null : IpAddressDisplay.Trim();
        Server.IsFavorite = IsFavorite;

        IsSaved = true;

        RequestClose?.Invoke();
    }

    private bool ValidateFields()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            StatusMessage = localizer.T("EnterName");
            return false;
        }

        if (string.IsNullOrWhiteSpace(Host))
        {
            StatusMessage = localizer.T("EnterHost");
            return false;
        }

        if (Port <= 0 || Port > 65535)
        {
            StatusMessage = localizer.T("PortRange");
            return false;
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            StatusMessage = localizer.T("EnterLogin");
            return false;
        }

        if (string.IsNullOrWhiteSpace(Password) &&
            string.IsNullOrWhiteSpace(PrivateKeyPath))
        {
            StatusMessage = localizer.T("EnterPasswordOrKey");
            return false;
        }

        return true;
    }

    private ServerProfile CreateServerFromFields()
    {
        return new ServerProfile
        {
            Id = Server.Id,
            Name = Name.Trim(),
            Host = Host.Trim(),
            Port = Port,
            Username = Username.Trim(),
            Password = string.IsNullOrWhiteSpace(Password) ? null : Password,
            PrivateKeyPath = string.IsNullOrWhiteSpace(PrivateKeyPath) ? null : PrivateKeyPath,
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes,
            GroupName = string.IsNullOrWhiteSpace(GroupName) ? "Production" : GroupName.Trim(),
            OsName = string.IsNullOrWhiteSpace(OsName) ? null : OsName.Trim(),
            IpAddressDisplay = string.IsNullOrWhiteSpace(IpAddressDisplay) ? null : IpAddressDisplay.Trim(),
            IsFavorite = IsFavorite
        };
    }
}
