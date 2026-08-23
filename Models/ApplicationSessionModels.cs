using System;
using System.Collections.Generic;

namespace DataSense.Models;

public class ApplicationSession
{
    public string ProcessName { get; init; } = string.Empty;
    public int Pid { get; init; }
    public string ExecutablePath { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string DataSource { get; init; } = string.Empty;
    public long StartTimeTicks { get; init; }

    public DateTime SessionStart { get; set; }
    public DateTime SessionEnd { get; set; }
    public TimeSpan Duration => SessionEnd - SessionStart;
    
    public long DownloadBytes { get; set; }
    public long UploadBytes { get; set; }
    public long TotalBytes => DownloadBytes + UploadBytes;

    public string NetworkName { get; set; } = "Unknown";
    public string ConnectionType { get; set; } = "Unknown";
    public bool IsActive { get; set; }
}

public class ApplicationLifecycleSummary
{
    public string ProcessName { get; init; } = string.Empty;
    public DateTime? FirstObserved { get; set; }
    public DateTime? LastObserved { get; set; }
    public int TotalSessions { get; set; }
    public TimeSpan TotalActiveDuration { get; set; }
    public TimeSpan AverageSessionDuration { get; set; }
    public TimeSpan LongestSession { get; set; }
    
    public int TodaySessionCount { get; set; }
    public TimeSpan TodayActiveDuration { get; set; }
    public long TodayUsage { get; set; }
    
    public int SevenDaySessionCount { get; set; }
    public TimeSpan SevenDayActiveDuration { get; set; }
    public long SevenDayUsage { get; set; }
    
    public bool IsCurrentlyActive { get; set; }
}
