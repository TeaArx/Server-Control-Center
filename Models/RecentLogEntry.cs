namespace ServerControlCenter.Models;

public class RecentLogEntry
{
    public string Time { get; set; } = "";

    public string Service { get; set; } = "";

    public string Level { get; set; } = "info";

    public string Message { get; set; } = "";

    public string LevelText => $"[{Level}]";
}
