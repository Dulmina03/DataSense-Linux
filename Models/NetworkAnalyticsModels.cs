using System;

namespace DataSense.Models;

/// <summary>Summary statistics for a specific network over a selected time range.</summary>
public class NetworkAnalyticsSummary
{
    public long TotalDownloaded { get; init; }
    public long TotalUploaded   { get; init; }
    public long TotalUsage      => TotalDownloaded + TotalUploaded;
    public TimeSpan TotalConnectionTime { get; init; }
    public int TotalSessions { get; init; }
    public DateTime? FirstConnected { get; init; }
    public DateTime? LastConnected { get; init; }
}

/// <summary>Aggregated speed-test performance for a specific network.</summary>
public class NetworkPerformanceSummary
{
    public double AvgDownloadMbps  { get; init; }
    public double BestDownloadMbps { get; init; }
    public double AvgUploadMbps    { get; init; }
    public double BestUploadMbps   { get; init; }
    public double AvgPingMs        { get; init; }
    public double BestPingMs       { get; init; }
    public int    TotalTests       { get; init; }
}

/// <summary>Comparison row representing one network's aggregated historical metrics.</summary>
public class NetworkComparisonRecord
{
    public string    NetworkName         { get; init; } = string.Empty;
    public string    ConnectionType      { get; init; } = string.Empty;
    public long      TotalUsage          { get; init; }
    public TimeSpan  TotalConnectionTime { get; init; }
    public int       SessionsCount       { get; init; }
    public double    AvgDownloadMbps     { get; set;  }
    public double    AvgUploadMbps       { get; set;  }
}
