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
}
