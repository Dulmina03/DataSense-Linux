using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using DataSense.Services;

namespace DataSense.Models;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 11.31A — Application Historical Intelligence Models
// All values must originate from real SQLite telemetry.
// Nullable properties represent genuinely unavailable data.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Trend direction classification for a single traffic dimension.
/// Calculated deterministically from recent vs previous 7-day windows.
/// </summary>
public enum AppTrendDirection
{
    Increasing,         // > +10 %
    Decreasing,         // < -10 %
    Stable,             // within ±10 %
    InsufficientData    // previous period has no usable records
}

/// <summary>
/// Current activity status of an application, derived from the most recent
/// telemetry sample timestamp relative to the observation window.
/// </summary>
public enum AppActivityStatus
{
    Active,             // telemetry within the last 30 s
    RecentlyActive,     // telemetry within the last 5 min
    Historical,         // telemetry exists but older than 5 min
    Unavailable         // no telemetry identity available
}

/// <summary>
/// A single data point in a time-series chart for an application.
/// Used for both daily and hourly chart rendering.
/// </summary>
public class ApplicationUsagePoint
{
    /// <summary>Bucket timestamp: calendar day (for daily) or hour-start (for hourly).</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Real bytes downloaded in this bucket, summed from ProcessUsageRecords.</summary>
    public long DownloadBytes { get; set; }

    /// <summary>Real bytes uploaded in this bucket, summed from ProcessUsageRecords.</summary>
    public long UploadBytes { get; set; }

    public long TotalBytes => DownloadBytes + UploadBytes;

    /// <summary>
    /// Percentage share of this bucket's usage relative to the total usage
    /// across all buckets in the same time window. Null when total is zero.
    /// </summary>
    public double? SharePercentage { get; set; }
}

/// <summary>
/// 24-hour bucket aggregation for an application.
/// Hour 0 = midnight UTC, Hour 23 = 11 PM UTC.
/// Only populated buckets (with real telemetry) are included;
/// missing hours are omitted rather than fabricated.
/// </summary>
public class ApplicationHourlyPattern
{
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>24-element array indexed [0..23]; null element = no telemetry for that hour.</summary>
    public long?[] HourlyDownloadBytes { get; set; } = new long?[24];

    /// <summary>24-element array indexed [0..23]; null element = no telemetry for that hour.</summary>
    public long?[] HourlyUploadBytes { get; set; } = new long?[24];

    /// <summary>UTC hour (0-23) with the highest combined usage. Null if no data.</summary>
    public int? PeakHour { get; set; }

    /// <summary>Combined bytes in the peak hour. Zero if no data.</summary>
    public long PeakHourBytes { get; set; }

    /// <summary>True if at least 1 hour bucket has real data.</summary>
    public bool HasData { get; set; }
}

/// <summary>
/// Download vs upload breakdown for an application over a selected period.
/// </summary>
public class ApplicationTrafficBreakdown
{
    public string ProcessName { get; set; } = string.Empty;

    public long DownloadBytes { get; set; }
    public long UploadBytes { get; set; }
    public long TotalBytes => DownloadBytes + UploadBytes;

    /// <summary>
    /// Download as a percentage of total. Null when TotalBytes == 0
    /// (do not fabricate a 50/50 split).
    /// </summary>
    public double? DownloadPercentage { get; set; }

    /// <summary>
    /// Upload as a percentage of total. Null when TotalBytes == 0.
    /// </summary>
    public double? UploadPercentage { get; set; }

    /// <summary>Formatted ratio text (e.g. "82% down / 18% up"). Empty if no data.</summary>
    public string FormattedRatio => TotalBytes > 0 && DownloadPercentage.HasValue && UploadPercentage.HasValue
        ? $"{DownloadPercentage:F0}% ↓ / {UploadPercentage:F0}% ↑"
        : string.Empty;

    /// <summary>True when DownloadBytes and UploadBytes can be distinguished.</summary>
    public bool CanDistinguishDirections => DownloadBytes >= 0 && UploadBytes >= 0 && TotalBytes > 0;
}

