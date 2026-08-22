using System;

namespace DataSense.Models;

public class ApplicationNetworkProfile
{
    public string ProcessName { get; set; } = string.Empty;
    public int Pid { get; set; }
    public long StartTimeTicks { get; set; }
    public string ExecutablePath { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string DataSource { get; set; } = "Nethogs";

    // Telemetry fields
    public long TodayDownload { get; set; }
    public long TodayUpload { get; set; }
    public long TodayTotal => TodayDownload + TodayUpload;

    public long SevenDayDownload { get; set; }
    public long SevenDayUpload { get; set; }
    public long SevenDayTotal => SevenDayDownload + SevenDayUpload;

    public long ThirtyDayDownload { get; set; }
    public long ThirtyDayUpload { get; set; }
    public long ThirtyDayTotal => ThirtyDayDownload + ThirtyDayUpload;

    public double PercentageOfTotalSystemUsage { get; set; }
    public double DownloadUploadRatio { get; set; } // download / (download + upload) ratio
    public double AverageDailyUsage { get; set; }
    public long PeakHourlyUsage { get; set; }

    public DateTime FirstObserved { get; set; }
    public DateTime LastObserved { get; set; }
    public int ObservedSessionsCount { get; set; }

    public string TrendState { get; set; } = "Insufficient Data"; // "Increasing", "Decreasing", "Stable", "Insufficient Data"
    public string AnomalyState { get; set; } = "Insufficient Data"; // "Normal", "Elevated", "Warning", "Critical", "Insufficient Data"
    public string DataSufficiencyState { get; set; } = "Insufficient Data"; // "Sufficient", "Insufficient Data", "Collecting Baseline"

    // Peak properties
    public int? PeakHour { get; set; }
    public string PeakDay { get; set; } = string.Empty;
    public string PeakUsagePeriod { get; set; } = string.Empty;
    public int? PeakDownloadHour { get; set; }
    public int? PeakUploadHour { get; set; }

    // Derived intelligence helpers
    public double DownloadPercentage => (TodayTotal > 0) ? ((double)TodayDownload / TodayTotal * 100.0) : 0;
    public double UploadPercentage => (TodayTotal > 0) ? ((double)TodayUpload / TodayTotal * 100.0) : 0;

    public string DirectionClassification
    {
        get
        {
            if (TodayTotal == 0) return "No Activity";
            double dlRatio = (double)TodayDownload / TodayTotal;
            if (dlRatio > 0.8) return "Download Heavy";
            if (dlRatio < 0.2) return "Upload Heavy";
            return "Balanced";
        }
    }

    public string DataShareClassification => PercentageOfTotalSystemUsage switch
    {
        >= 50.0 => "Dominant Consumer",
        >= 20.0 => "High Consumer",
        >= 5.0 => "Moderate Consumer",
        _ => "Low Consumer"
    };
}

public class ApplicationNetworkSnapshot
{
    public DateTime Timestamp { get; set; }
    public long DownloadBytes { get; set; }
    public long UploadBytes { get; set; }
    public long TotalBytes => DownloadBytes + UploadBytes;
}

public class ApplicationNetworkTrend
{
    public double? DownloadTrendPercentage { get; set; }
    public double? UploadTrendPercentage { get; set; }
    public double? TotalTrendPercentage { get; set; }
    public string TrendState { get; set; } = "Insufficient Data";
}

public class ApplicationNetworkEndpointSummary
{
    public string NetworkName { get; set; } = string.Empty;
    public string ConnectionType { get; set; } = string.Empty;
    public string InterfaceName { get; set; } = string.Empty;
    public long DownloadBytes { get; set; }
    public long UploadBytes { get; set; }
    public long TotalBytes => DownloadBytes + UploadBytes;
}
