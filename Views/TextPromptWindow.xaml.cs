using System.Windows;

namespace ServerControlCenter.Views;

public partial class TextPromptWindow : Window
{
    public TextPromptWindow(string title, string prompt, string initialValue = "")
    {
        InitializeComponent();

        Title = title;
        PromptTextBlock.Text = prompt;
        ValueTextBox.Text = initialValue;
        ValueTextBox.SelectAll();
        ValueTextBox.Focus();
    }

    public string Value => ValueTextBox.Text;

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
