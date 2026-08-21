using System;

namespace DataSense.Models;

public class NetworkProfile
{
    public string NetworkName { get; init; } = string.Empty;
    public string ConnectionType { get; init; } = string.Empty;
    public string InterfaceName { get; init; } = string.Empty;
    public DateTime FirstSeenAt { get; init; }
    public DateTime LastSeenAt { get; init; }
    public long TotalSessions { get; init; }
    public TimeSpan TotalConnectionDuration { get; init; }
    public long TotalDownloadBytes { get; init; }
    public long TotalUploadBytes { get; init; }
    public long TotalBytes => TotalDownloadBytes + TotalUploadBytes;
    public bool IsCurrentlyConnected { get; set; }
}

public class NetworkPerformanceProfile
{
    public string NetworkName { get; init; } = string.Empty;
    public double AverageDownloadSpeed { get; init; }
    public double AverageUploadSpeed { get; init; }
    public double AverageLatency { get; init; }
    public int SpeedTestCount { get; init; }
    public double StabilityScore { get; init; } = 100.0;
    public double ReliabilityScore { get; init; } = 100.0;
    public double PerformanceScore { get; set; } = 0;
}
