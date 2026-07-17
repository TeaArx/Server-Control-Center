using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ServerControlCenter.ViewModels;

namespace ServerControlCenter.Views.Features;

public partial class TerminalView : UserControl
{
    private readonly List<string> terminalHistory = [];
    private int terminalHistoryIndex;

    public TerminalView()
    {
        InitializeComponent();
    }

    private void TerminalCommandTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox input || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (control && e.Key == Key.C && input.SelectionLength == 0)
        {
            InterruptTerminal(viewModel);
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
            viewModel.ClearTerminalCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up && terminalHistory.Count > 0)
        {
            terminalHistoryIndex = Math.Max(0, terminalHistoryIndex - 1);
            viewModel.TerminalCommand = terminalHistory[terminalHistoryIndex];
            input.CaretIndex = input.Text.Length;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down && terminalHistory.Count > 0)
        {
            terminalHistoryIndex = Math.Min(terminalHistory.Count, terminalHistoryIndex + 1);
            viewModel.TerminalCommand = terminalHistoryIndex == terminalHistory.Count
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

        if (!string.IsNullOrWhiteSpace(viewModel.TerminalCommand) &&
            (terminalHistory.Count == 0 || terminalHistory[^1] != viewModel.TerminalCommand))
        {
            terminalHistory.Add(viewModel.TerminalCommand);
            terminalHistoryIndex = terminalHistory.Count;
        }

        if (viewModel.SendTerminalCommandCommand.CanExecute(null))
        {
            viewModel.SendTerminalCommandCommand.Execute(null);
        }
    }

    private void TerminalOutputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox output || DataContext is not MainViewModel viewModel)
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
                InterruptTerminal(viewModel);
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
            viewModel.ClearTerminalCommand.Execute(null);
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

    private void OpenButtonContextMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.ContextMenu is null)
        {
            return;
        }

        element.ContextMenu.PlacementTarget = element;
        element.ContextMenu.IsOpen = true;
    }
}
