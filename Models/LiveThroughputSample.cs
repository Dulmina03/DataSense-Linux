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

    public double CombinedBytesPerSecond => DownloadBytesPerSecond + UploadBytesPerSecond;

    public string DownloadSpeedText => ByteFormatter.FormatSpeed(DownloadBytesPerSecond);
    public string UploadSpeedText => ByteFormatter.FormatSpeed(UploadBytesPerSecond);
    public string TotalSpeedText => ByteFormatter.FormatSpeed(CombinedBytesPerSecond);

    public string TimeText => SecondsAgo == 0 
        ? $"NOW ({Timestamp:HH:mm:ss})" 
        : $"-{SecondsAgo}s ({Timestamp:HH:mm:ss})";

    public string ShortTimeText => SecondsAgo == 0 ? "NOW" : $"-{SecondsAgo}s";
}
