using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Database;
using DataSense.Models;

namespace DataSense.Services;

public class ApplicationAnalyticsService : IApplicationAnalyticsService
{
    private readonly INetworkUsageRepository _repository;
    private readonly ILinuxProcessResolver _processResolver;
    private readonly ConcurrentDictionary<AppAnalyticsPeriod, IEnumerable<ApplicationAnalyticsSummary>> _cache = new();

    public ApplicationAnalyticsService(
        INetworkUsageRepository repository,
        ILinuxProcessResolver processResolver)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _processResolver = processResolver ?? throw new ArgumentNullException(nameof(processResolver));
    }

    public async Task InvalidateCacheAsync()
    {
        _cache.Clear();
        await Task.CompletedTask;
    }

    private static (DateTime start, DateTime end) GetPeriodRange(AppAnalyticsPeriod period)
    {
        var utcNow = DateTime.UtcNow;
        var todayStart = utcNow.Date;
        var todayEnd = todayStart.AddDays(1).AddTicks(-1);

        return period switch
        {
            AppAnalyticsPeriod.Today      => (todayStart, todayEnd),
            AppAnalyticsPeriod.Yesterday  => (todayStart.AddDays(-1), todayStart.AddTicks(-1)),
            AppAnalyticsPeriod.Last7Days  => (todayStart.AddDays(-6), todayEnd),
            AppAnalyticsPeriod.Last30Days => (todayStart.AddDays(-29), todayEnd),
            AppAnalyticsPeriod.ThisMonth  => (new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc), todayEnd),
            AppAnalyticsPeriod.AllTime    => (new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), todayEnd),
            _                             => (todayStart.AddDays(-6), todayEnd)
        };
    }

    public async Task<IEnumerable<ApplicationAnalyticsSummary>> GetApplicationSummariesAsync(AppAnalyticsPeriod period, bool forceRefresh = false)
    {
        if (!forceRefresh && _cache.TryGetValue(period, out var cached))
        {
            return cached;
        }

        var (start, end) = GetPeriodRange(period);
        
        // Load target period data
        var targetData = (await _repository.GetProcessUsageIdentitiesAsync(start, end)).ToList();
        
        // Load comparison periods data
        var utcNow = DateTime.UtcNow;
        var todayStart = utcNow.Date;
        var todayEnd = todayStart.AddDays(1).AddTicks(-1);
        
        var prev7Start = todayStart.AddDays(-13);
        var prev7End = todayStart.AddDays(-6).AddTicks(-1);
        
        var todayData = (await _repository.GetProcessUsageIdentitiesAsync(todayStart, todayEnd)).ToList();
        var last7Data = (await _repository.GetProcessUsageIdentitiesAsync(todayStart.AddDays(-6), todayEnd)).ToList();
        var last30Data = (await _repository.GetProcessUsageIdentitiesAsync(todayStart.AddDays(-29), todayEnd)).ToList();
        var thisMonthStart = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var thisMonthData = (await _repository.GetProcessUsageIdentitiesAsync(thisMonthStart, todayEnd)).ToList();
        var prev7Data = (await _repository.GetProcessUsageIdentitiesAsync(prev7Start, prev7End)).ToList();

        // Calculate total process traffic in target period for percentage share
        long totalPeriodBytes = targetData.Sum(d => d.BytesDownloaded + d.BytesUploaded);
        if (totalPeriodBytes == 0) totalPeriodBytes = 1;

        var summaries = new List<ApplicationAnalyticsSummary>();

        foreach (var item in targetData)
        {
            var summary = new ApplicationAnalyticsSummary
            {
                ProcessName = item.ProcessName,
                Pid = item.Pid,
                StartTime = new DateTime(item.StartTimeTicks > 0 ? item.StartTimeTicks : utcNow.Ticks, DateTimeKind.Utc),
                ExecutablePath = item.ExecutablePath,
                UserName = item.UserName,
                DataSource = item.DataSource,
                DownloadBytes = item.BytesDownloaded,
                UploadBytes = item.BytesUploaded,
                PercentageOfTotal = ((double)(item.BytesDownloaded + item.BytesUploaded) / totalPeriodBytes) * 100.0,
                FirstSeen = item.FirstSeen,
                LastSeen = item.LastSeen,
                HasHistoricalData = true,
                IsCurrentlyRunning = DetermineProcessStatus(item.ProcessName, item.Pid, item.StartTimeTicks) == "Running"
            };

            // Today
            var todayMatch = todayData.FirstOrDefault(x => x.ProcessName == item.ProcessName && x.Pid == item.Pid && x.StartTimeTicks == item.StartTimeTicks);
            if (todayMatch != null)
            {
                summary.Today.DownloadBytes = todayMatch.BytesDownloaded;
                summary.Today.UploadBytes = todayMatch.BytesUploaded;
            }

            // Last 7 Days
            var last7Match = last7Data.FirstOrDefault(x => x.ProcessName == item.ProcessName && x.Pid == item.Pid && x.StartTimeTicks == item.StartTimeTicks);
            if (last7Match != null)
            {
                summary.Last7Days.DownloadBytes = last7Match.BytesDownloaded;
                summary.Last7Days.UploadBytes = last7Match.BytesUploaded;
            }

            // Last 30 Days
            var last30Match = last30Data.FirstOrDefault(x => x.ProcessName == item.ProcessName && x.Pid == item.Pid && x.StartTimeTicks == item.StartTimeTicks);
            if (last30Match != null)
            {
                summary.Last30Days.DownloadBytes = last30Match.BytesDownloaded;
                summary.Last30Days.UploadBytes = last30Match.BytesUploaded;
            }

            // This Month
            var thisMonthMatch = thisMonthData.FirstOrDefault(x => x.ProcessName == item.ProcessName && x.Pid == item.Pid && x.StartTimeTicks == item.StartTimeTicks);
            if (thisMonthMatch != null)
            {
                summary.ThisMonth.DownloadBytes = thisMonthMatch.BytesDownloaded;
                summary.ThisMonth.UploadBytes = thisMonthMatch.BytesUploaded;
            }

            // Projected monthly usage
            int daysInMonth = DateTime.DaysInMonth(utcNow.Year, utcNow.Month);
            double daysElapsed = utcNow.Day - 1 + (utcNow.TimeOfDay.TotalSeconds / 86400.0);
            if (daysElapsed < 0.1) daysElapsed = 0.1;
            summary.ProjectedMonthlyBytes = (long)(summary.ThisMonth.TotalBytes * daysInMonth / daysElapsed);

            // Trend calculation (Latest 7 days vs previous 7 days)
            var prev7Match = prev7Data.FirstOrDefault(x => x.ProcessName == item.ProcessName && x.Pid == item.Pid && x.StartTimeTicks == item.StartTimeTicks);
            long prev7Dl = prev7Match?.BytesDownloaded ?? 0;
            long prev7Ul = prev7Match?.BytesUploaded ?? 0;
            long prev7Total = prev7Dl + prev7Ul;

            long lat7Dl = summary.Last7Days.DownloadBytes;
            long lat7Ul = summary.Last7Days.UploadBytes;
            long lat7Total = summary.Last7Days.TotalBytes;

            summary.DownloadTrend = CalculateTrend(lat7Dl, prev7Dl, out double? dlPct);
            summary.DownloadTrendPercentage = dlPct;

            summary.UploadTrend = CalculateTrend(lat7Ul, prev7Ul, out double? ulPct);
            summary.UploadTrendPercentage = ulPct;

            summary.CombinedTrend = CalculateTrend(lat7Total, prev7Total, out double? combinedPct);
            summary.CombinedTrendPercentage = combinedPct;

            summaries.Add(summary);
        }

        _cache[period] = summaries;
        return summaries;
    }

    public async Task<ApplicationAnalyticsSummary?> GetProcessDetailAsync(string processName, int pid, long startTimeTicks, AppAnalyticsPeriod period)
    {
        var summaries = await GetApplicationSummariesAsync(period);
        var baseSummary = summaries.FirstOrDefault(x => x.ProcessName == processName && x.Pid == pid && x.StartTime.Ticks == startTimeTicks);

        if (baseSummary == null)
        {
            var allTimeSummaries = await GetApplicationSummariesAsync(AppAnalyticsPeriod.AllTime);
            baseSummary = allTimeSummaries.FirstOrDefault(x => x.ProcessName == processName && x.Pid == pid && x.StartTime.Ticks == startTimeTicks);
        }

        if (baseSummary == null) return null;

        var (start, end) = GetPeriodRange(period);
        string connectionString = (_repository as SqliteNetworkUsageRepository)?.ConnectionString ?? "Data Source=datasense.db";

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
        await connection.OpenAsync();

        // 1. Samples count (total records)
        int samplesCount = 0;
        const string sqlSamples = @"
            SELECT COUNT(*) FROM ProcessUsageRecords
            WHERE ProcessName = @ProcessName AND Pid = @Pid AND StartTimeTicks = @StartTimeTicks;";
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = sqlSamples;
            cmd.Parameters.AddWithValue("@ProcessName", processName);
            cmd.Parameters.AddWithValue("@Pid", pid);
            cmd.Parameters.AddWithValue("@StartTimeTicks", startTimeTicks);
            samplesCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        // 2. Active days count in the target period
        int activeDaysCount = 0;
        const string sqlActiveDays = @"
            SELECT COUNT(DISTINCT date(Timestamp)) FROM ProcessUsageRecords
            WHERE ProcessName = @ProcessName AND Pid = @Pid AND StartTimeTicks = @StartTimeTicks
              AND Timestamp >= @Start AND Timestamp <= @End AND (BytesDownloaded > 0 OR BytesUploaded > 0);";
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = sqlActiveDays;
            cmd.Parameters.AddWithValue("@ProcessName", processName);
            cmd.Parameters.AddWithValue("@Pid", pid);
            cmd.Parameters.AddWithValue("@StartTimeTicks", startTimeTicks);
            cmd.Parameters.AddWithValue("@Start", start.ToString("o"));
            cmd.Parameters.AddWithValue("@End", end.ToString("o"));
            activeDaysCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        // 3. Peak usage day
        DateTime? peakDay = null;
        long peakDayBytes = 0;
        const string sqlPeakDay = @"
            SELECT date(Timestamp) AS Day, SUM(BytesDownloaded + BytesUploaded) AS TotalUsage
            FROM ProcessUsageRecords
            WHERE ProcessName = @ProcessName AND Pid = @Pid AND StartTimeTicks = @StartTimeTicks
              AND Timestamp >= @Start AND Timestamp <= @End
            GROUP BY Day
            ORDER BY TotalUsage DESC LIMIT 1;";
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = sqlPeakDay;
            cmd.Parameters.AddWithValue("@ProcessName", processName);
            cmd.Parameters.AddWithValue("@Pid", pid);
            cmd.Parameters.AddWithValue("@StartTimeTicks", startTimeTicks);
            cmd.Parameters.AddWithValue("@Start", start.ToString("o"));
            cmd.Parameters.AddWithValue("@End", end.ToString("o"));
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                peakDay = DateTime.Parse(reader.GetString(0));
                peakDayBytes = reader.GetInt64(1);
            }
        }

        // 4. Peak usage hour
        int? peakHour = null;
        long peakHourBytes = 0;
        const string sqlPeakHour = @"
            SELECT CAST(strftime('%H', Timestamp) AS INTEGER) AS Hour, SUM(BytesDownloaded + BytesUploaded) AS TotalUsage
            FROM ProcessUsageRecords
            WHERE ProcessName = @ProcessName AND Pid = @Pid AND StartTimeTicks = @StartTimeTicks
              AND Timestamp >= @Start AND Timestamp <= @End
            GROUP BY Hour
            ORDER BY TotalUsage DESC LIMIT 1;";
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = sqlPeakHour;
            cmd.Parameters.AddWithValue("@ProcessName", processName);
            cmd.Parameters.AddWithValue("@Pid", pid);
            cmd.Parameters.AddWithValue("@StartTimeTicks", startTimeTicks);
            cmd.Parameters.AddWithValue("@Start", start.ToString("o"));
            cmd.Parameters.AddWithValue("@End", end.ToString("o"));
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                peakHour = reader.GetInt32(0);
                peakHourBytes = reader.GetInt64(1);
            }
        }

        baseSummary.SamplesCount = samplesCount;
        baseSummary.ActiveDaysCount = activeDaysCount;
        baseSummary.PeakUsageDay = peakDay;
        baseSummary.PeakUsageDayBytes = peakDayBytes;
        baseSummary.PeakUsageHour = peakHour;
        baseSummary.PeakUsageHourBytes = peakHourBytes;

        return baseSummary;
    }

    public async Task<IEnumerable<ApplicationUsageTimelinePoint>> GetProcessTimelineAsync(string processName, int pid, long startTimeTicks, AppAnalyticsPeriod period)
    {
        var (start, end) = GetPeriodRange(period);
        
        if (period == AppAnalyticsPeriod.Today)
        {
            var hourly = await _repository.GetProcessIdentityHourlyUsageAsync(processName, pid, startTimeTicks, DateTime.UtcNow);
            return hourly.Select(x => new ApplicationUsageTimelinePoint
            {
                Timestamp = DateTime.UtcNow.Date.AddHours(x.Hour),
                DownloadBytes = x.BytesDownloaded,
                UploadBytes = x.BytesUploaded
            });
        }
        else
        {
            var daily = await _repository.GetProcessIdentityDailyUsageAsync(processName, pid, startTimeTicks, start, end);
            return daily.Select(x => new ApplicationUsageTimelinePoint
            {
                Timestamp = x.Day,
                DownloadBytes = x.BytesDownloaded,
                UploadBytes = x.BytesUploaded
            });
        }
    }

    private static string CalculateTrend(long current, long previous, out double? percentageChange)
    {
        if (previous == 0)
        {
            percentageChange = null;
            return "Insufficient Data";
        }

        double pct = (double)(current - previous) / previous * 100.0;
        percentageChange = pct;

        if (pct > 10.0) return "Increasing";
        if (pct < -10.0) return "Decreasing";
        return "Stable";
    }

    private string DetermineProcessStatus(string processName, int pid, long startTimeTicks)
    {
        if (pid <= 0 || startTimeTicks <= 0) return "Exited";
        
        if (!Directory.Exists("/proc"))
        {
            return "Unavailable";
        }
        
        try
        {
            string processDir = $"/proc/{pid}";
            if (!Directory.Exists(processDir))
            {
                return "Exited";
            }
            
            var resolved = _processResolver.ResolveProcessIdentity(pid);
            if (resolved != null)
            {
                if (resolved.ProcessName == processName && resolved.StartTimeTicks == startTimeTicks)
                {
                    return "Running";
                }
                else
                {
                    return "Exited";
                }
            }
            return "Unknown";
        }
        catch (UnauthorizedAccessException)
        {
            return "Unavailable";
        }
        catch
        {
            return "Unknown";
        }
    }
}
