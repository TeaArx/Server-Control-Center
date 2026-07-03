namespace ServerControlCenter.Models;

public class ActivityLogEntry
{
    public int Id { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string Action { get; set; } = "";

    public string Target { get; set; } = "";

    public string Details { get; set; } = "";

    public string Level { get; set; } = "info";
}
