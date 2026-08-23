using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Database;
using DataSense.Models;

namespace DataSense.Services;

// Extends ApplicationAnalyticsService with Phase 11.31A profile/ranking/hourly methods.
// Cache uses DateTimeOffset expiry (3 min TTL) to prevent unbounded growth.

public partial class ApplicationAnalyticsService
{
    // TTL-bounded profile cache
    private readonly record struct CacheEntry<T>(T Value, DateTimeOffset ExpiresAt);
    private CacheEntry<IReadOnlyList<ApplicationHistoricalProfile>>? _profileCache;
    private readonly TimeSpan _profileCacheTtl = TimeSpan.FromMinutes(3);
    private readonly object _profileCacheLock = new();

    public async Task<IEnumerable<ApplicationHistoricalProfile>> GetApplicationProfilesAsync(bool forceRefresh = false)
    {
        lock (_profileCacheLock)
        {
            if (!forceRefresh && _profileCache.HasValue && _profileCache.Value.ExpiresAt > DateTimeOffset.UtcNow)
                return _profileCache.Value.Value;
        }

        var utcNow = DateTime.UtcNow;
        var todayStart = utcNow.Date;
        var todayEnd = todayStart.AddDays(1).AddTicks(-1);
        var yestStart = todayStart.AddDays(-1);
        var yestEnd = todayStart.AddTicks(-1);
        var w7Start = todayStart.AddDays(-6);
        var w30Start = todayStart.AddDays(-29);
        var monthStart = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var prev7Start = todayStart.AddDays(-13);
        var prev7End = todayStart.AddDays(-7).AddTicks(-1);

        // One pass per window — no per-process DB calls
        var allTimeData = (await _repository.GetProcessUsageIdentitiesAsync(
            new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), todayEnd)).ToList();
        var todayData   = (await _repository.GetProcessUsageIdentitiesAsync(todayStart, todayEnd)).ToList();
        var yestData    = (await _repository.GetProcessUsageIdentitiesAsync(yestStart, yestEnd)).ToList();
        var w7Data      = (await _repository.GetProcessUsageIdentitiesAsync(w7Start, todayEnd)).ToList();
        var w30Data     = (await _repository.GetProcessUsageIdentitiesAsync(w30Start, todayEnd)).ToList();
        var monthData   = (await _repository.GetProcessUsageIdentitiesAsync(monthStart, todayEnd)).ToList();
        var prev7Data   = (await _repository.GetProcessUsageIdentitiesAsync(prev7Start, prev7End)).ToList();

        long totalAllTime = allTimeData.Sum(d => d.BytesDownloaded + d.BytesUploaded);
        if (totalAllTime == 0) totalAllTime = 1;

        var profiles = new List<ApplicationHistoricalProfile>();

