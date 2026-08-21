using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

public interface IPatternAnalysisService
{
    /// <summary>Returns the historical pattern for a specific clock hour (0-23 UTC).</summary>
    Task<UsagePattern> GetHourlyPatternAsync(int hourUtc);

    /// <summary>Returns historical pattern points for all 24 clock hours.</summary>
    Task<IDictionary<int, UsagePatternPoint>> GetHourlyPatternsAsync();

    /// <summary>Returns the historical pattern for a specific day of the week.</summary>
    Task<UsagePattern> GetDayOfWeekPatternAsync(DayOfWeek dayOfWeek);

    /// <summary>Returns historical pattern points for all 7 days of the week.</summary>
    Task<IDictionary<DayOfWeek, UsagePatternPoint>> GetDayOfWeekPatternsAsync();

    /// <summary>Returns the historical daily usage pattern for a specific application.</summary>
    Task<UsagePattern> GetAppPatternAsync(string processName);

    /// <summary>Returns the historical daily usage pattern for a specific network.</summary>
    Task<UsagePattern> GetNetworkPatternAsync(string networkName);

    /// <summary>
    /// Performs statistical evaluation across current telemetry vs baselines
    /// to detect unusual activity (system, hour, app, network).
    /// </summary>
    Task<IEnumerable<UsageAnomaly>> DetectAnomaliesAsync();

    /// <summary>Returns summary text descriptions for busy hours and busy days of the week.</summary>
    Task<(string BusyHoursText, string BusyDaysText)> GetUsagePatternSummaryAsync();
}
