using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.CompilerServices;

namespace ServerControlCenter.Models;

public class ServerProfile : INotifyPropertyChanged
{
    private bool isOnline;
    private bool isFavorite;

    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 22;

    public string Username { get; set; } = string.Empty;

    public string? Password { get; set; }

    public string? PrivateKeyPath { get; set; }

    public string? Notes { get; set; }

    public string GroupName { get; set; } = "Production";

    public string? OsName { get; set; }

    public string? IpAddressDisplay { get; set; }

    public bool IsFavorite
    {
        get => isFavorite;
        set
        {
            if (isFavorite == value)
            {
                return;
            }

            isFavorite = value;
            OnPropertyChanged();
        }
    }

    [NotMapped]
    public bool HasUnreadablePassword { get; set; }

    [NotMapped]
    public string DisplayAddress => string.IsNullOrWhiteSpace(IpAddressDisplay)
        ? Host
        : IpAddressDisplay;

    [NotMapped]
    public string DisplayOs => string.IsNullOrWhiteSpace(OsName)
        ? "Unknown Linux"
        : OsName;

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
