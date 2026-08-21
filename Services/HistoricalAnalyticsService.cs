using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Database;
using DataSense.Models;

namespace DataSense.Services;

/// <summary>
/// Implements <see cref="IHistoricalAnalyticsService"/> using existing SQLite telemetry.
/// All calculations are deterministic and local. No data fabrication.
/// </summary>
public class HistoricalAnalyticsService : IHistoricalAnalyticsService
{
    private readonly INetworkUsageRepository _repository;

    public HistoricalAnalyticsService(INetworkUsageRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Monthly Overview
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<IList<MonthlyUsageSummary>> GetMonthlyOverviewAsync(int monthCount = 12)
    {
        var result = new List<MonthlyUsageSummary>(monthCount);
        var now    = DateTime.UtcNow;

        for (int i = 0; i < monthCount; i++)
        {
            var targetMonth = now.AddMonths(-i);
            var start       = new DateTime(targetMonth.Year, targetMonth.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var end         = start.AddMonths(1).AddTicks(-1);

            var daily = (await _repository.GetDailyUsageAsync(start, end)).ToList();
            if (!daily.Any())
            {
                result.Add(new MonthlyUsageSummary
                {
                    Year  = targetMonth.Year,
                    Month = targetMonth.Month
                });
                continue;
            }

            var activeDays  = daily.Where(d => d.TotalBytes > 0).ToList();
            var peakDay     = activeDays.OrderByDescending(d => d.TotalBytes).FirstOrDefault();

            result.Add(new MonthlyUsageSummary
            {
                Year            = targetMonth.Year,
                Month           = targetMonth.Month,
                BytesDownloaded = daily.Sum(d => d.BytesDownloaded),
                BytesUploaded   = daily.Sum(d => d.BytesUploaded),
                ActiveDays      = activeDays.Count,
                PeakDay         = peakDay
            });
        }

        return result;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Daily Breakdown
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<IList<DailyUsageRecord>> GetDailyBreakdownAsync(
        int year, int month, string? interfaceName = null)
    {
        var start = new DateTime(year, month, 1,  0, 0, 0, DateTimeKind.Utc);
        var end   = start.AddMonths(1).AddTicks(-1);
        var rows  = await _repository.GetDailyUsageAsync(start, end, interfaceName);
        return rows.OrderBy(d => d.Day).ToList();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Hourly Breakdown
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<IList<HourlyUsageRecord>> GetHourlyBreakdownAsync(
        DateTime day, string? interfaceName = null)
    {
        var rows = await _repository.GetHourlyUsageAsync(day.Date, interfaceName);
        return rows.OrderBy(h => h.Hour).ToList();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Top Applications
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<IList<HistoricalApplicationSummary>> GetTopApplicationsAsync(
        DateTime start, DateTime end, int limit = 10)
    {
        var records = (await _repository.GetTopProcessesAsync(start, end, limit)).ToList();
        if (!records.Any()) return new List<HistoricalApplicationSummary>();

        long grandTotal = records.Sum(r => r.BytesDownloaded + r.BytesUploaded);

        return records
            .Select(r => new HistoricalApplicationSummary
            {
                ProcessName    = r.ProcessName,
                DownloadBytes  = r.BytesDownloaded,
                UploadBytes    = r.BytesUploaded,
                TotalBytes     = r.BytesDownloaded + r.BytesUploaded,
                PercentOfTotal = grandTotal > 0
                    ? (r.BytesDownloaded + r.BytesUploaded) / (double)grandTotal * 100.0
                    : 0
            })
            .OrderByDescending(a => a.TotalBytes)
            .ToList();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Sessions
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<IList<NetworkSession>> GetSessionsAsync(
        DateTime start, DateTime end, string? interfaceName = null)
    {
        var sessions = await _repository.GetSessionsAsync(start, end, interfaceName);
        return sessions.OrderByDescending(s => s.StartTime).ToList();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Spike Detection (statistical, no ML)
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<IList<UsageSpikeRecord>> GetUsageSpikesAsync(
        DateTime start, DateTime end, int limit = 10)
    {
        var daily = (await _repository.GetDailyUsageAsync(start, end)).ToList();
        var activeDays = daily.Where(d => d.TotalBytes > 0).ToList();
        if (activeDays.Count < 3) return new List<UsageSpikeRecord>();

        double avg    = activeDays.Average(d => (double)d.TotalBytes);
        double stdDev = Math.Sqrt(activeDays.Average(d => Math.Pow(d.TotalBytes - avg, 2)));
        double threshold = avg + (stdDev > 0 ? stdDev * 1.5 : avg * 0.5);

        return activeDays
            .Where(d => d.TotalBytes > threshold)
            .OrderByDescending(d => d.TotalBytes)
            .Take(limit)
            .Select(d =>
            {
                double multiplier = avg > 0 ? d.TotalBytes / avg : 1;
                return new UsageSpikeRecord
                {
                    Date             = d.Day,
                    TotalBytes       = d.TotalBytes,
                    DownloadBytes    = d.BytesDownloaded,
                    UploadBytes      = d.BytesUploaded,
                    SpikeMultiplier  = multiplier,
                    Description      = $"{d.Day:MMM d} — {multiplier:F1}× above average"
                };
            })
            .ToList();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Period Comparison
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<PeriodComparisonResult> ComparePeriodAsync(DateTime start, DateTime end)
    {
        var duration     = end - start;
        var prevEnd      = start.AddTicks(-1);
        var prevStart    = prevEnd - duration;

        var currentTask  = _repository.GetDailyUsageAsync(start, end);
        var previousTask = _repository.GetDailyUsageAsync(prevStart, prevEnd);

        await Task.WhenAll(currentTask, previousTask);

        var current  = currentTask.Result.ToList();
        var previous = previousTask.Result.ToList();

        return new PeriodComparisonResult
        {
            PeriodALabel      = FormatPeriodLabel(start, end),
            PeriodBLabel      = FormatPeriodLabel(prevStart, prevEnd),
            PeriodADownloaded = current.Sum(d  => d.BytesDownloaded),
            PeriodAUploaded   = current.Sum(d  => d.BytesUploaded),
            PeriodBDownloaded = previous.Sum(d => d.BytesDownloaded),
            PeriodBUploaded   = previous.Sum(d => d.BytesUploaded)
        };
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Full Explorer Result
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<HistoricalExplorerResult> GetExplorerResultAsync(
        DateTime start, DateTime end, HistoricalDrillLevel level, string? interfaceName = null)
    {
        // Kick off all queries in parallel
        var dailyTask      = _repository.GetDailyUsageAsync(start, end, interfaceName);
        var sessionsTask   = _repository.GetSessionsAsync(start, end, interfaceName);
        var appsTask       = GetTopApplicationsAsync(start, end, 10);
        var spikesTask     = GetUsageSpikesAsync(start, end, 5);
        var compareTask    = ComparePeriodAsync(start, end);

        Task<IList<HourlyUsageRecord>>? hourlyTask = null;
        if (level == HistoricalDrillLevel.Day || level == HistoricalDrillLevel.Hour)
            hourlyTask = GetHourlyBreakdownAsync(start, interfaceName);

        await Task.WhenAll(
            dailyTask, sessionsTask, appsTask, spikesTask, compareTask,
            hourlyTask ?? Task.CompletedTask);

        var daily = dailyTask.Result.ToList();

        return new HistoricalExplorerResult
        {
            DrillLevel      = level,
            PeriodStart     = start,
            PeriodEnd       = end,
            PeriodLabel     = FormatPeriodLabel(start, end),
            TotalDownloaded = daily.Sum(d => d.BytesDownloaded),
            TotalUploaded   = daily.Sum(d => d.BytesUploaded),
            DailyBreakdown  = daily,
            HourlyBreakdown = hourlyTask?.Result ?? new List<HourlyUsageRecord>(),
            Sessions        = sessionsTask.Result.OrderByDescending(s => s.StartTime).ToList(),
            TopApps         = appsTask.Result,
            Spikes          = spikesTask.Result,
            Comparison      = compareTask.Result
        };
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static string FormatPeriodLabel(DateTime start, DateTime end)
    {
        var span = end - start;
        if (span.TotalDays <= 1)
            return start.ToString("MMM d, yyyy");
        if (start.Month == end.Month && start.Year == end.Year)
            return start.ToString("MMM yyyy");
        return $"{start:MMM d} – {end:MMM d, yyyy}";
    }
}
