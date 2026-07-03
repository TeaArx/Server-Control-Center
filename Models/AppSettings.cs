namespace ServerControlCenter.Models;

public class AppSettings
{
    public int Id { get; set; }

    public int MonitoringIntervalSeconds { get; set; } = 30;

    public string DefaultRemoteFolder { get; set; } = "/var/www";

    public string DefaultLogPath { get; set; } = "/var/log/nginx/error.log";

    public bool AutoRefreshMonitoring { get; set; } = true;
}
