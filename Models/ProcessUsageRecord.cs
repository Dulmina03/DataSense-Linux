using System;

namespace DataSense.Models;

public class ProcessUsageRecord
{
    public long Id { get; set; }
    
    /// <summary>Stable identifier for the process (e.g., "chrome", "code")</summary>
    public string ProcessName { get; set; } = string.Empty;
    
    public DateTime Timestamp { get; set; }
    
    public long BytesDownloaded { get; set; }
    
    public long BytesUploaded { get; set; }
    
    public long TotalBytes => BytesDownloaded + BytesUploaded;
}
