using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Database;
using DataSense.Models;

namespace DataSense.Services;

public class PatternAnalysisService : IPatternAnalysisService
{
    private readonly INetworkUsageRepository _repository;
    private readonly IAnalyticsService _analyticsService;

    // Cache to prevent recalculating patterns on every single frame/timer pulse
    private readonly object _cacheLock = new();
    private DateTime _lastCacheTime = DateTime.MinValue;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

    private IDictionary<int, UsagePatternPoint>? _cachedHourlyPatterns;
    private IDictionary<DayOfWeek, UsagePatternPoint>? _cachedDayOfWeekPatterns;
    private List<UsageAnomaly>? _cachedAnomalies;

    public PatternAnalysisService(
        INetworkUsageRepository repository,
        IAnalyticsService analyticsService)
    {
        _repository       = repository       ?? throw new ArgumentNullException(nameof(repository));
        _analyticsService = analyticsService ?? throw new ArgumentNullException(nameof(analyticsService));
    }

    public async Task<UsagePattern> GetHourlyPatternAsync(int hourUtc)
    {
        var patterns = await GetHourlyPatternsAsync();
        return patterns.TryGetValue(hourUtc, out var point)
            ? point.Pattern
            : new UsagePattern();
    }

    public async Task<IDictionary<int, UsagePatternPoint>> GetHourlyPatternsAsync()
    {
        lock (_cacheLock)
        {
            if (_cachedHourlyPatterns != null && DateTime.UtcNow - _lastCacheTime < _cacheDuration)
                return _cachedHourlyPatterns;
        }

        var result = new Dictionary<int, UsagePatternPoint>();

        // Query historical 30 days of hourly usage
        var utcNow = DateTime.UtcNow;
        var start  = utcNow.Date.AddDays(-30);
        var end    = utcNow.Date.AddTicks(-1); // exclude today for baseline

        var history = await _repository.GetHistoryAsync(start, end);
        var dailyRecords = (await _repository.GetDailyUsageAsync(start, end)).ToList();

        // If fewer than 3 historical days exist, return insufficient data state
        if (dailyRecords.Count < 3)
        {
            for (int h = 0; h < 24; h++)
            {
                result[h] = new UsagePatternPoint
                {
                    Key     = h.ToString("D2"),
                    Label   = $"{h:D2}:00",
                    Pattern = new UsagePattern { SampleCount = dailyRecords.Count }
                };
            }
            return result;
        }

        // Group records by calendar day and hour, summing total bytes per hour per day
        var hourlyByDay = new Dictionary<int, List<double>>();
        for (int h = 0; h < 24; h++)
        {
            hourlyByDay[h] = new List<double>();
        }

        // Group history records by Day + Hour
        var grouped = history
            .GroupBy(r => new { r.Timestamp.Date, r.Timestamp.Hour })
            .Select(g => new
            {
                Hour = g.Key.Hour,
                Bytes = g.Sum(r => (long)(r.DownloadSpeed + r.UploadSpeed)) // approximations or delta sums
            });

        // Better: Query daily hourly series across past 30 days
        // Group by Date to get complete days
        var uniqueDays = dailyRecords.Select(d => d.Day.Date).Distinct().ToList();

        foreach (var day in uniqueDays)
        {
            var hourlyForDay = await _repository.GetHourlyUsageAsync(day);
            foreach (var hRecord in hourlyForDay)
            {
                if (hRecord.Hour >= 0 && hRecord.Hour < 24)
                {
                    hourlyByDay[hRecord.Hour].Add(hRecord.TotalBytes);
                }
            }
        }

        for (int h = 0; h < 24; h++)
        {
            var sample = hourlyByDay[h];
            result[h] = new UsagePatternPoint
            {
                Key     = h.ToString("D2"),
                Label   = $"{h:D2}:00",
                Pattern = ComputePattern(sample)
            };
        }

        lock (_cacheLock)
        {
            _cachedHourlyPatterns = result;
            _lastCacheTime        = DateTime.UtcNow;
        }

        return result;
    }

