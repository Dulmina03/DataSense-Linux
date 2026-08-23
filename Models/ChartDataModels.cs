using System;

namespace DataSense.Models;

public class UsageChartItem
{
    public string Label { get; set; } = string.Empty;
    public long DownloadBytes { get; set; }
    public long UploadBytes { get; set; }
    public long TotalBytes { get; set; }
    public double Percentage { get; set; }
}

public class ProcessChartItem
{
    public string ProcessName { get; set; } = string.Empty;
    public long DownloadBytes { get; set; }
    public long UploadBytes { get; set; }
    public long TotalBytes { get; set; }
    public double Percentage { get; set; }
}

public class UsageTrendPoint
{
    public DateTime Timestamp { get; set; }
    public long DownloadBytes { get; set; }
    public long UploadBytes { get; set; }
    public long TotalBytes { get; set; }
}
