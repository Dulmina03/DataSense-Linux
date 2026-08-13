using System;

namespace DataSense.Models;

/// <summary>
/// Represents aggregated network usage for a single calendar day.
/// BytesDownloaded = MAX(BytesReceived) - MIN(BytesReceived) for that day.
/// BytesUploaded   = MAX(BytesSent)     - MIN(BytesSent)     for that day.
/// Negative differences (counter resets) are clamped to 0.
/// </summary>
public class DailyUsageRecord
{
    public DateTime Day { get; set; }          // Local date (date only, time = midnight)
    public long BytesDownloaded { get; set; }  // Daily download delta from cumulative counter
    public long BytesUploaded { get; set; }    // Daily upload delta from cumulative counter
    public long TotalBytes => BytesDownloaded + BytesUploaded;
}
