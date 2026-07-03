using System.Windows;
using ServerControlCenter.ViewModels;

namespace ServerControlCenter.Views;

public partial class ServerEditWindow : Window
{
    public ServerEditWindow(ServerEditViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;
        PasswordInput.Password = viewModel.Password;

        viewModel.RequestClose += () =>
        {
            DialogResult = true;
            Close();
        };
    }

    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ServerEditViewModel viewModel)
        {
            viewModel.Password = PasswordInput.Password;
        }
    }
}
