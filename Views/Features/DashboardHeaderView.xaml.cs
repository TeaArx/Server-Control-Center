using System.Windows.Controls;

namespace ServerControlCenter.Views.Features;

public partial class DashboardHeaderView : UserControl
{
    public DashboardHeaderView()
    {
        InitializeComponent();
    }

    public void FocusSearch()
    {
        ServerSearchBox.Focus();
        ServerSearchBox.SelectAll();
    }
}
