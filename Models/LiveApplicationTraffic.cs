using System;
using System.Collections.Generic;

namespace DataSense.Models;

public class LiveApplicationTraffic
{
    public string ProcessName { get; set; } = string.Empty;
    public int Pid { get; set; }
    public DateTime StartTime { get; set; }
    public string ProcessIdentity { get; set; } = string.Empty; // composite key
    public string ExecutablePath { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string DataSource { get; set; } = "Nethogs";
    public string NetworkName { get; set; } = "Unknown network";
    public string ConnectionType { get; set; } = "Unknown";
    public string InterfaceName { get; set; } = "Unknown";
    public double DownloadBytesPerSecond { get; set; }
    public double UploadBytesPerSecond { get; set; }
    public double TotalBytesPerSecond => DownloadBytesPerSecond + UploadBytesPerSecond;
    public long DownloadBytesSinceSample { get; set; }
    public long UploadBytesSinceSample { get; set; }
    public DateTime LastObservedAt { get; set; }
    public bool IsActive { get; set; }
    public string ActivityState { get; set; } = "Idle"; // Active, Idle, Recently Active, Unavailable
}

public class LiveApplicationSession
{
    public string ProcessIdentity { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public int Pid { get; set; }
    public DateTime SessionStart { get; set; }
    public DateTime LastActive { get; set; }
    public long AccumulatedBytes { get; set; }
    public double PeakDownloadRate { get; set; }
    public double PeakUploadRate { get; set; }
    public bool IsClosed { get; set; }
}
