using System;
using Avalonia.Media;

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

    /// <summary>
    /// Resolved human-friendly application display name (e.g. "Brave Web Browser", "Visual Studio Code").
    /// </summary>
    public string ApplicationDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Resolved application icon from Linux desktop theme or clean generic fallback.
    /// </summary>
    public IImage? ApplicationIcon { get; set; }

    /// <summary>
    /// Indicates whether the display name is distinct from the raw process identifier.
    /// </summary>
    public bool HasDistinctProcessIdentifier =>
        !string.IsNullOrWhiteSpace(ApplicationDisplayName) &&
        !string.Equals(ApplicationDisplayName, ProcessIdentifier, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Effective display name for UI presentation.
    /// </summary>
    public string EffectiveDisplayName =>
        !string.IsNullOrWhiteSpace(ApplicationDisplayName) ? ApplicationDisplayName : ProcessIdentifier;
}
