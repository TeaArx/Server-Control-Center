using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.CompilerServices;

namespace ServerControlCenter.Models;

public class ServerProfile : INotifyPropertyChanged
{
    private bool isOnline;

    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 22;

    public string Username { get; set; } = string.Empty;

    public string? Password { get; set; }

    public string? PrivateKeyPath { get; set; }

    public string? Notes { get; set; }

    [NotMapped]
    public bool IsOnline
    {
        get => isOnline;
        set
        {
            if (isOnline == value)
            {
                return;
            }

            isOnline = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
