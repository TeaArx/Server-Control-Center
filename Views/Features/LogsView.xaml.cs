using System.Windows.Controls;
using System.Windows.Input;
using ServerControlCenter.ViewModels;

namespace ServerControlCenter.Views.Features;

public partial class LogsView : UserControl
{
    public LogsView()
    {
        InitializeComponent();
    }

    private void LogPathTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainViewModel viewModel &&
            viewModel.LoadLogCommand.CanExecute(null))
        {
            e.Handled = true;
            viewModel.LoadLogCommand.Execute(null);
        }
    }
}
