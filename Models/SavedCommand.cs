namespace ServerControlCenter.Models;

public class SavedCommand
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;
}