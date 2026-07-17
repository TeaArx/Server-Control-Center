using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
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
        StateChanged += (_, _) => UpdateWindowChrome();
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

        DashboardHeader.FocusSearch();
        e.Handled = true;
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

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            var cursorPosition = e.GetPosition(this);
            var widthRatio = cursorPosition.X / ActualWidth;
            WindowState = WindowState.Normal;
            Left = cursorPosition.X - (RestoreBounds.Width * widthRatio);
            Top = Math.Max(0, cursorPosition.Y - 20);
        }

        DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void UpdateWindowChrome()
    {
        var maximized = WindowState == WindowState.Maximized;
        WindowBorder.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(10);
        WindowBorder.BorderThickness = maximized ? new Thickness(0) : new Thickness(1);
        MaximizeButton.Content = maximized ? "❐" : "□";
        MaximizeButton.ToolTip = maximized ? "Восстановить" : "Развернуть";
    }

    private void ApplyWindowFrameTheme()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            var dark = 1;
            DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));

            var captionColor = ColorToBgr(0x12, 0x1B, 0x23);
            var borderColor = ColorToBgr(0x35, 0x4A, 0x5D);
            var textColor = ColorToBgr(0xED, 0xF2, 0xF6);
            DwmSetWindowAttribute(handle, 35, ref captionColor, sizeof(int));
            DwmSetWindowAttribute(handle, 34, ref borderColor, sizeof(int));
            DwmSetWindowAttribute(handle, 36, ref textColor, sizeof(int));
            var cornerPreference = 2;
            DwmSetWindowAttribute(handle, 33, ref cornerPreference, sizeof(int));
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
