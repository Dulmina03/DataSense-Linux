using System;
using DataSense.Helpers;

namespace DataSense.Models;

/// <summary>
/// Represents a single live network throughput sample recorded at 1-second intervals
/// for the rolling 60-second live speed graph.
/// </summary>
public sealed class LiveThroughputSample
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public double DownloadBytesPerSecond { get; init; }
    public double UploadBytesPerSecond { get; init; }
    public int SecondsAgo { get; set; }

    // Pre-calculated Canvas coordinates for fast geometry rendering and hover collision
    public double CanvasX { get; set; }
    public double DownloadY { get; set; }
    public double UploadY { get; set; }

    // Upload Bar Geometry for the hybrid bar + wave chart
    public double BarWidth { get; set; } = 4.0;
    public double BarHeight { get; set; } = 0.0;
    public double BarLeftX { get; set; } = 0.0;
    public double BarTopY { get; set; } = 170.0;

    public bool IsPeriodUsage { get; set; } = false;

    public double CombinedBytesPerSecond => DownloadBytesPerSecond + UploadBytesPerSecond;

    public string DownloadSpeedText => IsPeriodUsage 
        ? ByteFormatter.FormatBytes((long)DownloadBytesPerSecond) 
        : ByteFormatter.FormatSpeed(DownloadBytesPerSecond);

    public string UploadSpeedText => IsPeriodUsage 
        ? ByteFormatter.FormatBytes((long)UploadBytesPerSecond) 
        : ByteFormatter.FormatSpeed(UploadBytesPerSecond);

    public string TotalSpeedText => IsPeriodUsage 
        ? ByteFormatter.FormatBytes((long)CombinedBytesPerSecond) 
        : ByteFormatter.FormatSpeed(CombinedBytesPerSecond);

    public string FormattedTime => IsPeriodUsage 
        ? (Timestamp.Hour == 0 && Timestamp.Minute == 0 ? Timestamp.ToString("MMM d") : Timestamp.ToString("HH:mm")) 
        : Timestamp.ToString("HH:mm:ss");

    public string ShortTime => IsPeriodUsage ? FormattedTime : Timestamp.ToString("HH:mm");

    public string TimeText => SecondsAgo == 0 
        ? $"NOW ({Timestamp:HH:mm:ss})" 
        : $"-{SecondsAgo}s ({Timestamp:HH:mm:ss})";

    public string ShortTimeText => SecondsAgo == 0 ? "NOW" : $"-{SecondsAgo}s";
}
