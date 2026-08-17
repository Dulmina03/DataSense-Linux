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
        var utcNow = DateTime.UtcNow;
        return period switch
        {
            AnalyticsPeriod.Today      => (utcNow.Date, utcNow.Date.AddDays(1).AddTicks(-1)),
            AnalyticsPeriod.Last7Days  => (utcNow.Date.AddDays(-6), utcNow.Date.AddDays(1).AddTicks(-1)),
            AnalyticsPeriod.Last30Days => (utcNow.Date.AddDays(-29), utcNow.Date.AddDays(1).AddTicks(-1)),
            AnalyticsPeriod.ThisMonth  => (new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc),
                                           utcNow.Date.AddDays(1).AddTicks(-1)),
            _                         => (utcNow.Date.AddDays(-6), utcNow.Date.AddDays(1).AddTicks(-1))
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
        var todayHourly = await GetTodayHourlyAsync();
        var peakHour = todayHourly.Count > 0 ? todayHourly.MaxBy(h => h.TotalBytes) : null;
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
        var hourly = await _repository.GetHourlyUsageAsync(DateTime.UtcNow.Date);
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
}
