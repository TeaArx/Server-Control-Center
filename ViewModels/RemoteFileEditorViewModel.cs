using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServerControlCenter.Models;
using ServerControlCenter.Services;

namespace ServerControlCenter.ViewModels;

public partial class RemoteFileEditorViewModel : ObservableObject, IDisposable
{
    private readonly ServerProfile server;
    private readonly SshService ssh;
    private readonly LocalizationService localizer = AppServices.Localizer;
    private CancellationTokenSource? operationCancellation;

    public string RemotePath { get; }

    [ObservableProperty]
    private string content = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
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

        using var cancellation = BeginOperation();

        try
        {
            IsBusy = true;
            HasLoadedSuccessfully = false;
            StatusMessage = localizer.T("FileLoading");
            var result = await ssh.ReadTextFileAsync(server, RemotePath, cancellation.Token);

            if (!result.Succeeded || result.Value is null)
            {
                StatusMessage = result.Message;
                return;
            }

            Content = result.Value;
            HasLoadedSuccessfully = true;
            StatusMessage = result.Message;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = localizer.T("OperationCancelled");
        }
        finally
        {
            IsBusy = false;
            EndOperation(cancellation);
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

        using var cancellation = BeginOperation();

        try
        {
            IsBusy = true;
            StatusMessage = localizer.T("FileSaving");
            var result = await ssh.SaveTextFileAsync(server, RemotePath, Content, cancellation.Token);
            StatusMessage = result.Message;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = localizer.T("OperationCancelled");
        }
        finally
        {
            IsBusy = false;
            EndOperation(cancellation);
        }
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => operationCancellation?.Cancel();

    private bool CanSave() => !IsBusy && HasLoadedSuccessfully;

    private CancellationTokenSource BeginOperation()
    {
        operationCancellation?.Cancel();
        operationCancellation?.Dispose();
        operationCancellation = new CancellationTokenSource();
        return operationCancellation;
    }

    private void EndOperation(CancellationTokenSource cancellation)
    {
        if (ReferenceEquals(operationCancellation, cancellation))
        {
            operationCancellation = null;
        }
    }

    public void Dispose()
    {
        operationCancellation?.Cancel();
        operationCancellation?.Dispose();
    }
}
