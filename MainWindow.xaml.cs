using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ServerControlCenter.Models;
using ServerControlCenter.Services;
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
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        SourceInitialized += (_, _) => ApplyWindowFrameTheme();
        Closed += (_, _) => viewModel.Dispose();
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 ||
            e.Key is not (Key.K or Key.F))
        {
            return;
        }

        ServerSearchBox.Focus();
        ServerSearchBox.SelectAll();
        e.Handled = true;
    }

    private void ServersListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
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

    private void RemoteFilesListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
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

    private void RemoteFilesDataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = FindVisualParent<DataGridRow>((DependencyObject)e.OriginalSource);
        if (row is null)
        {
            return;
        }

        row.IsSelected = true;

        if (DataContext is MainViewModel viewModel &&
            row.Item is RemoteFileItem remoteFile)
        {
            viewModel.SelectedRemoteFile = remoteFile;
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
        if (DataContext is MainViewModel viewModel &&
            viewModel.OpenSelectedRemoteItemCommand.CanExecute(null))
        {
            viewModel.OpenSelectedRemoteItemCommand.Execute(null);
        }
    }

    private void RemoteFileEditMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            viewModel.EditRemoteFileCommand.CanExecute(null))
        {
            viewModel.EditRemoteFileCommand.Execute(null);
        }
    }

    private void RemoteFileDownloadMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            viewModel.DownloadFileCommand.CanExecute(null))
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
            AppServices.Localizer.T("FolderCreateTitle"),
            AppServices.Localizer.T("FolderCreatePrompt"))
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
            AppServices.Localizer.T("RenameTitle"),
            AppServices.Localizer.T("RenamePrompt"),
            viewModel.SelectedRemoteFile.Name)
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
            $"{AppServices.Localizer.T("Delete")} '{viewModel.SelectedRemoteFile.FullPath}'?",
            AppServices.Localizer.T("Delete"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

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
            AppServices.Localizer.T("Confirm"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        return result == MessageBoxResult.Yes;
    }

    private bool ConfirmDangerousCommand(string command)
    {
        var result = MessageBox.Show(
            $"This command can change or stop the server:\n\n{command}\n\nRun it?",
            AppServices.Localizer.T("Confirm"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        return result == MessageBoxResult.Yes;
    }

    private static T? FindVisualParent<T>(DependencyObject? child)
        where T : DependencyObject
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

    private void ApplyWindowFrameTheme()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            var dark = 1;
            DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));

            var captionColor = ColorToBgr(0x05, 0x0B, 0x11);
            var borderColor = ColorToBgr(0x20, 0x31, 0x42);
            var textColor = ColorToBgr(0xF2, 0xF7, 0xFC);
            DwmSetWindowAttribute(handle, 35, ref captionColor, sizeof(int));
            DwmSetWindowAttribute(handle, 34, ref borderColor, sizeof(int));
            DwmSetWindowAttribute(handle, 36, ref textColor, sizeof(int));
        }
        catch
        {
            // Unsupported Windows builds keep the default native frame.
        }
    }

    private static int ColorToBgr(byte red, byte green, byte blue) => red | (green << 8) | (blue << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);
}
