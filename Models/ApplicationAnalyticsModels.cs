using System;

namespace DataSense.Models;

public class ApplicationProcessIdentity
{
    public string ProcessName { get; set; } = string.Empty;
    public int Pid { get; set; }
    public DateTime StartTime { get; set; }
    
    public string CompositeKey => $"{ProcessName}_{Pid}_{StartTime.Ticks}";
}

public class ApplicationUsagePeriodSummary
{
    public long DownloadBytes { get; set; }
    public long UploadBytes { get; set; }
    public long TotalBytes => DownloadBytes + UploadBytes;
    public double DailyAverageBytes { get; set; }
}

public class ApplicationAnalyticsSummary
{
    public string ProcessName { get; set; } = string.Empty;
    public int Pid { get; set; }
    public DateTime StartTime { get; set; }
    public string ExecutablePath { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string DataSource { get; set; } = "Nethogs";
    
    public long DownloadBytes { get; set; }
    public long UploadBytes { get; set; }
    public long TotalBytes => DownloadBytes + UploadBytes;
    public double PercentageOfTotal { get; set; } // Share of all process traffic in the selected period
    
    public DateTime? FirstSeen { get; set; }
    public DateTime? LastSeen { get; set; }
    
    public bool IsCurrentlyRunning { get; set; }
    public bool HasHistoricalData { get; set; }
    
    // Period breakdown summaries (Today, 7 days, 30 days, Month)
    public ApplicationUsagePeriodSummary Today { get; set; } = new();
    public ApplicationUsagePeriodSummary Last7Days { get; set; } = new();
    public ApplicationUsagePeriodSummary Last30Days { get; set; } = new();
    public ApplicationUsagePeriodSummary ThisMonth { get; set; } = new();
    
    // Projected monthly usage (Month summary total * days in month / days elapsed)
    public long? ProjectedMonthlyBytes { get; set; }
    
    // Activity metrics
    public int ActiveDaysCount { get; set; }
    public int SamplesCount { get; set; }
    public DateTime? PeakUsageDay { get; set; }
    public long PeakUsageDayBytes { get; set; }
    public int? PeakUsageHour { get; set; } // 0-23
    public long PeakUsageHourBytes { get; set; }
    
    // Trends compared to previous 7 days (Latest 7 days vs previous 7 days)
    public string DownloadTrend { get; set; } = "Insufficient Data"; // "Increasing", "Decreasing", "Stable", "Insufficient Data"
    public string UploadTrend { get; set; } = "Insufficient Data";
    public string CombinedTrend { get; set; } = "Insufficient Data";
    
    public double? DownloadTrendPercentage { get; set; }
    public double? UploadTrendPercentage { get; set; }
    public double? CombinedTrendPercentage { get; set; }
}

public class ApplicationUsageTimelinePoint
{
    public DateTime Timestamp { get; set; }
    public long DownloadBytes { get; set; }
    public long UploadBytes { get; set; }
    public long TotalBytes => DownloadBytes + UploadBytes;
}
