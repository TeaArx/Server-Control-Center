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
    private readonly List<string> terminalHistory = [];
    private int terminalHistoryIndex;

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
        if (DataContext is MainViewModel viewModel && e.Key == Key.Escape &&
            (viewModel.IsSettingsPanelOpen ||
             viewModel.IsFavoritesPanelOpen || viewModel.IsActivityPanelOpen))
        {
            viewModel.CloseOverlayPanelsCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (DataContext is MainViewModel vm && e.Key == Key.N &&
            (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            vm.AddServerCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 ||
            e.Key is not (Key.K or Key.F))
        {
            return;
        }

        ServerSearchBox.Focus();
        ServerSearchBox.SelectAll();
        e.Handled = true;
    }

    private void TerminalCommandTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox input || DataContext is not MainViewModel vm)
        {
            return;
        }

        var control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (control && e.Key == Key.C && input.SelectionLength == 0)
        {
            InterruptTerminal(vm);
            e.Handled = true;
            return;
        }

        if (control && shift && e.Key == Key.V)
        {
            input.Paste();
            e.Handled = true;
            return;
        }

        if (control && e.Key == Key.L)
        {
            vm.ClearTerminalCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up && terminalHistory.Count > 0)
        {
            terminalHistoryIndex = Math.Max(0, terminalHistoryIndex - 1);
            vm.TerminalCommand = terminalHistory[terminalHistoryIndex];
            input.CaretIndex = input.Text.Length;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down && terminalHistory.Count > 0)
        {
            terminalHistoryIndex = Math.Min(terminalHistory.Count, terminalHistoryIndex + 1);
            vm.TerminalCommand = terminalHistoryIndex == terminalHistory.Count
                ? string.Empty
                : terminalHistory[terminalHistoryIndex];
            input.CaretIndex = input.Text.Length;
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;

        if (!string.IsNullOrWhiteSpace(vm.TerminalCommand) &&
            (terminalHistory.Count == 0 || terminalHistory[^1] != vm.TerminalCommand))
        {
            terminalHistory.Add(vm.TerminalCommand);
            terminalHistoryIndex = terminalHistory.Count;
        }

        if (vm.SendTerminalCommandCommand.CanExecute(null))
        {
            vm.SendTerminalCommandCommand.Execute(null);
        }
    }

    private void TerminalOutputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox output || DataContext is not MainViewModel vm)
        {
            return;
        }

        var control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (control && e.Key == Key.C)
        {
            if (output.SelectionLength > 0)
            {
                output.Copy();
            }
            else
            {
                InterruptTerminal(vm);
            }

            e.Handled = true;
            return;
        }

        if (control && shift && e.Key == Key.V)
        {
            TerminalInputTextBox.Focus();
            TerminalInputTextBox.Paste();
            e.Handled = true;
            return;
        }

        if (control && e.Key == Key.L)
        {
            vm.ClearTerminalCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            TerminalInputTextBox.Focus();
            e.Handled = true;
        }
    }

    private void TerminalOutputTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        TerminalInputTextBox.Focus();
        var insertionPoint = TerminalInputTextBox.SelectionStart;
        TerminalInputTextBox.SelectedText = e.Text;
        TerminalInputTextBox.CaretIndex = insertionPoint + e.Text.Length;
        e.Handled = true;
    }

    private static void InterruptTerminal(MainViewModel viewModel)
    {
        if (viewModel.InterruptTerminalCommandCommand.CanExecute(null))
        {
            viewModel.InterruptTerminalCommandCommand.Execute(null);
        }
    }

    private void TerminalOutputTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.ScrollToEnd();
        }
    }

    private void RemoteFolderPathTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not MainViewModel viewModel ||
            !viewModel.LoadRemoteFilesCommand.CanExecute(null))
        {
            return;
        }

        e.Handled = true;
        viewModel.LoadRemoteFilesCommand.Execute(null);
    }

    private void LogPathTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not MainViewModel viewModel ||
            !viewModel.LoadLogCommand.CanExecute(null))
        {
            return;
        }

        e.Handled = true;
        viewModel.LoadLogCommand.Execute(null);
    }

    private void OperationsTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, sender) || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (OperationsTabControl.SelectedIndex == 1 && viewModel.LoadRemoteFilesCommand.CanExecute(null))
        {
            viewModel.LoadRemoteFilesCommand.Execute(null);
        }
        else if (OperationsTabControl.SelectedIndex == 2 && viewModel.LoadLogCommand.CanExecute(null))
        {
            viewModel.LoadLogCommand.Execute(null);
        }
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

    private void OpenButtonContextMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.ContextMenu is null)
        {
            return;
        }

        element.ContextMenu.PlacementTarget = element;
        element.ContextMenu.IsOpen = true;
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
            viewModel.DownloadSelectedRemoteFileCommand.CanExecute(null))
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
            AppServices.Localizer.Format("DangerousCommandPrompt", command),
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

    private void NumericTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = e.Text.Any(character => !char.IsDigit(character));
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
