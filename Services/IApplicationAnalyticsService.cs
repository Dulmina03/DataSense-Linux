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
    Task<IEnumerable<ApplicationAnalyticsSummary>> GetApplicationSummariesAsync(AppAnalyticsPeriod period, bool forceRefresh = false);
    Task<ApplicationAnalyticsSummary?> GetProcessDetailAsync(string processName, int pid, long startTimeTicks, AppAnalyticsPeriod period);
    Task<IEnumerable<ApplicationUsageTimelinePoint>> GetProcessTimelineAsync(string processName, int pid, long startTimeTicks, AppAnalyticsPeriod period);
    Task InvalidateCacheAsync();
}
