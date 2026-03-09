namespace TunnelManager.Shared.Models;

public class TunnelStatus
{
    public string Hostname { get; set; } = string.Empty;
    public int LocalPort { get; set; }
    public bool IsRunning { get; set; }
    public int RestartCount { get; set; }
    public DateTime LastChecked { get; set; } = DateTime.UtcNow;
}
