using System.Windows.Controls;
using ServerControlCenter.ViewModels;

namespace ServerControlCenter.Views.Features;

public partial class OperationsView : UserControl
{
    public OperationsView()
    {
        InitializeComponent();
    }

    private void OperationsTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, sender) || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (OperationsTabControl.SelectedIndex == 0 && viewModel.LoadRemoteFilesCommand.CanExecute(null))
        {
            viewModel.LoadRemoteFilesCommand.Execute(null);
        }
        else if (OperationsTabControl.SelectedIndex == 1 && viewModel.LoadLogCommand.CanExecute(null))
        {
            viewModel.LoadLogCommand.Execute(null);
        }
    }
}
