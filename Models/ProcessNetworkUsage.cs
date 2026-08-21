using System;

namespace DataSense.Models;

/// <summary>
/// A snapshot of per-process network usage emitted by the process monitoring backend.
/// Contains both instantaneous rates (B/s) and integrated byte counts over sample intervals.
/// </summary>
public class ProcessNetworkUsage
{
    public string ProcessIdentifier { get; init; } = string.Empty; // e.g. "chrome", "code"
    public string ExecutablePath { get; init; } = string.Empty;
    public int Pid { get; init; }
    public string User { get; init; } = "unknown";

    // Instantaneous rates in bytes per second
    public double DownloadRateBytesPerSec { get; init; }
    public double UploadRateBytesPerSec { get; init; }

    // Integrated byte counts for the sample window
    public long DownloadBytes { get; init; }
    public long UploadBytes { get; init; }
    public long TotalBytes => DownloadBytes + UploadBytes;

    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public DateTime? FirstSeen { get; init; }
    public DateTime? LastSeen { get; init; }
    public bool IsActive { get; init; } = true;
    public string DataSource { get; init; } = "Nethogs";

    /// <summary>
    /// Composite key combining PID and start time ticks to distinguish PID reuse.
    /// </summary>
    public string ProcessIdentityKey { get; init; } = string.Empty;
}
