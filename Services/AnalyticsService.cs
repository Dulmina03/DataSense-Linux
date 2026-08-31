using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Database;
using DataSense.Models;

namespace DataSense.Services;

public class AnalyticsService : IAnalyticsService
{
    private readonly INetworkUsageRepository _repository;

    public AnalyticsService(INetworkUsageRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    // ── Date range helpers ───────────────────────────────────────────────────

    private static (DateTime start, DateTime end) GetRange(AnalyticsPeriod period)
    {
        var localNow = DateTime.Now;
        return period switch
        {
            AnalyticsPeriod.Today      => Helpers.DateRangeHelper.GetLocalTodayRange(),
            AnalyticsPeriod.Last7Days  => (Helpers.DateRangeHelper.GetLocalDayRange(localNow.Date.AddDays(-6)).startUtc, Helpers.DateRangeHelper.GetLocalTodayRange().endUtc),
            AnalyticsPeriod.Last30Days => (Helpers.DateRangeHelper.GetLocalDayRange(localNow.Date.AddDays(-29)).startUtc, Helpers.DateRangeHelper.GetLocalTodayRange().endUtc),
            AnalyticsPeriod.ThisMonth  => Helpers.DateRangeHelper.GetLocalMonthRange(localNow.Year, localNow.Month),
            AnalyticsPeriod.AllTime    => (new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), Helpers.DateRangeHelper.GetLocalTodayRange().endUtc),
            _                         => Helpers.DateRangeHelper.GetLocalTodayRange()
        };
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public async Task<AnalyticsSummary> GetSummaryAsync(AnalyticsPeriod period)
    {
        var (start, end) = GetRange(period);
        var daily = (await _repository.GetDailyUsageAsync(start, end)).ToList();

        long totalDl = daily.Sum(d => d.BytesDownloaded);
        long totalUl = daily.Sum(d => d.BytesUploaded);

        var activeDays = daily.Where(d => d.TotalBytes > 0).ToList();
        long avgDaily = activeDays.Count > 0 ? (long)activeDays.Average(d => d.TotalBytes) : 0;
        var peakDay = activeDays.Count > 0 ? activeDays.MaxBy(d => d.TotalBytes) : null;
        var hourly = await GetTodayHourlyAsync();
        var peakHour = hourly.Count > 0 ? hourly.MaxBy(h => h.TotalBytes) : null;
        return new AnalyticsSummary
        {
            TotalDownloaded = totalDl,
            TotalUploaded   = totalUl,
            AvgDailyBytes   = avgDaily,
            PeakDay         = peakDay,
            PeakHourToday   = peakHour,
        };
    }

    public async Task<IList<HourlyUsageRecord>> GetTodayHourlyAsync()
    {
        var hourly = await _repository.GetHourlyUsageAsync(DateTime.Today);
        return hourly.ToList();
    }

    public async Task<IList<DailyUsageRecord>> GetDailySeriesAsync(AnalyticsPeriod period)
    {
        var (start, end) = GetRange(period);
        var raw = (await _repository.GetDailyUsageAsync(start, end)).ToList();
        raw.Reverse(); // chronological order for UI
        return raw;
    }

    // ── Process analytics ─────────────────────────────────────────────────────

    public async Task<ProcessAnalyticsSummary> GetProcessSummaryAsync(string processName, AnalyticsPeriod period)
    {
        var (start, end) = GetRange(period);
        var daily = (await _repository.GetProcessDailyUsageAsync(processName, start, end)).ToList();
        long totalDl = daily.Sum(d => d.BytesDownloaded);
        long totalUl = daily.Sum(d => d.BytesUploaded);
        var activeDays = daily.Where(d => d.TotalBytes > 0).ToList();
        DateTime? first = activeDays.Any() ? activeDays.Min(d => d.Day) : (DateTime?)null;
        DateTime? last  = activeDays.Any() ? activeDays.Max(d => d.Day) : (DateTime?)null;
        return new ProcessAnalyticsSummary
        {
            TotalDownloaded = totalDl,
            TotalUploaded   = totalUl,
            FirstActive    = first,
            LastActive     = last,
            DaysUsed       = activeDays.Count
        };
    }

    public async Task<IList<DailyUsageRecord>> GetProcessDailySeriesAsync(string processName, AnalyticsPeriod period)
    {
        var (start, end) = GetRange(period);
        var raw = (await _repository.GetProcessDailyUsageAsync(processName, start, end)).ToList();
        raw.Reverse(); // chronological order for UI
        return raw;
    }

    public async Task<IList<HourlyUsageRecord>> GetProcessTodayHourlyAsync(string processName)
    {
        var hourly = await _repository.GetProcessHourlyUsageAsync(processName, DateTime.UtcNow.Date);
        return hourly.ToList();
    }

    public async Task<IEnumerable<ProcessUsageRecord>> GetTopDataConsumersAsync(AnalyticsPeriod period, int limit)
    {
        var (start, end) = GetRange(period);
        var top = await _repository.GetTopProcessesAsync(start, end, limit);
        return top;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Network Analytics
    // ────────────────────────────────────────────────────────────────────────

    public async Task<IEnumerable<string>> GetAvailableNetworksAsync()
    {
        return await _repository.GetAvailableNetworksAsync();
    }

    public async Task<NetworkAnalyticsSummary> GetNetworkSummaryAsync(string networkName, AnalyticsPeriod period)
    {
        var (start, end) = GetRange(period);
        return await _repository.GetNetworkSummaryAsync(networkName, start, end);
    }

    public async Task<IList<DailyUsageRecord>> GetNetworkDailySeriesAsync(string networkName, AnalyticsPeriod period)
    {
        var (start, end) = GetRange(period);
        var raw = (await _repository.GetNetworkDailyUsageAsync(networkName, start, end)).ToList();
        raw.Reverse(); // chronological order for UI
        return raw;
    }

    public async Task<IList<HourlyUsageRecord>> GetNetworkTodayHourlyAsync(string networkName)
    {
        var hourly = await _repository.GetNetworkHourlyUsageAsync(networkName, DateTime.UtcNow.Date);
        return hourly.ToList();
    }

    public async Task<NetworkPerformanceSummary?> GetNetworkPerformanceAsync(string networkName)
    {
        return await _repository.GetNetworkPerformanceAsync(networkName);
    }

    public async Task<IEnumerable<NetworkComparisonRecord>> GetNetworkComparisonAsync()
    {
        return await _repository.GetNetworkComparisonAsync();
    }

    public async Task<IEnumerable<NetworkSession>> GetNetworkSessionsAsync(string networkName, AnalyticsPeriod period)
    {
        var (start, end) = GetRange(period);
        return await _repository.GetSessionsAsync(start, end, networkName: networkName);
    }
}