        foreach (var item in allTimeData)
        {
            static ProcessUsageRecord? Match(List<ProcessUsageRecord> src, ProcessUsageRecord item) =>
                src.FirstOrDefault(x => x.ProcessName == item.ProcessName && x.Pid == item.Pid && x.StartTimeTicks == item.StartTimeTicks);

            var today   = Match(todayData, item);
            var yest    = Match(yestData, item);
            var w7      = Match(w7Data, item);
            var w30     = Match(w30Data, item);
            var month   = Match(monthData, item);
            var prev7   = Match(prev7Data, item);

            long todayBytes = (today?.BytesDownloaded ?? 0) + (today?.BytesUploaded ?? 0);
            long yestBytes  = (yest?.BytesDownloaded ?? 0)  + (yest?.BytesUploaded ?? 0);
            long w7Total    = (w7?.BytesDownloaded ?? 0)    + (w7?.BytesUploaded ?? 0);
            long w30Total   = (w30?.BytesDownloaded ?? 0)   + (w30?.BytesUploaded ?? 0);
            long prev7Total = (prev7?.BytesDownloaded ?? 0) + (prev7?.BytesUploaded ?? 0);
            long monthTotal = (month?.BytesDownloaded ?? 0) + (month?.BytesUploaded ?? 0);

            // Active days (from AllTime identity record — separate query needed for precision,
            // but the identity rows already expose FirstSeen/LastSeen; use w30 active-day count
            // via daily query for accuracy).
            var daily30 = (await _repository.GetProcessIdentityDailyUsageAsync(
                item.ProcessName, item.Pid, item.StartTimeTicks, w30Start, todayEnd)).ToList();
            int activeDays = daily30.Count(d => d.BytesDownloaded + d.BytesUploaded > 0);

            // Peak day & hour from AllTime window
            DateTime? peakDay = null; long peakDayBytes = 0;
            int? peakHour = null; long peakHourBytes = 0;
            var dailyAll = (await _repository.GetProcessIdentityDailyUsageAsync(
                item.ProcessName, item.Pid, item.StartTimeTicks,
                new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), todayEnd)).ToList();
            if (dailyAll.Count > 0)
            {
                var pd = dailyAll.MaxBy(d => d.BytesDownloaded + d.BytesUploaded)!;
                peakDay = pd.Day; peakDayBytes = pd.BytesDownloaded + pd.BytesUploaded;
            }
            var hourlyToday = (await _repository.GetProcessIdentityHourlyUsageAsync(
                item.ProcessName, item.Pid, item.StartTimeTicks, utcNow)).ToList();
            if (hourlyToday.Count > 0)
            {
                var ph = hourlyToday.MaxBy(h => h.BytesDownloaded + h.BytesUploaded)!;
                peakHour = ph.Hour; peakHourBytes = ph.BytesDownloaded + ph.BytesUploaded;
            }

            // Trend
            string trendState = "Insufficient Data";
            double? trendPct = null;
            if (prev7Total > 0)
            {
                double pct = (double)(w7Total - prev7Total) / prev7Total * 100.0;
                trendPct = pct;
                trendState = pct > 10.0 ? "Increasing" : pct < -10.0 ? "Decreasing" : "Stable";
            }

            // Projections — only when at least 1 day elapsed
            double daysElapsed = (utcNow - monthStart).TotalDays;
            long? projectedMonthly = daysElapsed >= 1.0
                ? (long)(monthTotal / daysElapsed * DateTime.DaysInMonth(utcNow.Year, utcNow.Month))
                : null;

            bool hasSufficient = activeDays >= 3;

