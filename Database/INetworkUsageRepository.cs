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
}
