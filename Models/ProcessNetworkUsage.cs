using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using DataSense.Helpers;

namespace DataSense.Models;

/// <summary>
/// A snapshot of per-process network usage emitted by the process monitoring backend.
/// Contains both instantaneous rates (B/s) and integrated byte counts over sample intervals.
/// Implements ObservableObject so in-place telemetry updates don't recreate UI visual rows.
/// </summary>
public class ProcessNetworkUsage : ObservableObject
{
    private string _processIdentifier = string.Empty;
    public string ProcessIdentifier { get => _processIdentifier; init => _processIdentifier = value; }

    private string _executablePath = string.Empty;
    public string ExecutablePath { get => _executablePath; init => _executablePath = value; }

    private int _pid;
    public int Pid { get => _pid; init => _pid = value; }

    private string _user = "unknown";
    public string User { get => _user; init => _user = value; }

    private double _downloadRateBytesPerSec;
    public double DownloadRateBytesPerSec
    {
        get => _downloadRateBytesPerSec;
        set
        {
            if (SetProperty(ref _downloadRateBytesPerSec, value))
            {
                OnPropertyChanged(nameof(DownloadRateText));
                OnPropertyChanged(nameof(TotalRateBytesPerSec));
                OnPropertyChanged(nameof(TotalRateText));
                OnPropertyChanged(nameof(IsCurrentlyActive));
                OnPropertyChanged(nameof(ActivityText));
                OnPropertyChanged(nameof(ActivityColor));
            }
        }
    }

    private double _uploadRateBytesPerSec;
    public double UploadRateBytesPerSec
    {
        get => _uploadRateBytesPerSec;
        set
        {
            if (SetProperty(ref _uploadRateBytesPerSec, value))
            {
                OnPropertyChanged(nameof(UploadRateText));
                OnPropertyChanged(nameof(TotalRateBytesPerSec));
                OnPropertyChanged(nameof(TotalRateText));
                OnPropertyChanged(nameof(IsCurrentlyActive));
                OnPropertyChanged(nameof(ActivityText));
                OnPropertyChanged(nameof(ActivityColor));
            }
        }
    }

    private long _downloadBytes;
    public long DownloadBytes
    {
        get => _downloadBytes;
        set
        {
            if (SetProperty(ref _downloadBytes, value))
            {
                OnPropertyChanged(nameof(TotalBytes));
                OnPropertyChanged(nameof(TotalDataText));
                OnPropertyChanged(nameof(TotalRateText));
                OnPropertyChanged(nameof(FormattedDownloadDataText));
            }
        }
    }

    private long _uploadBytes;
    public long UploadBytes
    {
        get => _uploadBytes;
        set
        {
            if (SetProperty(ref _uploadBytes, value))
            {
                OnPropertyChanged(nameof(TotalBytes));
                OnPropertyChanged(nameof(TotalDataText));
                OnPropertyChanged(nameof(TotalRateText));
                OnPropertyChanged(nameof(FormattedUploadDataText));
            }
        }
    }

    public long TotalBytes => DownloadBytes + UploadBytes;
    public double TotalRateBytesPerSec => DownloadRateBytesPerSec + UploadRateBytesPerSec;

    private DateTime _timestamp = DateTime.UtcNow;
    public DateTime Timestamp { get => _timestamp; set => SetProperty(ref _timestamp, value); }

    public DateTime? FirstSeen { get; init; }
    public DateTime? LastSeen { get; init; }

    private bool _isActive = true;
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetProperty(ref _isActive, value))
            {
                OnPropertyChanged(nameof(IsCurrentlyActive));
                OnPropertyChanged(nameof(ActivityText));
                OnPropertyChanged(nameof(ActivityColor));
            }
        }
    }

    public string DataSource { get; init; } = "Nethogs";
    public string ProcessIdentityKey { get; init; } = string.Empty;

    private string _applicationDisplayName = string.Empty;
    public string ApplicationDisplayName
    {
        get => _applicationDisplayName;
        set
        {
            if (SetProperty(ref _applicationDisplayName, value))
            {
                OnPropertyChanged(nameof(EffectiveDisplayName));
                OnPropertyChanged(nameof(HasDistinctProcessIdentifier));
            }
        }
    }

    private IImage? _applicationIcon;
    public IImage? ApplicationIcon
    {
        get => _applicationIcon;
        set => SetProperty(ref _applicationIcon, value);
    }

    public bool HasDistinctProcessIdentifier =>
        !string.IsNullOrWhiteSpace(ApplicationDisplayName) &&
        !string.Equals(ApplicationDisplayName, ProcessIdentifier, StringComparison.OrdinalIgnoreCase);

    public string EffectiveDisplayName =>
        !string.IsNullOrWhiteSpace(ApplicationDisplayName) ? ApplicationDisplayName : ProcessIdentifier;

    public string DownloadRateText => ByteFormatter.FormatSpeed(DownloadRateBytesPerSec);
    public string UploadRateText => ByteFormatter.FormatSpeed(UploadRateBytesPerSec);

    public string TotalRateText => TotalRateBytesPerSec > 0
        ? ByteFormatter.FormatSpeed(TotalRateBytesPerSec)
        : ByteFormatter.FormatBytes(TotalBytes);

    public string TotalDataText => ByteFormatter.FormatBytes(TotalBytes);
    public string FormattedDownloadDataText => ByteFormatter.FormatBytes(DownloadBytes);
    public string FormattedUploadDataText => ByteFormatter.FormatBytes(UploadBytes);

    public bool IsCurrentlyActive => IsActive && (DownloadRateBytesPerSec > 0 || UploadRateBytesPerSec > 0);
    public string ActivityText => IsCurrentlyActive ? "Active" : "Idle";
    public string ActivityColor => IsCurrentlyActive ? "Success" : "Muted";

    public string TooltipSummary =>
        $"{EffectiveDisplayName}\n\n" +
        $"Process: {ProcessIdentifier}\n" +
        $"Download Speed: {DownloadRateText}\n" +
        $"Upload Speed: {UploadRateText}\n" +
        $"Downloaded: {FormattedDownloadDataText}\n" +
        $"Uploaded: {FormattedUploadDataText}\n" +
        $"Total Data: {TotalDataText}\n" +
        $"Status: {ActivityText}";

    public void UpdateFrom(ProcessNetworkUsage other)
    {
        DownloadRateBytesPerSec = other.DownloadRateBytesPerSec;
        UploadRateBytesPerSec = other.UploadRateBytesPerSec;
        DownloadBytes = other.DownloadBytes;
        UploadBytes = other.UploadBytes;
        Timestamp = other.Timestamp;
        IsActive = other.IsActive;
        if (!string.IsNullOrWhiteSpace(other.ApplicationDisplayName) && string.IsNullOrWhiteSpace(ApplicationDisplayName))
        {
            ApplicationDisplayName = other.ApplicationDisplayName;
        }
        if (other.ApplicationIcon != null && ApplicationIcon == null)
        {
            ApplicationIcon = other.ApplicationIcon;
        }
    }
}
