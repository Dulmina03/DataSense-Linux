using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Database;

public interface INetworkUsageRepository
{
    Task<(IEnumerable<NetworkUsageRecord> Records, int TotalCount)> GetHistoryPagedAsync(DateTime start, DateTime end, string? interfaceName, int pageIndex, int pageSize);
    Task InitializeAsync();
    Task SaveUsageAsync(NetworkUsage usage);
    Task<IEnumerable<NetworkUsageRecord>> GetHistoryAsync(DateTime start, DateTime end, string? interfaceName = null);
    Task PurgeOldRecordsAsync(TimeSpan retentionPeriod);

    /// <summary>
    /// Returns one <see cref="DailyUsageRecord"/> per calendar day in [start, end].
    /// Usage is computed as MAX – MIN of the cumulative byte counters so that
    /// summing individual telemetry rows is never required.
    /// </summary>
    Task<IEnumerable<DailyUsageRecord>> GetDailyUsageAsync(DateTime start, DateTime end, string? interfaceName = null);

    /// <summary>Returns the distinct interface names stored in the database.</summary>
    Task<IEnumerable<string>> GetInterfaceNamesAsync();

    /// <summary>
    /// Returns today's total downloaded and uploaded bytes using the MAX–MIN
    /// cumulative-counter approach for the current UTC calendar day.
    /// Negative deltas (counter resets) are clamped to 0.
    /// Returns (0, 0) when no records exist for today.
    /// </summary>
    Task<(long BytesDownloaded, long BytesUploaded)> GetTodaySummaryAsync(string? interfaceName = null);

    /// <summary>
    /// Returns the current UTC calendar month's total downloaded and uploaded bytes
    /// by summing each day's MAX–MIN cumulative-counter deltas.
    /// Returns (0, 0) when no records exist for the current month.
    /// </summary>
    Task<(long BytesDownloaded, long BytesUploaded)> GetMonthSummaryAsync(string? interfaceName = null);

    /// <summary>
    /// Returns one <see cref="HourlyUsageRecord"/> per clock-hour (UTC) for a given calendar day.
    /// Usage is computed as MAX – MIN of cumulative byte counters, clamped to 0.
    /// </summary>
    Task<IEnumerable<HourlyUsageRecord>> GetHourlyUsageAsync(DateTime day, string? interfaceName = null);

    // Network Sessions
    Task SaveSessionAsync(NetworkSession session);
    Task UpdateSessionAsync(NetworkSession session);
    Task<IEnumerable<NetworkSession>> GetSessionsAsync(DateTime start, DateTime end, string? interfaceName = null, string? networkName = null);
    Task<(long BytesDownloaded, long BytesUploaded)> GetSessionsSummaryAsync(DateTime start, DateTime end, string? interfaceName = null);

    // Speed Tests
    Task SaveSpeedTestAsync(SpeedTestRecord record);
    Task<IEnumerable<SpeedTestRecord>> GetSpeedTestsAsync(int count = 50, string? networkName = null);

    // Process Analytics
    Task SaveProcessUsageAsync(ProcessUsageRecord record);
    Task SaveProcessUsageBatchAsync(IEnumerable<ProcessUsageRecord> records);
    
    /// <summary>Returns one aggregated DailyUsageRecord per clock-hour (UTC) for a single process.</summary>
    Task<IEnumerable<HourlyUsageRecord>> GetProcessHourlyUsageAsync(string processName, DateTime day);
    
    /// <summary>Returns one DailyUsageRecord per calendar day for a single process.</summary>
    Task<IEnumerable<DailyUsageRecord>> GetProcessDailyUsageAsync(string processName, DateTime start, DateTime end);

    /// <summary>Returns one HourlyUsageRecord per clock-hour (UTC) aggregated across all processes.</summary>
    Task<IEnumerable<HourlyUsageRecord>> GetAllProcessesHourlyUsageAsync(DateTime day);

    /// <summary>Returns one DailyUsageRecord per calendar day aggregated across all processes.</summary>
    Task<IEnumerable<DailyUsageRecord>> GetAllProcessesDailyUsageAsync(DateTime start, DateTime end);
    
    /// <summary>Returns top N processes by usage within the given time range.</summary>
    Task<IEnumerable<ProcessUsageRecord>> GetTopProcessesAsync(DateTime start, DateTime end, int limit);

    Task<IEnumerable<ProcessUsageRecord>> GetProcessUsageIdentitiesAsync(DateTime start, DateTime end);
    Task<IEnumerable<HourlyUsageRecord>> GetProcessIdentityHourlyUsageAsync(string processName, int pid, long startTimeTicks, DateTime day);
    Task<IEnumerable<DailyUsageRecord>> GetProcessIdentityDailyUsageAsync(string processName, int pid, long startTimeTicks, DateTime start, DateTime end);

    // Network Analytics
    Task<IEnumerable<string>> GetAvailableNetworksAsync();
    Task<NetworkAnalyticsSummary> GetNetworkSummaryAsync(string networkName, DateTime start, DateTime end);
    Task<IEnumerable<DailyUsageRecord>> GetNetworkDailyUsageAsync(string networkName, DateTime start, DateTime end);
    Task<IEnumerable<HourlyUsageRecord>> GetNetworkHourlyUsageAsync(string networkName, DateTime day);
    Task<NetworkPerformanceSummary?> GetNetworkPerformanceAsync(string networkName);
    Task<IEnumerable<NetworkComparisonRecord>> GetNetworkComparisonAsync();

    // App Settings (key-value store for persisting user preferences)
    Task<string?> GetSettingAsync(string key);
    Task SaveSettingAsync(string key, string value);

    // Storage Management & Maintenance
    Task<int> GetTotalRecordCountAsync();
    Task ClearAllHistoryAsync();
}
