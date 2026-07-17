using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ServerControlCenter.Models;
using ServerControlCenter.Services;
using ServerControlCenter.ViewModels;

namespace ServerControlCenter.Views.Features;

public partial class RemoteFilesView : UserControl
{
    public RemoteFilesView()
    {
        InitializeComponent();
    }

    private void RemoteFolderPathTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainViewModel viewModel &&
            viewModel.LoadRemoteFilesCommand.CanExecute(null))
        {
            e.Handled = true;
            viewModel.LoadRemoteFilesCommand.Execute(null);
        }
    }

    private void RemoteFilesListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && viewModel.OpenSelectedRemoteItemCommand.CanExecute(null))
        {
            viewModel.OpenSelectedRemoteItemCommand.Execute(null);
        }
    }

    private void RemoteFilesDataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = FindVisualParent<DataGridRow>((DependencyObject)e.OriginalSource);

        if (row is null)
        {
            return;
        }

        row.IsSelected = true;

        if (DataContext is MainViewModel viewModel && row.Item is RemoteFileItem remoteFile)
        {
            viewModel.SelectedRemoteFile = remoteFile;
        }
    }

    private void RemoteFilesListView_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void RemoteFilesListView_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);

        foreach (var path in paths.Where(File.Exists))
        {
            await viewModel.UploadLocalFileToCurrentFolderAsync(path);
        }
    }

    private void OpenButtonContextMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { ContextMenu: not null } element)
        {
            element.ContextMenu.PlacementTarget = element;
            element.ContextMenu.IsOpen = true;
        }
    }

    private void RemoteFileOpenMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && viewModel.OpenSelectedRemoteItemCommand.CanExecute(null))
        {
            viewModel.OpenSelectedRemoteItemCommand.Execute(null);
        }
    }

    private void RemoteFileEditMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && viewModel.EditRemoteFileCommand.CanExecute(null))
        {
            viewModel.EditRemoteFileCommand.Execute(null);
        }
    }

    private void RemoteFileDownloadMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && viewModel.DownloadSelectedRemoteFileCommand.CanExecute(null))
        {
            viewModel.DownloadSelectedRemoteFileCommand.Execute(null);
        }
    }

    private async void RemoteFileCreateFolderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var prompt = new TextPromptWindow(
            AppServices.Localizer.T("FolderCreateTitle"),
            AppServices.Localizer.T("FolderCreatePrompt"))
        {
            Owner = Window.GetWindow(this)
        };

        if (prompt.ShowDialog() == true)
        {
            await viewModel.CreateRemoteFolderAsync(prompt.Value);
        }
    }

    private async void RemoteFileRenameMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedRemoteFile is null)
        {
            return;
        }

        var prompt = new TextPromptWindow(
            AppServices.Localizer.T("RenameTitle"),
            AppServices.Localizer.T("RenamePrompt"),
            viewModel.SelectedRemoteFile.Name)
        {
            Owner = Window.GetWindow(this)
        };

        if (prompt.ShowDialog() == true)
        {
            await viewModel.RenameSelectedRemoteItemAsync(prompt.Value);
        }
    }

    private void RemoteFileDeleteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && viewModel.DeleteSelectedRemoteItemCommand.CanExecute(null))
        {
            viewModel.DeleteSelectedRemoteItemCommand.Execute(null);
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T parent)
            {
                return parent;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }
}
