using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

public enum AnalyticsPeriod
{
    Today,
    Last7Days,
    Last30Days,
    ThisMonth
}

/// <summary>Summary statistics for a given <see cref="AnalyticsPeriod"/>.</summary>
public class AnalyticsSummary
{
    public long TotalDownloaded { get; init; }
    public long TotalUploaded   { get; init; }
    public long TotalUsage      => TotalDownloaded + TotalUploaded;

    /// <summary>Average bytes per active day (days with data only).</summary>
    public long AvgDailyBytes { get; init; }

    /// <summary>Day with the highest combined usage in the period.</summary>
    public DailyUsageRecord? PeakDay { get; init; }

    /// <summary>Hour (0–23) with the highest combined usage today.</summary>
    public HourlyUsageRecord? PeakHourToday { get; init; }
}

public interface IAnalyticsService
{
    /// <summary>
    /// Computes a full analytics summary for the requested period.
    /// All DB queries run asynchronously; never blocks the UI thread.
    /// </summary>
    Task<AnalyticsSummary> GetSummaryAsync(AnalyticsPeriod period);

    /// <summary>Returns today's hourly usage (UTC hours, sparse — missing hours omitted).</summary>
    Task<IList<HourlyUsageRecord>> GetTodayHourlyAsync();

    /// <summary>Returns per-day usage for the selected period, chronological order.</summary>
    Task<IList<DailyUsageRecord>> GetDailySeriesAsync(AnalyticsPeriod period);
}
