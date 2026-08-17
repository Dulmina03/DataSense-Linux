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
    Task<IEnumerable<NetworkSession>> GetSessionsAsync(DateTime start, DateTime end, string? interfaceName = null);

    // Speed Tests
    Task SaveSpeedTestAsync(SpeedTestRecord record);
    Task<IEnumerable<SpeedTestRecord>> GetSpeedTestsAsync(int count = 50);

    // Process Analytics
    Task SaveProcessUsageAsync(ProcessUsageRecord record);
    
    /// <summary>Returns one aggregated DailyUsageRecord per clock-hour (UTC) for a single process.</summary>
    Task<IEnumerable<HourlyUsageRecord>> GetProcessHourlyUsageAsync(string processName, DateTime day);
    
    /// <summary>Returns one DailyUsageRecord per calendar day for a single process.</summary>
    Task<IEnumerable<DailyUsageRecord>> GetProcessDailyUsageAsync(string processName, DateTime start, DateTime end);
    
    /// <summary>Returns top N processes by usage within the given time range.</summary>
    Task<IEnumerable<ProcessUsageRecord>> GetTopProcessesAsync(DateTime start, DateTime end, int limit);
}
