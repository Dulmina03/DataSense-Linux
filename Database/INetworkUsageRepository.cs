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
}
