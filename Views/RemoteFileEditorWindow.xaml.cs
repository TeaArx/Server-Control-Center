using System.Windows;
using ServerControlCenter.ViewModels;

namespace ServerControlCenter.Views;

public partial class RemoteFileEditorWindow : Window
{
    private readonly RemoteFileEditorViewModel _viewModel;

    public RemoteFileEditorWindow(RemoteFileEditorViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        Loaded += RemoteFileEditorWindow_Loaded;
    }

    private async void RemoteFileEditorWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }
}
