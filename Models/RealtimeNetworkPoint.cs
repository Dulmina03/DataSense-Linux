using System;
using DataSense.Helpers;

namespace DataSense.Models;

/// <summary>
/// Represents a single network traffic visualization point for the Dashboard dual-series analytics graph.
/// </summary>
public class RealtimeNetworkPoint
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public double DownloadRateBytesPerSec { get; set; }
    public double UploadRateBytesPerSec { get; set; }
    public long DownloadBytes { get; set; }
    public long UploadBytes { get; set; }
    public string CustomLabel { get; set; } = string.Empty;

    // Canvas geometry properties for UI binding
    public double CanvasX { get; set; }
    public double DownloadY { get; set; }
    public double UploadY { get; set; }

    public double CombinedRateBytesPerSec => DownloadRateBytesPerSec + UploadRateBytesPerSec;
    public long CombinedBytes => DownloadBytes + UploadBytes;

    public string TimeText => !string.IsNullOrEmpty(CustomLabel) ? CustomLabel : Timestamp.ToString("HH:mm:ss");
    public string ShortTimeText => !string.IsNullOrEmpty(CustomLabel) ? CustomLabel : Timestamp.ToString("HH:mm");
    
    public string DownloadSpeedText => DownloadBytes > 0 
        ? ByteFormatter.FormatBytes(DownloadBytes) 
        : ByteFormatter.FormatSpeed(DownloadRateBytesPerSec);
        
    public string UploadSpeedText => UploadBytes > 0 
        ? ByteFormatter.FormatBytes(UploadBytes) 
        : ByteFormatter.FormatSpeed(UploadRateBytesPerSec);
        
    public string TotalSpeedText => CombinedBytes > 0 
        ? ByteFormatter.FormatBytes(CombinedBytes) 
        : ByteFormatter.FormatSpeed(CombinedRateBytesPerSec);
}
