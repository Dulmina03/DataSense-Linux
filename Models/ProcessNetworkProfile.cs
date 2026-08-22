using System;

namespace DataSense.Models;

public class ProcessNetworkProfile
{
    public string ProcessName { get; set; } = string.Empty;
    public int Pid { get; set; }
    public long StartTimeTicks { get; set; }
    
    public string NetworkName { get; set; } = string.Empty;
    public string ConnectionType { get; set; } = string.Empty;
    public string InterfaceName { get; set; } = string.Empty;
    
    public long DownloadBytes { get; set; }
    public long UploadBytes { get; set; }
    public long TotalBytes => DownloadBytes + UploadBytes;
    
    public int SessionCount { get; set; }
    public int ActiveDays { get; set; }
    
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    
    public int SampleCount { get; set; }
    public double PercentageOfNetworkUsage { get; set; }
    public bool HasHistoricalData { get; set; }
}

public class ProcessNetworkUsageSummary
{
    public string ProcessName { get; set; } = string.Empty;
    public int Pid { get; set; }
    public long StartTimeTicks { get; set; }
    public string ExecutablePath { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    
    public long DownloadBytes { get; set; }
    public long UploadBytes { get; set; }
    public long TotalBytes => DownloadBytes + UploadBytes;
    
    public double PercentageOfTotal { get; set; }
    public int Rank { get; set; }
}

public enum ProcessNetworkInsightSeverity
{
    Info,
    Warning,
    Critical
}

public class ProcessNetworkInsight
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string NetworkName { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public ProcessNetworkInsightSeverity Severity { get; set; } = ProcessNetworkInsightSeverity.Info;
    public string ActionableStep { get; set; } = string.Empty;
}

public class ProcessNetworkAnomaly
{
    public string ProcessName { get; set; } = string.Empty;
    public int Pid { get; set; }
    public long StartTimeTicks { get; set; }
    public string NetworkName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Description { get; set; } = string.Empty;
    public long ExcessBytes { get; set; }
    public double DeviationSigma { get; set; }
}
