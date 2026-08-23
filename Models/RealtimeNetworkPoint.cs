using System;
using DataSense.Helpers;

namespace DataSense.Models;

/// <summary>
/// Represents a single real-time network traffic sample point for the Dashboard live graph.
/// </summary>
public class RealtimeNetworkPoint
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public double DownloadRateBytesPerSec { get; set; }
    public double UploadRateBytesPerSec { get; set; }

    // Canvas geometry properties for UI binding
    public double CanvasX { get; set; }
    public double DownloadY { get; set; }
    public double UploadY { get; set; }

    public double CombinedRateBytesPerSec => DownloadRateBytesPerSec + UploadRateBytesPerSec;

    public string TimeText => Timestamp.ToString("HH:mm:ss");
    public string ShortTimeText => Timestamp.ToString("HH:mm");
    public string DownloadSpeedText => ByteFormatter.FormatSpeed(DownloadRateBytesPerSec);
    public string UploadSpeedText => ByteFormatter.FormatSpeed(UploadRateBytesPerSec);
    public string TotalSpeedText => ByteFormatter.FormatSpeed(CombinedRateBytesPerSec);
}
