using System.IO;
using System.Windows;
using ServerControlCenter.ViewModels;

namespace ServerControlCenter;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var viewModel = new MainViewModel();
        viewModel.ConfirmCommandRequested += ConfirmDangerousCommand;
        viewModel.ConfirmActionRequested += ConfirmAction;
        DataContext = viewModel;
        Closed += (_, _) => viewModel.Dispose();
    }

    private void ServersListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.EditServerCommand.CanExecute(null))
        {
            viewModel.EditServerCommand.Execute(null);
        }
    }

    private void RemoteFilesListView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.OpenSelectedRemoteItemCommand.CanExecute(null))
        {
            viewModel.OpenSelectedRemoteItemCommand.Execute(null);
        }
    }

    private void RemoteFilesListView_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;

        e.Handled = true;
    }

    private async void RemoteFilesListView_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);

        foreach (var path in paths.Where(File.Exists))
        {
            await viewModel.UploadLocalFileToCurrentFolderAsync(path);
        }
    }
    private void RemoteFileOpenMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.OpenSelectedRemoteItemCommand.CanExecute(null))
        {
            viewModel.OpenSelectedRemoteItemCommand.Execute(null);
        }
    }

    private void RemoteFileEditMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.EditRemoteFileCommand.CanExecute(null))
        {
            viewModel.EditRemoteFileCommand.Execute(null);
        }
    }

    private void RemoteFileDownloadMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.DownloadFileCommand.CanExecute(null))
        {
            viewModel.DownloadFileCommand.Execute(null);
        }
    }

    private async void RemoteFileCreateFolderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var prompt = new ServerControlCenter.Views.TextPromptWindow(
            "Создать папку",
            "Имя новой папки:"
        )
        {
            Owner = this
        };

        if (prompt.ShowDialog() == true)
        {
            await viewModel.CreateRemoteFolderAsync(prompt.Value);
        }
    }

    private async void RemoteFileRenameMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel ||
            viewModel.SelectedRemoteFile is null)
        {
            return;
        }

        var prompt = new ServerControlCenter.Views.TextPromptWindow(
            "Переименовать",
            "Новое имя:",
            viewModel.SelectedRemoteFile.Name
        )
        {
            Owner = this
        };

        if (prompt.ShowDialog() == true)
        {
            await viewModel.RenameSelectedRemoteItemAsync(prompt.Value);
        }
    }
    private void RemoteFileDeleteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel ||
            viewModel.SelectedRemoteFile is null)
        {
            return;
        }

        var result = MessageBox.Show(
            $"Удалить '{viewModel.SelectedRemoteFile.FullPath}'?",
            "Подтверждение удаления",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        if (viewModel.DeleteSelectedRemoteItemCommand.CanExecute(null))
        {
            viewModel.DeleteSelectedRemoteItemCommand.Execute(null);
        }
    }
    private bool ConfirmAction(string message)
    {
        var result = MessageBox.Show(
            message,
            "Подтверждение",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question
        );

        return result == MessageBoxResult.Yes;
    }

    private bool ConfirmDangerousCommand(string command)
    {
        var result = MessageBox.Show(
            $"Команда выглядит опасной и может изменить или остановить сервер:\n\n{command}\n\nВыполнить?",
            "Подтверждение опасной команды",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );

        return result == MessageBoxResult.Yes;
    }
}





