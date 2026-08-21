using System;
using System.Collections.Generic;
using DataSense.Helpers;

namespace DataSense.Models;

public enum SessionStatusEnum
{
    Active,
    Completed,
    Interrupted,
    Disconnected,
    Unknown
}

public class NetworkSessionItem
{
    public NetworkSession Session { get; set; } = new();
    public SessionStatusEnum Status { get; set; } = SessionStatusEnum.Unknown;
    public string StatusText => Status.ToString();
    public string InterruptionReason { get; set; } = string.Empty;

    public string NetworkName => Session.NetworkName;
    public string ConnectionType => Session.ConnectionType;
    public string InterfaceName => Session.InterfaceName;
    public DateTime StartTime => Session.StartTime;
    public DateTime? EndTime => Session.EndTime;
    public TimeSpan Duration => Session.Duration;

    public string FormattedDuration => Duration.TotalHours >= 1 
        ? $"{(int)Duration.TotalHours}h {Duration.Minutes}m" 
        : $"{Duration.Minutes}m {Duration.Seconds}s";

    public long BytesDownloaded => Session.BytesDownloaded;
    public long BytesUploaded => Session.BytesUploaded;
    public long TotalBytes => Session.TotalBytes;

    public string FormattedDownload => ByteFormatter.FormatBytes(BytesDownloaded);
    public string FormattedUpload => ByteFormatter.FormatBytes(BytesUploaded);
    public string FormattedTotal => ByteFormatter.FormatBytes(TotalBytes);

    public double? AverageDownloadSpeedBps { get; set; }
    public double? AverageUploadSpeedBps { get; set; }
    public double? PeakDownloadSpeedBps { get; set; }
    public double? PeakUploadSpeedBps { get; set; }

    public string FormattedAvgDownloadSpeed => AverageDownloadSpeedBps.HasValue
        ? ByteFormatter.FormatSpeed(AverageDownloadSpeedBps.Value)
        : "Insufficient telemetry";

    public string FormattedAvgUploadSpeed => AverageUploadSpeedBps.HasValue
        ? ByteFormatter.FormatSpeed(AverageUploadSpeedBps.Value)
        : "Insufficient telemetry";

    public string FormattedPeakDownloadSpeed => PeakDownloadSpeedBps.HasValue
        ? ByteFormatter.FormatSpeed(PeakDownloadSpeedBps.Value)
        : "Insufficient telemetry";

    public string FormattedPeakUploadSpeed => PeakUploadSpeedBps.HasValue
        ? ByteFormatter.FormatSpeed(PeakUploadSpeedBps.Value)
        : "Insufficient telemetry";

    public string ColorHex
    {
        get
        {
            if (string.IsNullOrEmpty(NetworkName)) return "#94A3B8";
            if (ConnectionType.Equals("Wi-Fi", StringComparison.OrdinalIgnoreCase) || ConnectionType.Equals("wifi", StringComparison.OrdinalIgnoreCase))
                return "#38BDF8";
            if (ConnectionType.Equals("Ethernet", StringComparison.OrdinalIgnoreCase) || ConnectionType.Equals("ethernet", StringComparison.OrdinalIgnoreCase))
                return "#10B981";
            if (ConnectionType.Equals("Mobile", StringComparison.OrdinalIgnoreCase) || ConnectionType.Equals("cellular", StringComparison.OrdinalIgnoreCase))
                return "#C084FC";
            
            // Deterministic hash color fallback
            int hash = Math.Abs(NetworkName.GetHashCode());
            string[] palette = { "#38BDF8", "#10B981", "#C084FC", "#F59E0B", "#EC4899", "#6366F1" };
            return palette[hash % palette.Length];
        }
    }
}

public class SessionProcessAttribution
{
    public string ProcessName { get; set; } = string.Empty;
    public int Pid { get; set; }
    public string ExecutablePath { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public long BytesDownloaded { get; set; }
    public long BytesUploaded { get; set; }
    public long TotalBytes => BytesDownloaded + BytesUploaded;
    public double PercentageOfSessionTraffic { get; set; }

    public string FormattedTotal => ByteFormatter.FormatBytes(TotalBytes);
    public string FormattedDownload => ByteFormatter.FormatBytes(BytesDownloaded);
    public string FormattedUpload => ByteFormatter.FormatBytes(BytesUploaded);
}

public class SessionComparisonResult
{
    public bool HasSufficientData { get; set; }
    public int ComparableSessionCount { get; set; }
    public long HistoricalAverageBytes { get; set; }
    public TimeSpan HistoricalAverageDuration { get; set; }
    public double HistoricalAverageSpeedBps { get; set; }
    public double UsageDifferencePercentage { get; set; }
    public double DurationDifferencePercentage { get; set; }
    public List<string> ComparisonStatements { get; set; } = new();
    public string StatusMessage { get; set; } = string.Empty;
}

public class NetworkSessionPattern
{
    public string NetworkName { get; set; } = string.Empty;
    public TimeSpan TypicalDuration { get; set; }
    public string FormattedTypicalDuration => TypicalDuration.TotalHours >= 1 
        ? $"{(int)TypicalDuration.TotalHours}h {TypicalDuration.Minutes}m" 
        : $"{TypicalDuration.Minutes}m {TypicalDuration.Seconds}s";

    public long AverageUsageBytes { get; set; }
    public string FormattedAverageUsage => ByteFormatter.FormatBytes(AverageUsageBytes);

    public string TypicalStartTimeOfDay { get; set; } = "—";
    public string TypicalEndTimeOfDay { get; set; } = "—";
    public int SessionCount { get; set; }
}

public class NetworkSwitchItem
{
    public DateTime Timestamp { get; set; }
    public string OldNetwork { get; set; } = string.Empty;
    public string NewNetwork { get; set; } = string.Empty;
    public string ConnectionType { get; set; } = string.Empty;

    public double? TrafficBeforeDownloadBps { get; set; }
    public double? TrafficBeforeUploadBps { get; set; }
    public double? TrafficAfterDownloadBps { get; set; }
    public double? TrafficAfterUploadBps { get; set; }

    public int? ActiveProcessCountBefore { get; set; }
    public int? ActiveProcessCountAfter { get; set; }

    public string SummaryText => $"{OldNetwork} → {NewNetwork}";
}

public class SessionIntelligenceInsight
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Severity { get; set; } = "Info"; // Info, Warning, Critical
}
