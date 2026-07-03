using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServerControlCenter.Models;
using ServerControlCenter.Services;

namespace ServerControlCenter.ViewModels;

public partial class RemoteFileEditorViewModel : ObservableObject
{
    private readonly ServerProfile server;
    private readonly SshService ssh;

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
            StatusMessage = "Загрузка файла...";

            var loadedContent = await ssh.ReadTextFileAsync(server, RemotePath);

            if (loadedContent.StartsWith("Ошибка чтения файла:", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = loadedContent;
                return;
            }

            Content = loadedContent;
            HasLoadedSuccessfully = true;
            StatusMessage = "Файл загружен.";
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
            StatusMessage = "Сначала файл должен быть успешно загружен.";
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Сохранение файла...";

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
