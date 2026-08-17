using System;

namespace DataSense.Models;

/// <summary>
/// A single snapshot of instantaneous network usage for a process,
/// emitted by the process network monitoring backend (e.g. nethogs).
/// </summary>
public class ProcessNetworkUsage
{
    public string ProcessIdentifier { get; init; } = string.Empty; // e.g. "chrome", "code"
    public string ExecutablePath { get; init; } = string.Empty;
    public int Pid { get; init; }
    public string User { get; init; } = string.Empty;
    
    // Instantaneous rates in bytes per second
    public double DownloadRateBytesPerSec { get; init; }
    public double UploadRateBytesPerSec { get; init; }
    
    public DateTime Timestamp { get; init; }
}