/// <summary>
/// Deterministic trend analysis comparing recent vs previous 7-day windows.
/// trend% = ((recent - previous) / previous) * 100
/// Direction thresholds: >+10% = Increasing, <-10% = Decreasing, else Stable.
/// Never calculates a trend when previous period has no real data.
/// </summary>
public class ApplicationTrend
{
    public string ProcessName { get; set; } = string.Empty;

    public AppTrendDirection DownloadTrend { get; set; } = AppTrendDirection.InsufficientData;
    public AppTrendDirection UploadTrend { get; set; } = AppTrendDirection.InsufficientData;
    public AppTrendDirection CombinedTrend { get; set; } = AppTrendDirection.InsufficientData;

    /// <summary>Null when previous period has no data (division by zero avoided).</summary>
    public double? DownloadTrendPercentage { get; set; }

    /// <summary>Null when previous period has no data.</summary>
    public double? UploadTrendPercentage { get; set; }

    /// <summary>Null when previous period has no data.</summary>
    public double? CombinedTrendPercentage { get; set; }

    /// <summary>Latest 7-day total bytes (all directions).</summary>
    public long Recent7DayBytes { get; set; }

    /// <summary>Previous 7-day total bytes (all directions). Zero = no prior data.</summary>
    public long Previous7DayBytes { get; set; }
}

/// <summary>
/// Full historical intelligence profile for a single application identity
/// (ProcessName + PID + StartTimeTicks).  All aggregate values are derived
/// from real ProcessUsageRecords rows; no fabrication or estimation.
/// Implements ObservableObject so in-place telemetry updates avoid recreating UI rows.
/// </summary>
public class ApplicationHistoricalProfile : ObservableObject
{
    // ── Identity ──────────────────────────────────────────────────────────────

    private string _processName = string.Empty;
    public string ProcessName { get => _processName; set => SetProperty(ref _processName, value); }

    /// <summary>PID at time of capture; 0 = not available.</summary>
    public int Pid { get; set; }

    /// <summary>Process start-time ticks for PID-recycling safety; 0 = not available.</summary>
    public long StartTimeTicks { get; set; }

    /// <summary>Full path from /proc; empty string = not available.</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>Linux username; empty string = not available.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Monitoring backend (e.g. "Nethogs"); empty = not available.</summary>
    public string DataSource { get; set; } = string.Empty;

    /// <summary>Resolved human-friendly display name (e.g. "Brave Web Browser").</summary>
    private string _applicationDisplayName = string.Empty;
    public string ApplicationDisplayName
    {
        get => _applicationDisplayName;
        set
        {
            if (SetProperty(ref _applicationDisplayName, value))
            {
                OnPropertyChanged(nameof(EffectiveDisplayName));
            }
        }
    }

    /// <summary>Resolved application icon from Linux desktop theme or generic fallback.</summary>
    private Avalonia.Media.IImage? _applicationIcon;
    public Avalonia.Media.IImage? ApplicationIcon
    {
        get => _applicationIcon;
        set => SetProperty(ref _applicationIcon, value);
    }

    /// <summary>Visual index in current chart display (0 to 11) for deterministic multi-color rendering.</summary>
    private int _displayIndex = -1;
    public int DisplayIndex
    {
        get => _displayIndex;
        set => SetProperty(ref _displayIndex, value);
    }

    /// <summary>Effective display name for UI rendering.</summary>
    public string EffectiveDisplayName =>
        !string.IsNullOrWhiteSpace(ApplicationDisplayName) ? ApplicationDisplayName : ProcessName;

    /// <summary>Formatted total bytes string.</summary>
    public string FormattedTotalText => DataSense.Helpers.ByteFormatter.FormatBytes(TotalBytes);

    /// <summary>Formatted download bytes string.</summary>
    public string FormattedDownloadText => DataSense.Helpers.ByteFormatter.FormatBytes(DownloadBytes);

