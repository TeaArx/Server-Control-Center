using System.Windows.Controls;
using System.Windows.Input;

namespace ServerControlCenter.Views.Features;

public partial class OverlayHostView : UserControl
{
    public OverlayHostView()
    {
        InitializeComponent();
    }

    private void NumericTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = e.Text.Any(character => !char.IsDigit(character));
    }
}
