using System;

namespace DataSense.Models;

/// <summary>
/// Detailed application telemetry profile computed locally from historical database records.
/// </summary>
public class ApplicationUsageProfile
{
    public string ProcessName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    public double TodayBytes { get; set; }
    public double YesterdayBytes { get; set; }
    public double SevenDayAverageBytes { get; set; }
    public double ThirtyDayAverageBytes { get; set; }

    public double CurrentRateBytesPerSecond { get; set; }
    public double MonthlyProjectedBytes { get; set; }

    public double PercentageOfTotalUsage { get; set; }
    public double TrendPercentage { get; set; }

    public bool IsIncreasing { get; set; }
    public bool IsAnomalous { get; set; }
    public bool HasSufficientData { get; set; } = true;

    public string FormattedToday => FormatBytes((long)TodayBytes);
    public string FormattedSevenDayAvg => FormatBytes((long)SevenDayAverageBytes);
    public string FormattedMonthlyProjected => FormatBytes((long)MonthlyProjectedBytes);
    public string FormattedTrend => TrendPercentage >= 0 ? $"+{TrendPercentage:F0}%" : $"{TrendPercentage:F0}%";

    public string TrendColor => TrendPercentage switch
    {
        > 50  => "#FF5252",
        > 15  => "#FF9800",
        < -15 => "#00E676",
        _     => "#888899"
    };

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0) bytes = 0;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < units.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {units[order]}";
    }
}