    /// <summary>Formatted upload bytes string.</summary>
    public string FormattedUploadText => DataSense.Helpers.ByteFormatter.FormatBytes(UploadBytes);

    /// <summary>Rich tooltip summary formatted with actual telemetry metrics.</summary>
    public string TooltipSummary =>
        $"{EffectiveDisplayName}\n\n" +
        $"Download: {FormattedDownloadText}\n" +
        $"Upload:   {FormattedUploadText}\n" +
        $"Total:    {FormattedTotalText}\n" +
        $"Share:    {PercentageOfTotal:F1}%";

    // ── Period Aggregates ─────────────────────────────────────────────────────

    /// <summary>Bytes today (UTC calendar day). 0 if no today records.</summary>
    public long TodayBytes { get; set; }

    /// <summary>Bytes yesterday (UTC calendar day). 0 if no yesterday records.</summary>
    public long YesterdayBytes { get; set; }

    /// <summary>Bytes in last 7 days (rolling). 0 if no records.</summary>
    public long SevenDayTotalBytes { get; set; }

    /// <summary>
    /// Daily average over the days that actually had telemetry in the last 7 days.
    /// Null if fewer than 1 active day (do not divide by zero or fabricate).
    /// </summary>
    public double? SevenDayAverageBytes { get; set; }

    /// <summary>Bytes in last 30 days (rolling). 0 if no records.</summary>
    public long ThirtyDayTotalBytes { get; set; }

    /// <summary>
    /// Daily average over active days in last 30 days.
    /// Null if fewer than 1 active day.
    /// </summary>
    public double? ThirtyDayAverageBytes { get; set; }

    /// <summary>
    /// Projected current-month total (this-month-so-far / elapsed-days * days-in-month).
    /// Null when insufficient month data exists (fewer than 1 day elapsed).
    /// </summary>
    public long? MonthlyProjectedBytes { get; set; }

    // ── Download / Upload Split ───────────────────────────────────────────────

    /// <summary>Total download bytes across the selected profile window (AllTime).</summary>
    private long _downloadBytes;
    public long DownloadBytes
    {
        get => _downloadBytes;
        set
        {
            if (SetProperty(ref _downloadBytes, value))
            {
                OnPropertyChanged(nameof(TotalBytes));
                OnPropertyChanged(nameof(FormattedTotalText));
                OnPropertyChanged(nameof(FormattedDownloadText));
            }
        }
    }

    /// <summary>Total upload bytes across the selected profile window (AllTime).</summary>
    private long _uploadBytes;
    public long UploadBytes
    {
        get => _uploadBytes;
        set
        {
            if (SetProperty(ref _uploadBytes, value))
            {
                OnPropertyChanged(nameof(TotalBytes));
                OnPropertyChanged(nameof(FormattedTotalText));
                OnPropertyChanged(nameof(FormattedUploadText));
            }
        }
    }

    public long TotalBytes => DownloadBytes + UploadBytes;

    private double _percentageOfTotal;
    public double PercentageOfTotal
    {
        get => _percentageOfTotal;
        set => SetProperty(ref _percentageOfTotal, value);
    }

    private double _relativeUsagePercent;
    public double RelativeUsagePercent
    {
        get => _relativeUsagePercent;
        set => SetProperty(ref _relativeUsagePercent, value);
    }

    public void UpdateFrom(ApplicationHistoricalProfile other)
    {
        DownloadBytes = other.DownloadBytes;
        UploadBytes = other.UploadBytes;
        PercentageOfTotal = other.PercentageOfTotal;
        RelativeUsagePercent = other.RelativeUsagePercent;
        DisplayIndex = other.DisplayIndex;
        TodayBytes = other.TodayBytes;
        YesterdayBytes = other.YesterdayBytes;
        SevenDayTotalBytes = other.SevenDayTotalBytes;
        ThirtyDayTotalBytes = other.ThirtyDayTotalBytes;
        if (!string.IsNullOrWhiteSpace(other.ApplicationDisplayName) && string.IsNullOrWhiteSpace(ApplicationDisplayName))
        {
            ApplicationDisplayName = other.ApplicationDisplayName;
        }
        if (other.ApplicationIcon != null && ApplicationIcon == null)
        {
            ApplicationIcon = other.ApplicationIcon;
        }
    }