    public async Task<UsagePattern> GetDayOfWeekPatternAsync(DayOfWeek dayOfWeek)
    {
        var patterns = await GetDayOfWeekPatternsAsync();
        return patterns.TryGetValue(dayOfWeek, out var point)
            ? point.Pattern
            : new UsagePattern();
    }

    public async Task<IDictionary<DayOfWeek, UsagePatternPoint>> GetDayOfWeekPatternsAsync()
    {
        lock (_cacheLock)
        {
            if (_cachedDayOfWeekPatterns != null && DateTime.UtcNow - _lastCacheTime < _cacheDuration)
                return _cachedDayOfWeekPatterns;
        }

        var result = new Dictionary<DayOfWeek, UsagePatternPoint>();

        var utcNow = DateTime.UtcNow;
        var start  = utcNow.Date.AddDays(-60); // 60 days for good day-of-week coverage
        var end    = utcNow.Date.AddTicks(-1);

        var dailyRecords = (await _repository.GetDailyUsageAsync(start, end)).ToList();

        foreach (DayOfWeek dow in Enum.GetValues(typeof(DayOfWeek)))
        {
            var daySamples = dailyRecords
                .Where(d => d.Day.DayOfWeek == dow)
                .Select(d => (double)d.TotalBytes)
                .ToList();

            result[dow] = new UsagePatternPoint
            {
                Key     = dow.ToString(),
                Label   = dow.ToString(),
                Pattern = ComputePattern(daySamples)
            };
        }

        lock (_cacheLock)
        {
            _cachedDayOfWeekPatterns = result;
        }

        return result;
    }

    public async Task<UsagePattern> GetAppPatternAsync(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return new UsagePattern();

        var utcNow = DateTime.UtcNow;
        var start  = utcNow.Date.AddDays(-30);
        var end    = utcNow.Date.AddTicks(-1);

        var appDaily = await _repository.GetProcessDailyUsageAsync(processName, start, end);
        var samples  = appDaily.Select(d => (double)d.TotalBytes).ToList();

        return ComputePattern(samples);
    }

    public async Task<UsagePattern> GetNetworkPatternAsync(string networkName)
    {
        if (string.IsNullOrWhiteSpace(networkName)) return new UsagePattern();

        var utcNow = DateTime.UtcNow;
        var start  = utcNow.Date.AddDays(-30);
        var end    = utcNow.Date.AddTicks(-1);

        var netDaily = await _repository.GetNetworkDailyUsageAsync(networkName, start, end);
        var samples  = netDaily.Select(d => (double)d.TotalBytes).ToList();

        return ComputePattern(samples);
    }

