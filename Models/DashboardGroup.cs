using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ServerControlCenter.Models;

public class DashboardGroup : INotifyPropertyChanged
{
    private bool isSelected;

    public string Name { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public int Count { get; set; }

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value)
            {
                return;
            }

            isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