    // ── Trend ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Combined-traffic trend percentage (recent7 vs prev7).
    /// Null when previous period has no data.
    /// </summary>
    public double? TrendPercentage { get; set; }

    /// <summary>Human-readable trend label derived from TrendPercentage thresholds.</summary>
    public string TrendState { get; set; } = "Insufficient Data";

    public bool IsIncreasing => TrendState == "Increasing";

    // ── Activity ──────────────────────────────────────────────────────────────

    /// <summary>True when a live monitoring sample for this identity was received within 30 s.</summary>
    public bool IsCurrentlyActive { get; set; }

    /// <summary>Activity status with finer granularity for UI display.</summary>
    public AppActivityStatus ActivityStatus { get; set; } = AppActivityStatus.Unavailable;

    // ── Data Quality ──────────────────────────────────────────────────────────

    /// <summary>
    /// True when at least 3 distinct calendar days of telemetry exist.
    /// Averages, trends, and projections must not be computed when false.
    /// </summary>
    public bool HasSufficientData { get; set; }

    public DateTime? FirstSeen { get; set; }
    public DateTime? LastSeen { get; set; }

    /// <summary>Number of distinct UTC calendar days with at least one record.</summary>
    public int ActiveDays { get; set; }

    // ── Peak Detection ───────────────────────────────────────────────────────

    /// <summary>UTC hour (0-23) with the highest combined usage. Null if no data.</summary>
    public int? PeakHour { get; set; }

    /// <summary>Combined bytes in the peak hour.</summary>
    public long PeakHourBytes { get; set; }

    /// <summary>UTC calendar day with the highest combined usage. Null if no data.</summary>
    public DateTime? PeakDay { get; set; }

    /// <summary>Combined bytes on the peak day.</summary>
    public long PeakDayBytes { get; set; }

    // ── Surge Detection ───────────────────────────────────────────────────────

    /// <summary>
    /// True when recent7DayAverage > previous7DayAverage × 1.5
    /// AND HasSufficientData is true AND previous period has real data.
    /// Never set when previous period has no records.
    /// </summary>
    public bool IsUsageSurging { get; set; }

    /// <summary>
    /// ((recent7DayAverage - previous7DayAverage) / previous7DayAverage) × 100.
    /// Null when previous period has no usable data (no division by zero).
    /// </summary>
    public double? SurgePercentage { get; set; }

    // ── Health ────────────────────────────────────────────────────────────────

    /// <summary>Health state of the analytics service at the time this profile was built.</summary>
    public SubsystemState AnalyticsHealth { get; set; } = SubsystemState.Healthy;
}

/// <summary>
/// Standalone trend comparison result for a single application identity.
/// Returned by GetApplicationTrendAsync so callers do not need to parse the full profile.
/// </summary>
public class ApplicationTrendComparison
{
    public string ProcessName { get; set; } = string.Empty;
    public int Pid { get; set; }
    public long StartTimeTicks { get; set; }

    /// <summary>Recent 7-day total bytes.</summary>
    public long RecentPeriodBytes { get; set; }

    /// <summary>Previous 7-day total bytes. 0 = no prior data.</summary>
    public long PreviousPeriodBytes { get; set; }

    /// <summary>
    /// ((recent - previous) / previous) × 100.
    /// Null when previous == 0 (never divides by zero).
    /// </summary>
    public double? PercentageChange { get; set; }

    /// <summary>"Increasing", "Decreasing", "Stable", or "Insufficient Data".</summary>
    public string TrendState { get; set; } = "Insufficient Data";

    /// <summary>True when previous period has real data and calculation is valid.</summary>
    public bool HasSufficientData { get; set; }

    public AppTrendDirection TrendDirection { get; set; } = AppTrendDirection.InsufficientData;
}