    public async Task<IEnumerable<UsageAnomaly>> DetectAnomaliesAsync()
    {
        lock (_cacheLock)
        {
            if (_cachedAnomalies != null && DateTime.UtcNow - _lastCacheTime < _cacheDuration)
                return _cachedAnomalies;
        }

        var anomalies = new List<UsageAnomaly>();
        var utcNow    = DateTime.UtcNow;
        int curHour   = utcNow.Hour;

        // 1. Evaluate Current Hour Usage Anomaly
        var todayHourly = await _analyticsService.GetTodayHourlyAsync();
        var curHourRecord = todayHourly.FirstOrDefault(h => h.Hour == curHour);
        if (curHourRecord != null && curHourRecord.TotalBytes > 0)
        {
            var hourlyPattern = await GetHourlyPatternAsync(curHour);
            if (hourlyPattern.HasSufficientData && hourlyPattern.StandardDeviation > 0)
            {
                double zScore = (curHourRecord.TotalBytes - hourlyPattern.AverageBytes) / hourlyPattern.StandardDeviation;
                long delta    = curHourRecord.TotalBytes - (long)hourlyPattern.AverageBytes;

                // Anomaly threshold: Z > 2.0 and delta > 50MB
                if (zScore > 2.0 && delta > 50_000_000)
                {
                    var severity = zScore > 3.0 ? AnomalySeverity.Critical : AnomalySeverity.Warning;
                    double pct   = hourlyPattern.AverageBytes > 0
                        ? ((curHourRecord.TotalBytes - hourlyPattern.AverageBytes) / hourlyPattern.AverageBytes * 100)
                        : 100;

                    anomalies.Add(new UsageAnomaly
                    {
                        Target            = $"Hourly: {curHour:D2}:00",
                        AnomalyType       = "HourlySpike",
                        Severity          = severity,
                        CurrentValue      = curHourRecord.TotalBytes,
                        ExpectedAverage   = hourlyPattern.AverageBytes,
                        NormalRangeLower  = hourlyPattern.NormalRangeLower,
                        NormalRangeUpper  = hourlyPattern.NormalRangeUpper,
                        ZScore            = zScore,
                        Title             = $"Unusual Activity at {curHour:D2}:00",
                        Description       = $"Your usage of {FormatBytes(curHourRecord.TotalBytes)} between {curHour:D2}:00–{curHour+1:D2}:00 is {pct:F0}% above your normal {FormatBytes((long)hourlyPattern.AverageBytes)} at this time of day.",
                        Timestamp         = utcNow
                    });
                }
            }
        }

        // 2. Evaluate Day-of-Week Usage Anomaly
        var todaySummary = await _analyticsService.GetSummaryAsync(AnalyticsPeriod.Today);
        if (todaySummary.TotalUsage > 0)
        {
            var dowPattern = await GetDayOfWeekPatternAsync(utcNow.DayOfWeek);
            if (dowPattern.HasSufficientData && dowPattern.StandardDeviation > 0)
            {
                // Normalize by time of day passed
                double fractionOfDay = Math.Max(0.1, utcNow.TimeOfDay.TotalHours / 24.0);
                double expectedByNow = dowPattern.AverageBytes * fractionOfDay;
                double stdDevNorm    = dowPattern.StandardDeviation * Math.Sqrt(fractionOfDay);

                double zScore = (todaySummary.TotalUsage - expectedByNow) / (stdDevNorm > 0 ? stdDevNorm : 1.0);

                if (zScore > 2.0 && (todaySummary.TotalUsage - expectedByNow) > 100_000_000)
                {
                    double pct = expectedByNow > 0
                        ? ((todaySummary.TotalUsage - expectedByNow) / expectedByNow * 100)
                        : 100;

                    anomalies.Add(new UsageAnomaly
                    {
                        Target            = $"DayOfWeek: {utcNow.DayOfWeek}",
                        AnomalyType       = "DayOfWeekSpike",
                        Severity          = zScore > 3.0 ? AnomalySeverity.Critical : AnomalySeverity.Warning,
                        CurrentValue      = todaySummary.TotalUsage,
                        ExpectedAverage   = dowPattern.AverageBytes,
                        NormalRangeLower  = dowPattern.NormalRangeLower,
                        NormalRangeUpper  = dowPattern.NormalRangeUpper,
                        ZScore            = zScore,
                        Title             = $"Unusual {utcNow.DayOfWeek} Usage",
                        Description       = $"Today's total usage of {FormatBytes(todaySummary.TotalUsage)} is {pct:F0}% above your expected pace for {utcNow.DayOfWeek}s.",
                        Timestamp         = utcNow
                    });
                }
            }
        }

        // 3. Evaluate Top Application Anomalies
        var topApps = await _analyticsService.GetTopDataConsumersAsync(AnalyticsPeriod.Today, 5);
        foreach (var app in topApps)
        {
            if (app.TotalBytes < 50_000_000) continue; // ignore trivial app traffic

            var appPattern = await GetAppPatternAsync(app.ProcessName);
            if (appPattern.HasSufficientData && appPattern.StandardDeviation > 0)
            {
                double zScore = (app.TotalBytes - appPattern.AverageBytes) / appPattern.StandardDeviation;

                if (zScore > 2.0 && app.TotalBytes > appPattern.AverageBytes * 1.8)
                {
                    double pct = appPattern.AverageBytes > 0
                        ? ((app.TotalBytes - appPattern.AverageBytes) / appPattern.AverageBytes * 100)
                        : 100;

                    anomalies.Add(new UsageAnomaly
                    {
                        Target            = $"Process: {app.ProcessName}",
                        AnomalyType       = "AppSpike",
                        Severity          = zScore > 3.0 ? AnomalySeverity.Critical : AnomalySeverity.Warning,
                        CurrentValue      = app.TotalBytes,
                        ExpectedAverage   = appPattern.AverageBytes,
                        NormalRangeLower  = appPattern.NormalRangeLower,
                        NormalRangeUpper  = appPattern.NormalRangeUpper,
                        ZScore            = zScore,
                        Title             = $"Unusual Application Behavior — {app.ProcessName}",
                        Description       = $"{app.ProcessName} consumed {FormatBytes(app.TotalBytes)} today, which is {pct:F0}% above its typical daily average of {FormatBytes((long)appPattern.AverageBytes)}.",
                        Timestamp         = utcNow
                    });
                }
            }
        }

        lock (_cacheLock)
        {
            _cachedAnomalies = anomalies;
        }

        return anomalies;
    }

