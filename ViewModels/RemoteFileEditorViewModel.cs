using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServerControlCenter.Models;
using ServerControlCenter.Services;

namespace ServerControlCenter.ViewModels;

public partial class RemoteFileEditorViewModel : ObservableObject
{
    private readonly ServerProfile server;
    private readonly SshService ssh;
    private readonly LocalizationService localizer = AppServices.Localizer;

    public string RemotePath { get; }

    [ObservableProperty]
    private string content = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool hasLoadedSuccessfully;

    public RemoteFileEditorViewModel(ServerProfile server, string remotePath, SshService ssh)
    {
        this.server = server;
        this.ssh = ssh;
        RemotePath = remotePath;
    }

    public async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            HasLoadedSuccessfully = false;
            StatusMessage = localizer.T("FileLoading");

            var loadedContent = await ssh.ReadTextFileAsync(server, RemotePath);

            if (loadedContent.StartsWith("Ошибка чтения файла:", StringComparison.OrdinalIgnoreCase) ||
                loadedContent.StartsWith("File read error:", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = loadedContent;
                return;
            }

            Content = loadedContent;
            HasLoadedSuccessfully = true;
            StatusMessage = localizer.T("FileLoaded");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!HasLoadedSuccessfully)
        {
            StatusMessage = localizer.T("FileMustLoadFirst");
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = localizer.T("FileSaving");

            StatusMessage = await ssh.SaveTextFileAsync(server, RemotePath, Content);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSave()
    {
        return !IsBusy && HasLoadedSuccessfully;
    }
}
