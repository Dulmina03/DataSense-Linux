using System;

namespace DataSense.Models;

public class NetworkSession
{
    public long Id { get; set; }
    public string NetworkName { get; set; } = string.Empty;
    public string InterfaceName { get; set; } = string.Empty;
    public string ConnectionType { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public long BytesDownloaded { get; set; }
    public long BytesUploaded { get; set; }
    
    public TimeSpan Duration => EndTime.HasValue 
        ? EndTime.Value - StartTime 
        : DateTime.UtcNow - StartTime;
}
