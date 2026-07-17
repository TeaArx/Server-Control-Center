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

        var remotePath = SelectedRemoteFile.FullPath;
        SftpOutput = L.LanguageCode == "ru" ? "Подготовка предпросмотра удаления…" : "Preparing delete preview…";
        var previewResult = await _ssh.PreviewDeleteRemoteItemAsync(SelectedServer, remotePath);

        if (!previewResult.Succeeded || previewResult.Value is null)
        {
            SftpOutput = previewResult.Message;
            return;
        }

        var preview = previewResult.Value;
        var prompt = L.LanguageCode == "ru"
            ? $"Удалить {preview.Path}?\nФайлов: {preview.FileCount}; папок: {preview.DirectoryCount}; объём: {preview.TotalBytes:N0} байт."
            : $"Delete {preview.Path}?\nFiles: {preview.FileCount}; folders: {preview.DirectoryCount}; size: {preview.TotalBytes:N0} bytes.";

        if (!(ConfirmActionRequested?.Invoke(prompt) ?? false))
        {
            SftpOutput = L.T("DangerousCommandCancelled");
            return;
        }

        SftpOutput = L.T("Delete");
        var result = await _ssh.DeleteRemoteItemAsync(SelectedServer, remotePath, SelectedRemoteFile.IsDirectory);
        SftpOutput = result.Message;
        await LogOperationAsync("SFTP", SelectedServer.Name, result, $"{L.T("Delete")}: {remotePath}", "warn");

        if (result.Succeeded)
        {
            await LoadRemoteFilesAsync();
        }
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
}
