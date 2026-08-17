namespace DataSense.Models;

/// <summary>
/// Aggregated network usage for a single clock hour within a calendar day.
/// BytesDownloaded = MAX(BytesReceived) – MIN(BytesReceived) within that hour.
/// Negative deltas (counter resets) are clamped to 0.
/// </summary>
public class HourlyUsageRecord
{
    /// <summary>Hour of day, 0–23 (UTC).</summary>
    public int Hour { get; set; }

    public long BytesDownloaded { get; set; }
    public long BytesUploaded   { get; set; }
    public long TotalBytes      => BytesDownloaded + BytesUploaded;
}
