using System.Windows.Controls;
using System.Windows.Input;
using ServerControlCenter.ViewModels;

namespace ServerControlCenter.Views.Features;

public partial class ServersSidebarView : UserControl
{
    public ServersSidebarView()
    {
        InitializeComponent();
    }

    private void ServersListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && viewModel.EditServerCommand.CanExecute(null))
        {
            viewModel.EditServerCommand.Execute(null);
        }
    }
}
