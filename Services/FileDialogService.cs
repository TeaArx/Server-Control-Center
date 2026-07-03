using Microsoft.Win32;
using Ookii.Dialogs.Wpf;

namespace ServerControlCenter.Services;

public class FileDialogService
{
    public string? PickFile()
    {
        var dialog = new OpenFileDialog();

        return dialog.ShowDialog() == true
            ? dialog.FileName
            : null;
    }

    public string? PickSaveFile()
    {
        var dialog = new SaveFileDialog();

        return dialog.ShowDialog() == true
            ? dialog.FileName
            : null;
    }

    public string? PickFolder()
    {
        var dialog = new VistaFolderBrowserDialog();

        return dialog.ShowDialog() == true
            ? dialog.SelectedPath
            : null;
    }
}