using System;
using System.Collections.Generic;

namespace DataSense.Models;

/// <summary>Drill-down granularity level for the Historical Explorer.</summary>
public enum HistoricalDrillLevel
{
    Year,
    Month,
    Day,
    Hour
}

/// <summary>Aggregate summary for a calendar month.</summary>
public class MonthlyUsageSummary
{
    public int  Year  { get; init; }
    public int  Month { get; init; }
    public long BytesDownloaded { get; init; }
    public long BytesUploaded   { get; init; }
    public long TotalBytes      => BytesDownloaded + BytesUploaded;
    public int  ActiveDays      { get; init; }
    public DailyUsageRecord? PeakDay { get; init; }
    public string MonthLabel    => new DateTime(Year, Month, 1).ToString("MMM yyyy");
}

/// <summary>Side-by-side comparison of two periods.</summary>
public class PeriodComparisonResult
{
    public string PeriodALabel { get; init; } = string.Empty;
    public string PeriodBLabel { get; init; } = string.Empty;

    public long PeriodADownloaded { get; init; }
    public long PeriodAUploaded   { get; init; }
    public long PeriodATotal      => PeriodADownloaded + PeriodAUploaded;

    public long PeriodBDownloaded { get; init; }
    public long PeriodBUploaded   { get; init; }
    public long PeriodBTotal      => PeriodBDownloaded + PeriodBUploaded;

    /// <summary>Positive = A used more, Negative = B used more. In percent.</summary>
    public double TotalChangePct  => PeriodBTotal > 0
        ? (PeriodATotal - PeriodBTotal) / (double)PeriodBTotal * 100.0
        : 0;

    public bool IsIncrease => TotalChangePct > 0;
}

/// <summary>An application's contribution to total usage in a historical period.</summary>
public class HistoricalApplicationSummary
{
    public string ProcessName    { get; init; } = string.Empty;
    public long   TotalBytes     { get; init; }
    public long   DownloadBytes  { get; init; }
    public long   UploadBytes    { get; init; }
    public double PercentOfTotal { get; init; }
}

/// <summary>A notable usage spike identified in history.</summary>
public class UsageSpikeRecord
{
    public DateTime Date        { get; init; }
    public long     TotalBytes  { get; init; }
    public long     DownloadBytes { get; init; }
    public long     UploadBytes   { get; init; }
    /// <summary>How much above the average this spike is, as a multiplier.</summary>
    public double   SpikeMultiplier { get; init; }
    public string   Description     { get; init; } = string.Empty;
}

/// <summary>Complete historical explorer result for a given drill-down state.</summary>
public class HistoricalExplorerResult
{
    public HistoricalDrillLevel DrillLevel { get; init; }
    public DateTime PeriodStart { get; init; }
    public DateTime PeriodEnd   { get; init; }
    public string   PeriodLabel { get; init; } = string.Empty;

    public long TotalDownloaded { get; init; }
    public long TotalUploaded   { get; init; }
    public long TotalBytes      => TotalDownloaded + TotalUploaded;

    public IList<DailyUsageRecord>        DailyBreakdown   { get; init; } = new List<DailyUsageRecord>();
    public IList<HourlyUsageRecord>       HourlyBreakdown  { get; init; } = new List<HourlyUsageRecord>();
    public IList<NetworkSession>          Sessions         { get; init; } = new List<NetworkSession>();
    public IList<HistoricalApplicationSummary> TopApps    { get; init; } = new List<HistoricalApplicationSummary>();
    public IList<UsageSpikeRecord>        Spikes           { get; init; } = new List<UsageSpikeRecord>();
    public PeriodComparisonResult?        Comparison       { get; init; }
}
