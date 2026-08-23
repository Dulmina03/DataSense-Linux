using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

public enum AppAnalyticsPeriod
{
    Today,
    Yesterday,
    Last7Days,
    Last30Days,
    ThisMonth,
    AllTime
}

public interface IApplicationAnalyticsService
{
    // ── Existing methods (Phase 11.28/11.29) ─────────────────────────────────

    /// <summary>
    /// Returns aggregated summaries for all application identities visible in the given period.
    /// Results are cached for approximately 3 minutes unless forceRefresh is true.
    /// </summary>
    Task<IEnumerable<ApplicationAnalyticsSummary>> GetApplicationSummariesAsync(
        AppAnalyticsPeriod period, bool forceRefresh = false);

    /// <summary>
    /// Returns a detailed summary (including peak day/hour, active days) for one
    /// specific application identity (ProcessName + PID + StartTimeTicks).
    /// </summary>
    Task<ApplicationAnalyticsSummary?> GetProcessDetailAsync(
        string processName, int pid, long startTimeTicks, AppAnalyticsPeriod period);

    /// <summary>
    /// Returns daily or hourly timeline points for one application identity.
    /// Today → hourly buckets; other periods → daily buckets.
    /// Only buckets with real data are returned; gaps are not fabricated.
    /// </summary>
    Task<IEnumerable<ApplicationUsageTimelinePoint>> GetProcessTimelineAsync(
        string processName, int pid, long startTimeTicks, AppAnalyticsPeriod period);

    /// <summary>Invalidates all cached analytics data.</summary>
    Task InvalidateCacheAsync();

    // ── Phase 11.31A extensions ───────────────────────────────────────────────

    /// <summary>
    /// Returns a full historical intelligence profile for every application
    /// identity found in the database.  Profiles include period aggregates,
    /// trend analysis, peak detection, and activity status.
    /// Results are cached for approximately 3 minutes.
    /// </summary>
    Task<IEnumerable<ApplicationHistoricalProfile>> GetApplicationProfilesAsync(
        bool forceRefresh = false);

    /// <summary>
    /// Returns a historical intelligence profile for one application identity.
    /// Returns null if no records exist for this identity.
    /// </summary>
    Task<ApplicationHistoricalProfile?> GetApplicationProfileAsync(
        string processName, int pid, long startTimeTicks);

    /// <summary>
    /// Returns hourly usage data for an application over a given UTC date.
    /// Hours with no real records are omitted (not fabricated as zero).
    /// </summary>
    Task<ApplicationHourlyPattern> GetApplicationHourlyUsageAsync(
        string processName, int pid, long startTimeTicks, DateTime day);

    /// <summary>
    /// Returns daily usage points for an application between start and end.
    /// Only days with real records are returned.
    /// </summary>
    Task<IEnumerable<ApplicationUsagePoint>> GetApplicationDailyUsageAsync(
        string processName, int pid, long startTimeTicks, DateTime start, DateTime end);

    /// <summary>
    /// Returns a download/upload breakdown for an application within the given period.
    /// DownloadPercentage and UploadPercentage are null when TotalBytes == 0.
    /// </summary>
    Task<ApplicationTrafficBreakdown> GetApplicationTrafficBreakdownAsync(
        string processName, int pid, long startTimeTicks, AppAnalyticsPeriod period);

    /// <summary>
    /// Returns the top N application profiles ranked by total bytes descending.
    /// Secondary sort: ProcessName ascending (deterministic, stable).
    /// Only identities with valid telemetry are included.
    /// </summary>
    Task<IEnumerable<ApplicationHistoricalProfile>> GetTopApplicationsAsync(
        int limit = 10, bool byDownload = false, bool byUpload = false);

    /// <summary>
    /// Returns a standalone trend comparison (recent 7 days vs previous 7 days)
    /// for one application identity. Never divides by zero; returns
    /// TrendState = "Insufficient Data" when previous period has no records.
    /// </summary>
    Task<ApplicationTrendComparison> GetApplicationTrendAsync(
        string processName, int pid, long startTimeTicks);
}