            var profile = new ApplicationHistoricalProfile
            {
                ProcessName       = item.ProcessName,
                Pid               = item.Pid,
                StartTimeTicks    = item.StartTimeTicks,
                ExecutablePath    = item.ExecutablePath,
                UserName          = item.UserName,
                DataSource        = item.DataSource,
                TodayBytes        = todayBytes,
                YesterdayBytes    = yestBytes,
                SevenDayTotalBytes  = w7Total,
                SevenDayAverageBytes  = activeDays > 0 ? (double?)w7Total / Math.Min(activeDays, 7) : null,
                ThirtyDayTotalBytes = w30Total,
                ThirtyDayAverageBytes = activeDays > 0 ? (double?)w30Total / Math.Min(activeDays, 30) : null,
                MonthlyProjectedBytes = projectedMonthly,
                DownloadBytes     = item.BytesDownloaded,
                UploadBytes       = item.BytesUploaded,
                PercentageOfTotal = (double)(item.BytesDownloaded + item.BytesUploaded) / totalAllTime * 100.0,
                TrendPercentage   = trendPct,
                TrendState        = trendState,
                HasSufficientData = hasSufficient,
                FirstSeen         = item.FirstSeen,
                LastSeen          = item.LastSeen,
                ActiveDays        = activeDays,
                PeakHour          = peakHour,
                PeakHourBytes     = peakHourBytes,
                PeakDay           = peakDay,
                PeakDayBytes      = peakDayBytes,
                ActivityStatus    = AppActivityStatus.Historical,
                IsCurrentlyActive = false
            };
            profiles.Add(profile);
        }

        var result = profiles.AsReadOnly();

        // Enrich with surge detection using the prev7 window data fetched above
        EnrichWithSurgeDetection(profiles, prev7Data, SubsystemState.Healthy);

        lock (_profileCacheLock)
        {
            _profileCache = new CacheEntry<IReadOnlyList<ApplicationHistoricalProfile>>(
                result, DateTimeOffset.UtcNow.Add(_profileCacheTtl));
        }

        // Publish events for notable conditions (fire-and-forget; never throws)
        PublishAnalyticsEvents(result);

        return result;
    }

    public async Task<ApplicationHistoricalProfile?> GetApplicationProfileAsync(
        string processName, int pid, long startTimeTicks)
    {
        var profiles = await GetApplicationProfilesAsync();
        return profiles.FirstOrDefault(p =>
            p.ProcessName == processName && p.Pid == pid && p.StartTimeTicks == startTimeTicks);
    }

    public async Task<ApplicationHourlyPattern> GetApplicationHourlyUsageAsync(
        string processName, int pid, long startTimeTicks, DateTime day)
    {
        var pattern = new ApplicationHourlyPattern { ProcessName = processName };
        var hourly = (await _repository.GetProcessIdentityHourlyUsageAsync(
            processName, pid, startTimeTicks, day)).ToList();

        foreach (var h in hourly)
        {
            if (h.Hour < 0 || h.Hour > 23) continue;
            pattern.HourlyDownloadBytes[h.Hour] = h.BytesDownloaded;
            pattern.HourlyUploadBytes[h.Hour]   = h.BytesUploaded;
            long total = h.BytesDownloaded + h.BytesUploaded;
            if (total > pattern.PeakHourBytes)
            {
                pattern.PeakHour      = h.Hour;
                pattern.PeakHourBytes = total;
            }
        }
        pattern.HasData = hourly.Count > 0;
        return pattern;
    }

    public async Task<IEnumerable<ApplicationUsagePoint>> GetApplicationDailyUsageAsync(
        string processName, int pid, long startTimeTicks, DateTime start, DateTime end)
    {
        var daily = await _repository.GetProcessIdentityDailyUsageAsync(
            processName, pid, startTimeTicks, start, end);
        var points = daily.Select(d => new ApplicationUsagePoint
        {
            Timestamp     = d.Day,
            DownloadBytes = d.BytesDownloaded,
            UploadBytes   = d.BytesUploaded
        }).ToList();

        // Compute share percentages within the returned set
        long total = points.Sum(p => p.TotalBytes);
        if (total > 0)
            foreach (var p in points)
                p.SharePercentage = (double)p.TotalBytes / total * 100.0;

        return points;
    }

    public async Task<ApplicationTrafficBreakdown> GetApplicationTrafficBreakdownAsync(
        string processName, int pid, long startTimeTicks, AppAnalyticsPeriod period)
    {
        var (start, end) = GetPeriodRange(period);
        var data = (await _repository.GetProcessUsageIdentitiesAsync(start, end))
            .FirstOrDefault(x => x.ProcessName == processName && x.Pid == pid && x.StartTimeTicks == startTimeTicks);

        long dl = data?.BytesDownloaded ?? 0;
        long ul = data?.BytesUploaded   ?? 0;
        long total = dl + ul;

        return new ApplicationTrafficBreakdown
        {
            ProcessName       = processName,
            DownloadBytes     = dl,
            UploadBytes       = ul,
            DownloadPercentage = total > 0 ? (double)dl / total * 100.0 : null,
            UploadPercentage   = total > 0 ? (double)ul / total * 100.0 : null
        };
    }

    public async Task<IEnumerable<ApplicationHistoricalProfile>> GetTopApplicationsAsync(
        int limit = 10, bool byDownload = false, bool byUpload = false)
    {
        var profiles = await GetApplicationProfilesAsync();
        IEnumerable<ApplicationHistoricalProfile> ordered = byDownload
            ? profiles.OrderByDescending(p => p.DownloadBytes).ThenBy(p => p.ProcessName)
            : byUpload
                ? profiles.OrderByDescending(p => p.UploadBytes).ThenBy(p => p.ProcessName)
                : profiles.OrderByDescending(p => p.TotalBytes).ThenBy(p => p.ProcessName);
        return ordered.Take(limit);
    }
}