    public async Task<(string BusyHoursText, string BusyDaysText)> GetUsagePatternSummaryAsync()
    {
        var hourly = await GetHourlyPatternsAsync();
        var dow    = await GetDayOfWeekPatternsAsync();

        // Check if sufficient data exists
        bool hasHourlyData = hourly.Values.Any(p => p.Pattern.HasSufficientData);
        bool hasDowData    = dow.Values.Any(p => p.Pattern.HasSufficientData);

        if (!hasHourlyData || !hasDowData)
        {
            return (
                "Not enough historical data to identify reliable hourly patterns.",
                "Not enough historical data to identify reliable day-of-week patterns."
            );
        }

        // Top 3 busiest hours
        var topHours = hourly.Values
            .Where(p => p.Pattern.HasSufficientData)
            .OrderByDescending(p => p.Pattern.AverageBytes)
            .Take(3)
            .Select(p => $"{p.Label}")
            .ToList();

        string busyHoursText = topHours.Count > 0
            ? $"Normally busiest around {string.Join(", ", topHours)}"
            : "Hourly usage is evenly distributed";

        // Top busiest days
        var topDays = dow.Values
            .Where(p => p.Pattern.HasSufficientData)
            .OrderByDescending(p => p.Pattern.AverageBytes)
            .Take(2)
            .Select(p => p.Label)
            .ToList();

        string busyDaysText = topDays.Count > 0
            ? $"Peak usage typically occurs on {string.Join(" and ", topDays)}s"
            : "Daily usage is evenly distributed";

        return (busyHoursText, busyDaysText);
    }

    // ── Helper methods ───────────────────────────────────────────────────────

    private static UsagePattern ComputePattern(IList<double> values)
    {
        if (values == null || values.Count < 3)
        {
            return new UsagePattern
            {
                SampleCount = values?.Count ?? 0
            };
        }

        double count = values.Count;
        double avg   = values.Average();

        var sorted   = values.OrderBy(v => v).ToList();
        double median = count % 2 == 0
            ? (sorted[(int)count / 2 - 1] + sorted[(int)count / 2]) / 2.0
            : sorted[(int)count / 2];

        double sumSquares = values.Sum(v => Math.Pow(v - avg, 2));
        double stdDev     = Math.Sqrt(sumSquares / count);

        return new UsagePattern
        {
            AverageBytes      = avg,
            MedianBytes       = median,
            StandardDeviation = stdDev,
            MinimumBytes      = sorted.First(),
            MaximumBytes      = sorted.Last(),
            SampleCount       = values.Count
        };
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0) bytes = 0;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < units.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {units[order]}";
    }
}
