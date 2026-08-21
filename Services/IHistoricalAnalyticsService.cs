using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

/// <summary>
/// Provides historical analytics for the Historical Explorer.
/// All data is derived from existing SQLite telemetry — no fabrication.
/// </summary>
public interface IHistoricalAnalyticsService
{
    /// <summary>
    /// Returns monthly usage summaries for the past <paramref name="monthCount"/> months,
    /// most recent first.
    /// </summary>
    Task<IList<MonthlyUsageSummary>> GetMonthlyOverviewAsync(int monthCount = 12);

    /// <summary>
    /// Returns daily breakdowns for a specific calendar month.
    /// </summary>
    Task<IList<DailyUsageRecord>> GetDailyBreakdownAsync(int year, int month, string? interfaceName = null);

    /// <summary>
    /// Returns hourly breakdowns for a specific calendar day (UTC).
    /// </summary>
    Task<IList<HourlyUsageRecord>> GetHourlyBreakdownAsync(DateTime day, string? interfaceName = null);

    /// <summary>
    /// Returns top applications by usage for a given period.
    /// </summary>
    Task<IList<HistoricalApplicationSummary>> GetTopApplicationsAsync(
        DateTime start, DateTime end, int limit = 10);

    /// <summary>
    /// Returns network sessions for a given period.
    /// </summary>
    Task<IList<NetworkSession>> GetSessionsAsync(
        DateTime start, DateTime end, string? interfaceName = null);

    /// <summary>
    /// Identifies usage spikes: days or hours whose usage deviates significantly
    /// from the rolling average (purely statistical, no ML).
    /// </summary>
    Task<IList<UsageSpikeRecord>> GetUsageSpikesAsync(DateTime start, DateTime end, int limit = 10);

    /// <summary>
    /// Compares two consecutive same-duration periods (e.g., this week vs last week).
    /// </summary>
    Task<PeriodComparisonResult> ComparePeriodAsync(DateTime start, DateTime end);

    /// <summary>
    /// Returns a complete drill-down result for any period and granularity.
    /// </summary>
    Task<HistoricalExplorerResult> GetExplorerResultAsync(
        DateTime start, DateTime end, HistoricalDrillLevel level, string? interfaceName = null);
}
