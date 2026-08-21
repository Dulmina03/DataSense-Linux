using System;

namespace DataSense.Models;

public class ProcessUsageRecord
{
    public long Id { get; set; }

    /// <summary>Stable identifier for the process (e.g., "chrome", "code")</summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>Full executable path if available from /proc</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>Linux user who owns the process</summary>
    public string UserName { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; }

    public long BytesDownloaded { get; set; }

    public long BytesUploaded { get; set; }

    public long TotalBytes => BytesDownloaded + BytesUploaded;

    /// <summary>Backend that produced this record (e.g., "Nethogs")</summary>
    public string DataSource { get; set; } = "Nethogs";
}
