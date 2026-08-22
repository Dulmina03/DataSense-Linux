using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Database;
using DataSense.Models;

namespace DataSense.Services;

public class ApplicationIntelligenceService : IApplicationIntelligenceService
{
    private readonly INetworkUsageRepository _repository;
    private readonly IAnalyticsService _analyticsService;
    private readonly IPatternAnalysisService _patternAnalysisService;

    public ApplicationIntelligenceService(
        INetworkUsageRepository repository,
        IAnalyticsService analyticsService,
        IPatternAnalysisService patternAnalysisService)
    {
        _repository             = repository             ?? throw new ArgumentNullException(nameof(repository));
        _analyticsService       = analyticsService       ?? throw new ArgumentNullException(nameof(analyticsService));
        _patternAnalysisService = patternAnalysisService ?? throw new ArgumentNullException(nameof(patternAnalysisService));
    }

    public async Task<ApplicationUsageProfile?> GetApplicationProfileAsync(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return null;

        var utcNow = DateTime.UtcNow;
        var todayStart = utcNow.Date;
        var yesterdayStart = todayStart.AddDays(-1);

        // Fetch process daily history for past 30 days
        var dailyRecords = (await _repository.GetProcessDailyUsageAsync(processName, todayStart.AddDays(-30), utcNow)).ToList();
        
        long todayBytes = dailyRecords.FirstOrDefault(r => r.Day.Date == todayStart)?.TotalBytes ?? 0;
        long yesterdayBytes = dailyRecords.FirstOrDefault(r => r.Day.Date == yesterdayStart)?.TotalBytes ?? 0;

        var past7Days = dailyRecords.Where(r => r.Day.Date < todayStart && r.Day.Date >= todayStart.AddDays(-7)).ToList();
        double avg7Days = past7Days.Count > 0 ? past7Days.Average(r => (double)r.TotalBytes) : todayBytes;

        var past30Days = dailyRecords.Where(r => r.Day.Date < todayStart).ToList();
        double avg30Days = past30Days.Count > 0 ? past30Days.Average(r => (double)r.TotalBytes) : todayBytes;

        double trendPct = 0;
        if (avg7Days > 0)
        {
            trendPct = ((todayBytes - avg7Days) / avg7Days) * 100.0;
        }

        // Pattern evaluation
        var pattern = await _patternAnalysisService.GetAppPatternAsync(processName);

        // Overall summary to compute percentage
        var totalTodaySummary = await _analyticsService.GetSummaryAsync(AnalyticsPeriod.Today);
        double pctTotal = totalTodaySummary.TotalUsage > 0
            ? ((double)todayBytes / totalTodaySummary.TotalUsage) * 100.0
            : 0;

        double monthlyProjected = avg7Days > 0 ? avg7Days * 30.0 : todayBytes * 30.0;

        return new ApplicationUsageProfile
        {
            ProcessName              = processName,
            DisplayName              = GetFriendlyAppName(processName),
            TodayBytes               = todayBytes,
            YesterdayBytes           = yesterdayBytes,
            SevenDayAverageBytes     = avg7Days,
            ThirtyDayAverageBytes    = avg30Days,
            CurrentRateBytesPerSecond= 0,
            MonthlyProjectedBytes    = monthlyProjected,
            PercentageOfTotalUsage   = pctTotal,
            TrendPercentage          = trendPct,
            IsIncreasing             = trendPct > 15.0,
            IsAnomalous              = pattern.HasSufficientData && pattern.StandardDeviation > 0 && todayBytes > pattern.NormalRangeUpper,
            HasSufficientData        = dailyRecords.Count >= 3
        };
    }

    public async Task<IEnumerable<ApplicationUsageProfile>> GetTopApplicationProfilesAsync(AnalyticsPeriod period, int limit)
    {
        var topConsumers = (await _analyticsService.GetTopDataConsumersAsync(period, limit)).ToList();
        var periodSummary = await _analyticsService.GetSummaryAsync(period);
        double totalSystemBytes = periodSummary.TotalUsage > 0 ? periodSummary.TotalUsage : 1.0;

        var result = new List<ApplicationUsageProfile>();
        foreach (var consumer in topConsumers)
        {
            var profile = await GetApplicationProfileAsync(consumer.ProcessName);
            if (profile != null)
            {
                profile.PercentageOfTotalUsage = (consumer.TotalBytes / totalSystemBytes) * 100.0;
                result.Add(profile);
            }
        }
        return result;
    }

    public async Task<IEnumerable<ApplicationRecommendation>> GenerateApplicationRecommendationsAsync()
    {
        var recommendations = new List<ApplicationRecommendation>();
        var topApps = (await GetTopApplicationProfilesAsync(AnalyticsPeriod.Today, 8)).ToList();
        var anomalies = (await _patternAnalysisService.DetectAnomaliesAsync()).ToList();

        // Check overall data sufficiency
        bool anySufficient = topApps.Any(a => a.HasSufficientData);

        if (!anySufficient || topApps.Count == 0)
        {
            recommendations.Add(new ApplicationRecommendation
            {
                ProcessName            = "System",
                Title                  = "Establishing Application Baselines",
                Description            = "Not enough application history to generate a reliable recommendation.",
                ActionableStep         = "Continue using DataSense while per-process historical patterns are accumulated over time.",
                Impact                 = RecommendationImpact.Low,
                PotentialSavingsBytes  = 0,
                Timestamp              = DateTime.UtcNow
            });
            return recommendations;
        }

        foreach (var app in topApps)
        {
            // Rule 1: High Bandwidth Heavyweight (> 25% of today's total system bandwidth)
            if (app.PercentageOfTotalUsage >= 25.0 && app.TodayBytes > 100_000_000)
            {
                long potentialSavings = (long)(app.TodayBytes * 0.3); // 30% potential reduction
                recommendations.Add(new ApplicationRecommendation
                {
                    ProcessName           = app.ProcessName,
                    Title                 = $"High Bandwidth Consumer — {app.DisplayName}",
                    Description           = $"{app.DisplayName} accounts for {app.PercentageOfTotalUsage:F0}% of today's total network traffic ({FormatBytes((long)app.TodayBytes)}).",
                    ActionableStep        = $"Consider configuring background download limits, lowering video resolution, or closing unused tabs in {app.DisplayName}.",
                    Impact                = RecommendationImpact.High,
                    PotentialSavingsBytes = potentialSavings,
                    Timestamp             = DateTime.UtcNow
                });
            }

            // Rule 2: Rapid Usage Surge (> 50% increase over 7-day average)
            if (app.TrendPercentage > 50.0 && app.TodayBytes > 50_000_000 && app.HasSufficientData)
            {
                recommendations.Add(new ApplicationRecommendation
                {
                    ProcessName           = app.ProcessName,
                    Title                 = $"Rapid Bandwidth Surge — {app.DisplayName}",
                    Description           = $"{app.DisplayName}'s bandwidth consumption surged by {app.TrendPercentage:F0}% today compared to its 7-day average.",
                    ActionableStep        = $"Check if {app.DisplayName} is executing background updates, syncing cloud drives, or streaming media.",
                    Impact                = RecommendationImpact.Medium,
                    PotentialSavingsBytes = (long)(app.TodayBytes - app.SevenDayAverageBytes),
                    Timestamp             = DateTime.UtcNow
                });
            }

            // Rule 3: Process Statistical Anomaly
            var appAnomaly = anomalies.FirstOrDefault(a => a.Target.Contains(app.ProcessName, StringComparison.OrdinalIgnoreCase));
            if (appAnomaly != null)
            {
                recommendations.Add(new ApplicationRecommendation
                {
                    ProcessName           = app.ProcessName,
                    Title                 = $"Unusual Process Behavior — {app.DisplayName}",
                    Description           = appAnomaly.Description,
                    ActionableStep        = $"Verify active connections in {app.DisplayName} or restart the process if network traffic is unintended.",
                    Impact                = RecommendationImpact.Critical,
                    PotentialSavingsBytes = (long)(app.TodayBytes * 0.4),
                    Timestamp             = DateTime.UtcNow
                });
            }
        }

        return recommendations
            .GroupBy(r => r.Title)
            .Select(g => g.First())
            .OrderByDescending(r => r.Impact);
    }

    public async Task<IEnumerable<ApplicationRecommendation>> GetProcessRecommendationsAsync(string processName)
    {
        var allRecs = await GenerateApplicationRecommendationsAsync();
        var processRecs = allRecs.Where(r => r.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase)).ToList();

        if (processRecs.Count == 0)
        {
            var profile = await GetApplicationProfileAsync(processName);
            if (profile != null && profile.HasSufficientData)
            {
                processRecs.Add(new ApplicationRecommendation
                {
                    ProcessName           = processName,
                    Title                 = $"Normal Consumption — {profile.DisplayName}",
                    Description           = $"{profile.DisplayName}'s data usage ({profile.FormattedToday} today) is well within expected historical baselines.",
                    ActionableStep        = "No optimization needed. Application activity is operating normally.",
                    Impact                = RecommendationImpact.Low,
                    PotentialSavingsBytes = 0,
                    Timestamp             = DateTime.UtcNow
                });
            }
            else
            {
                processRecs.Add(new ApplicationRecommendation
                {
                    ProcessName           = processName,
                    Title                 = $"Collecting Telemetry — {GetFriendlyAppName(processName)}",
                    Description           = "Not enough application history to generate a reliable recommendation.",
                    ActionableStep        = "Continue using the application normally to establish baseline metrics.",
                    Impact                = RecommendationImpact.Low,
                    PotentialSavingsBytes = 0,
                    Timestamp             = DateTime.UtcNow
                });
            }
        }

        return processRecs;
    }

    public async Task<IEnumerable<ApplicationUsageProfile>> GetSpikeContributorsAsync(DateTime date)
    {
        var dailyApps = (await _analyticsService.GetTopDataConsumersAsync(AnalyticsPeriod.Today, 10)).ToList();
        var profiles = new List<ApplicationUsageProfile>();

        foreach (var app in dailyApps)
        {
            var profile = await GetApplicationProfileAsync(app.ProcessName);
            if (profile != null && profile.PercentageOfTotalUsage >= 15.0)
            {
                profiles.Add(profile);
            }
        }
        return profiles;
    }

    private static string GetFriendlyAppName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return "Unknown";
        return processName.ToLowerInvariant() switch
        {
            "chrome"           => "Google Chrome",
            "firefox"          => "Mozilla Firefox",
            "code"             => "Visual Studio Code",
            "spotify"          => "Spotify",
            "slack"            => "Slack",
            "discord"          => "Discord",
            "steam"            => "Steam",
            "telegram-desktop" => "Telegram",
            "dropbox"          => "Dropbox",
            _                  => char.ToUpper(processName[0]) + processName[1..]
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

    private static (string State, double? Percentage) CalculateTrendState(long current, long previous)
    {
        if (previous == 0)
        {
            return ("Insufficient Data", null);
        }
        double pct = ((double)(current - previous) / previous) * 100.0;
        string state = pct switch
        {
            > 10.0 => "Increasing",
            < -10.0 => "Decreasing",
            _ => "Stable"
        };
        return (state, pct);
    }

    public async Task<ApplicationNetworkProfile?> GetApplicationNetworkProfileAsync(string processName, int pid, long startTimeTicks)
    {
        if (string.IsNullOrWhiteSpace(processName)) return null;

        var utcNow = DateTime.UtcNow;
        var todayStart = utcNow.Date;
        var sevenDaysStart = todayStart.AddDays(-7);
        var fourteenDaysStart = todayStart.AddDays(-14);
        var thirtyDaysStart = todayStart.AddDays(-30);
        var sixtyDaysStart = todayStart.AddDays(-60);

        string connectionString = (_repository as SqliteNetworkUsageRepository)?.ConnectionString ?? "Data Source=datasense.db";

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
        await connection.OpenAsync();

        // If pid is not supplied, find the latest matching PID and StartTimeTicks
        if (pid <= 0 || startTimeTicks <= 0)
        {
            const string sqlLatest = @"
                SELECT Pid, StartTimeTicks FROM ProcessUsageRecords
                WHERE ProcessName = @ProcessName
                ORDER BY Timestamp DESC LIMIT 1;";
            using var cmdLatest = connection.CreateCommand();
            cmdLatest.CommandText = sqlLatest;
            cmdLatest.Parameters.AddWithValue("@ProcessName", processName);
            using var readerLatest = await cmdLatest.ExecuteReaderAsync();
            if (await readerLatest.ReadAsync())
            {
                pid = readerLatest.GetInt32(0);
                startTimeTicks = readerLatest.GetInt64(1);
            }
            else
            {
                // No telemetry at all for this process
                return null;
            }
        }

        // 1. Gather metadata, timestamps, and samples
        const string sqlMeta = @"
            SELECT
                MIN(Timestamp) AS FirstObserved,
                MAX(Timestamp) AS LastObserved,
                COUNT(*) AS SampleCount,
                MAX(ExecutablePath) AS ExecPath,
                MAX(UserName) AS User,
                MAX(DataSource) AS Source
            FROM ProcessUsageRecords
            WHERE ProcessName = @ProcessName AND Pid = @Pid AND StartTimeTicks = @StartTimeTicks;";
        
        DateTime firstObserved = utcNow;
        DateTime lastObserved = utcNow;
        int sampleCount = 0;
        string execPath = string.Empty;
        string username = string.Empty;
        string dataSource = "Nethogs";

        using (var cmdMeta = connection.CreateCommand())
        {
            cmdMeta.CommandText = sqlMeta;
            cmdMeta.Parameters.AddWithValue("@ProcessName", processName);
            cmdMeta.Parameters.AddWithValue("@Pid", pid);
            cmdMeta.Parameters.AddWithValue("@StartTimeTicks", startTimeTicks);
            using var readerMeta = await cmdMeta.ExecuteReaderAsync();
            if (await readerMeta.ReadAsync() && !readerMeta.IsDBNull(0) && !readerMeta.IsDBNull(1))
            {
                firstObserved = DateTime.Parse(readerMeta.GetString(0), null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
                lastObserved = DateTime.Parse(readerMeta.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
                sampleCount = readerMeta.GetInt32(2);
                execPath = readerMeta.IsDBNull(3) ? string.Empty : readerMeta.GetString(3);
                username = readerMeta.IsDBNull(4) ? string.Empty : readerMeta.GetString(4);
                dataSource = readerMeta.IsDBNull(5) ? "Nethogs" : readerMeta.GetString(5);
            }
            else
            {
                return null;
            }
        }

        // 2. Fetch period byte aggregates
        const string sqlAggs = @"
            SELECT
                SUM(CASE WHEN Timestamp >= @TodayStart THEN BytesDownloaded ELSE 0 END) AS TodayDl,
                SUM(CASE WHEN Timestamp >= @TodayStart THEN BytesUploaded ELSE 0 END) AS TodayUl,
                SUM(CASE WHEN Timestamp >= @SevenDaysStart THEN BytesDownloaded ELSE 0 END) AS SevenDaysDl,
                SUM(CASE WHEN Timestamp >= @SevenDaysStart THEN BytesUploaded ELSE 0 END) AS SevenDaysUl,
                SUM(CASE WHEN Timestamp >= @FourteenDaysStart AND Timestamp < @SevenDaysStart THEN BytesDownloaded ELSE 0 END) AS PrevSevenDaysDl,
                SUM(CASE WHEN Timestamp >= @FourteenDaysStart AND Timestamp < @SevenDaysStart THEN BytesUploaded ELSE 0 END) AS PrevSevenDaysUl,
                SUM(CASE WHEN Timestamp >= @ThirtyDaysStart THEN BytesDownloaded ELSE 0 END) AS ThirtyDaysDl,
                SUM(CASE WHEN Timestamp >= @ThirtyDaysStart THEN BytesUploaded ELSE 0 END) AS ThirtyDaysUl,
                SUM(CASE WHEN Timestamp >= @SixtyDaysStart AND Timestamp < @ThirtyDaysStart THEN BytesDownloaded ELSE 0 END) AS PrevThirtyDaysDl,
                SUM(CASE WHEN Timestamp >= @SixtyDaysStart AND Timestamp < @ThirtyDaysStart THEN BytesUploaded ELSE 0 END) AS PrevThirtyDaysUl
            FROM ProcessUsageRecords
            WHERE ProcessName = @ProcessName AND Pid = @Pid AND StartTimeTicks = @StartTimeTicks;";

        long todayDl = 0, todayUl = 0;
        long sevenDl = 0, sevenUl = 0;
        long prevSevenDl = 0, prevSevenUl = 0;
        long thirtyDl = 0, thirtyUl = 0;
        long prevThirtyDl = 0, prevThirtyUl = 0;

        using (var cmdAggs = connection.CreateCommand())
        {
            cmdAggs.CommandText = sqlAggs;
            cmdAggs.Parameters.AddWithValue("@ProcessName", processName);
            cmdAggs.Parameters.AddWithValue("@Pid", pid);
            cmdAggs.Parameters.AddWithValue("@StartTimeTicks", startTimeTicks);
            cmdAggs.Parameters.AddWithValue("@TodayStart", todayStart.ToString("o"));
            cmdAggs.Parameters.AddWithValue("@SevenDaysStart", sevenDaysStart.ToString("o"));
            cmdAggs.Parameters.AddWithValue("@FourteenDaysStart", fourteenDaysStart.ToString("o"));
            cmdAggs.Parameters.AddWithValue("@ThirtyDaysStart", thirtyDaysStart.ToString("o"));
            cmdAggs.Parameters.AddWithValue("@SixtyDaysStart", sixtyDaysStart.ToString("o"));

            using var readerAggs = await cmdAggs.ExecuteReaderAsync();
            if (await readerAggs.ReadAsync())
            {
                todayDl = readerAggs.IsDBNull(0) ? 0 : readerAggs.GetInt64(0);
                todayUl = readerAggs.IsDBNull(1) ? 0 : readerAggs.GetInt64(1);
                sevenDl = readerAggs.IsDBNull(2) ? 0 : readerAggs.GetInt64(2);
                sevenUl = readerAggs.IsDBNull(3) ? 0 : readerAggs.GetInt64(3);
                prevSevenDl = readerAggs.IsDBNull(4) ? 0 : readerAggs.GetInt64(4);
                prevSevenUl = readerAggs.IsDBNull(5) ? 0 : readerAggs.GetInt64(5);
                thirtyDl = readerAggs.IsDBNull(6) ? 0 : readerAggs.GetInt64(6);
                thirtyUl = readerAggs.IsDBNull(7) ? 0 : readerAggs.GetInt64(7);
                prevThirtyDl = readerAggs.IsDBNull(8) ? 0 : readerAggs.GetInt64(8);
                prevThirtyUl = readerAggs.IsDBNull(9) ? 0 : readerAggs.GetInt64(9);
            }
        }

        // 3. Peak hourly total bytes
        long peakHourlyUsage = 0;
        int? peakHour = null;
        const string sqlPeakHour = @"
            SELECT CAST(strftime('%H', Timestamp) AS INTEGER) AS Hour, SUM(BytesDownloaded + BytesUploaded) AS HourlyTotal
            FROM ProcessUsageRecords
            WHERE ProcessName = @ProcessName AND Pid = @Pid AND StartTimeTicks = @StartTimeTicks
            GROUP BY Hour
            ORDER BY HourlyTotal DESC LIMIT 1;";
        using (var cmdPeakHour = connection.CreateCommand())
        {
            cmdPeakHour.CommandText = sqlPeakHour;
            cmdPeakHour.Parameters.AddWithValue("@ProcessName", processName);
            cmdPeakHour.Parameters.AddWithValue("@Pid", pid);
            cmdPeakHour.Parameters.AddWithValue("@StartTimeTicks", startTimeTicks);
            using var readerPeakHour = await cmdPeakHour.ExecuteReaderAsync();
            if (await readerPeakHour.ReadAsync())
            {
                peakHour = readerPeakHour.GetInt32(0);
                peakHourlyUsage = readerPeakHour.GetInt64(1);
            }
        }

        // Peak download hourly
        int? peakDlHour = null;
        const string sqlPeakDlHour = @"
            SELECT CAST(strftime('%H', Timestamp) AS INTEGER) AS Hour, SUM(BytesDownloaded) AS DownloadTotal
            FROM ProcessUsageRecords
            WHERE ProcessName = @ProcessName AND Pid = @Pid AND StartTimeTicks = @StartTimeTicks
            GROUP BY Hour
            ORDER BY DownloadTotal DESC LIMIT 1;";
        using (var cmdPeakDl = connection.CreateCommand())
        {
            cmdPeakDl.CommandText = sqlPeakDlHour;
            cmdPeakDl.Parameters.AddWithValue("@ProcessName", processName);
            cmdPeakDl.Parameters.AddWithValue("@Pid", pid);
            cmdPeakDl.Parameters.AddWithValue("@StartTimeTicks", startTimeTicks);
            using var readerPeakDl = await cmdPeakDl.ExecuteReaderAsync();
            if (await readerPeakDl.ReadAsync())
            {
                peakDlHour = readerPeakDl.GetInt32(0);
            }
        }

        // Peak upload hourly
        int? peakUlHour = null;
        const string sqlPeakUlHour = @"
            SELECT CAST(strftime('%H', Timestamp) AS INTEGER) AS Hour, SUM(BytesUploaded) AS UploadTotal
            FROM ProcessUsageRecords
            WHERE ProcessName = @ProcessName AND Pid = @Pid AND StartTimeTicks = @StartTimeTicks
            GROUP BY Hour
            ORDER BY UploadTotal DESC LIMIT 1;";
        using (var cmdPeakUl = connection.CreateCommand())
        {
            cmdPeakUl.CommandText = sqlPeakUlHour;
            cmdPeakUl.Parameters.AddWithValue("@ProcessName", processName);
            cmdPeakUl.Parameters.AddWithValue("@Pid", pid);
            cmdPeakUl.Parameters.AddWithValue("@StartTimeTicks", startTimeTicks);
            using var readerPeakUl = await cmdPeakUl.ExecuteReaderAsync();
            if (await readerPeakUl.ReadAsync())
            {
                peakUlHour = readerPeakUl.GetInt32(0);
            }
        }

        // Peak day
        string peakDay = "Not enough application history";
        const string sqlPeakDay = @"
            SELECT date(Timestamp) AS Day, SUM(BytesDownloaded + BytesUploaded) AS DailyTotal
            FROM ProcessUsageRecords
            WHERE ProcessName = @ProcessName AND Pid = @Pid AND StartTimeTicks = @StartTimeTicks
            GROUP BY Day
            ORDER BY DailyTotal DESC LIMIT 1;";
        using (var cmdPeakDay = connection.CreateCommand())
        {
            cmdPeakDay.CommandText = sqlPeakDay;
            cmdPeakDay.Parameters.AddWithValue("@ProcessName", processName);
            cmdPeakDay.Parameters.AddWithValue("@Pid", pid);
            cmdPeakDay.Parameters.AddWithValue("@StartTimeTicks", startTimeTicks);
            using var readerPeakDay = await cmdPeakDay.ExecuteReaderAsync();
            if (await readerPeakDay.ReadAsync())
            {
                peakDay = readerPeakDay.GetString(0);
            }
        }

        // 4. Activity Periods (distinct timestamps)
        var rawTimestamps = new List<DateTime>();
        const string sqlTimestamps = @"
            SELECT DISTINCT Timestamp FROM ProcessUsageRecords
            WHERE ProcessName = @ProcessName AND Pid = @Pid AND StartTimeTicks = @StartTimeTicks
            ORDER BY Timestamp ASC;";
        using (var cmdTime = connection.CreateCommand())
        {
            cmdTime.CommandText = sqlTimestamps;
            cmdTime.Parameters.AddWithValue("@ProcessName", processName);
            cmdTime.Parameters.AddWithValue("@Pid", pid);
            cmdTime.Parameters.AddWithValue("@StartTimeTicks", startTimeTicks);
            using var readerTime = await cmdTime.ExecuteReaderAsync();
            while (await readerTime.ReadAsync())
            {
                rawTimestamps.Add(DateTime.Parse(readerTime.GetString(0)));
            }
        }

        int activityPeriodsCount = 0;
        TimeSpan activeObservationDuration = TimeSpan.Zero;
        if (rawTimestamps.Count > 0)
        {
            activityPeriodsCount = 1;
            DateTime periodStart = rawTimestamps[0];
            DateTime lastTime = rawTimestamps[0];
            for (int i = 1; i < rawTimestamps.Count; i++)
            {
                if (rawTimestamps[i] - lastTime > TimeSpan.FromMinutes(10))
                {
                    activeObservationDuration += (lastTime - periodStart);
                    activityPeriodsCount++;
                    periodStart = rawTimestamps[i];
                }
                lastTime = rawTimestamps[i];
            }
            activeObservationDuration += (lastTime - periodStart);
            if (activeObservationDuration == TimeSpan.Zero)
            {
                activeObservationDuration = TimeSpan.FromSeconds(2 * rawTimestamps.Count);
            }
        }

        // 5. Daily historical series for baselines (excluding today)
        var dailyTotals = new List<double>();
        var dailyDls = new List<double>();
        var dailyUls = new List<double>();
        const string sqlDailyHistory = @"
            SELECT date(Timestamp) AS Day, SUM(BytesDownloaded + BytesUploaded) AS DailyTotal,
                   SUM(BytesDownloaded) AS DailyDl, SUM(BytesUploaded) AS DailyUl
            FROM ProcessUsageRecords
            WHERE ProcessName = @ProcessName AND Pid = @Pid AND StartTimeTicks = @StartTimeTicks
              AND Timestamp < @TodayStart
            GROUP BY Day;";
        using (var cmdDaily = connection.CreateCommand())
        {
            cmdDaily.CommandText = sqlDailyHistory;
            cmdDaily.Parameters.AddWithValue("@ProcessName", processName);
            cmdDaily.Parameters.AddWithValue("@Pid", pid);
            cmdDaily.Parameters.AddWithValue("@StartTimeTicks", startTimeTicks);
            cmdDaily.Parameters.AddWithValue("@TodayStart", todayStart.ToString("o"));
            using var readerDaily = await cmdDaily.ExecuteReaderAsync();
            while (await readerDaily.ReadAsync())
            {
                dailyTotals.Add(readerDaily.GetDouble(1));
                dailyDls.Add(readerDaily.GetDouble(2));
                dailyUls.Add(readerDaily.GetDouble(3));
            }
        }

        bool hasSufficientBaseline = dailyTotals.Count >= 3;
        double avgDailyUsage = hasSufficientBaseline ? dailyTotals.Average() : (thirtyDl + thirtyUl) / Math.Max(1.0, dailyTotals.Count);

        // Anomaly Z-Score
        double zTotal = 0;
        double zDl = 0;
        double zUl = 0;
        string anomalyState = "Normal";

        if (hasSufficientBaseline)
        {
            double meanTotal = dailyTotals.Average();
            double stdDevTotal = Math.Sqrt(dailyTotals.Sum(v => Math.Pow(v - meanTotal, 2)) / dailyTotals.Count);
            
            double meanDl = dailyDls.Average();
            double stdDevDl = Math.Sqrt(dailyDls.Sum(v => Math.Pow(v - meanDl, 2)) / dailyDls.Count);

            double meanUl = dailyUls.Average();
            double stdDevUl = Math.Sqrt(dailyUls.Sum(v => Math.Pow(v - meanUl, 2)) / dailyUls.Count);

            long todayTotal = todayDl + todayUl;
            double effectiveStdDevTotal = stdDevTotal > 0 ? stdDevTotal : 1.0;
            double effectiveStdDevDl = stdDevDl > 0 ? stdDevDl : 1.0;
            double effectiveStdDevUl = stdDevUl > 0 ? stdDevUl : 1.0;

            zTotal = (todayTotal - meanTotal) / effectiveStdDevTotal;
            zDl = (todayDl - meanDl) / effectiveStdDevDl;
            zUl = (todayUl - meanUl) / effectiveStdDevUl;

            double maxZ = Math.Max(zTotal, Math.Max(zDl, zUl));
            if (maxZ > 3.0) anomalyState = "Critical";
            else if (maxZ > 2.0) anomalyState = "Warning";
            else if (maxZ > 1.5) anomalyState = "Elevated";
        }
        else
        {
            anomalyState = "Insufficient Data";
        }

        // 6. Ranks across all processes in the last 30 days
        const string sqlAllProcs = @"
            SELECT ProcessName, Pid, StartTimeTicks,
                   SUM(BytesDownloaded + BytesUploaded) AS TotalUsage,
                   SUM(BytesDownloaded) AS DownloadUsage,
                   SUM(BytesUploaded) AS UploadUsage
            FROM ProcessUsageRecords
            WHERE Timestamp >= @ThirtyDaysStart
            GROUP BY ProcessName, Pid, StartTimeTicks;";

        var allProcs = new List<(string Name, int P, long St, long Total, long Dl, long Ul)>();
        using (var cmdProcs = connection.CreateCommand())
        {
            cmdProcs.CommandText = sqlAllProcs;
            cmdProcs.Parameters.AddWithValue("@ThirtyDaysStart", thirtyDaysStart.ToString("o"));
            using var readerProcs = await cmdProcs.ExecuteReaderAsync();
            while (await readerProcs.ReadAsync())
            {
                allProcs.Add((
                    readerProcs.GetString(0),
                    readerProcs.GetInt32(1),
                    readerProcs.GetInt64(2),
                    readerProcs.GetInt64(3),
                    readerProcs.GetInt64(4),
                    readerProcs.GetInt64(5)
                ));
            }
        }

        // Rank calculations
        int totalRank = 1;
        int downloadRank = 1;
        int uploadRank = 1;

        var rankedByTotal = allProcs.OrderByDescending(p => p.Total).ToList();
        var rankedByDl = allProcs.OrderByDescending(p => p.Dl).ToList();
        var rankedByUl = allProcs.OrderByDescending(p => p.Ul).ToList();

        var totalIndex = rankedByTotal.FindIndex(x => x.Name == processName && x.P == pid && x.St == startTimeTicks);
        if (totalIndex >= 0) totalRank = totalIndex + 1;
        else totalRank = rankedByTotal.Count + 1;

        var dlIndex = rankedByDl.FindIndex(x => x.Name == processName && x.P == pid && x.St == startTimeTicks);
        if (dlIndex >= 0) downloadRank = dlIndex + 1;
        else downloadRank = rankedByDl.Count + 1;

        var ulIndex = rankedByUl.FindIndex(x => x.Name == processName && x.P == pid && x.St == startTimeTicks);
        if (ulIndex >= 0) uploadRank = ulIndex + 1;
        else uploadRank = rankedByUl.Count + 1;

        // 7. System total usage over the last 30 days
        var sysSummary = await _analyticsService.GetSummaryAsync(AnalyticsPeriod.Last30Days);
        long sysTotal = sysSummary.TotalUsage > 0 ? sysSummary.TotalUsage : 1;

        double pctOfSystem = ((double)(thirtyDl + thirtyUl) / sysTotal) * 100.0;

        // 8. Trends
        var (trendState, _) = CalculateTrendState(sevenDl + sevenUl, prevSevenDl + prevSevenUl);

        // 9. Peak Usage Period from peak hour
        string peakPeriod = "Not enough application history";
        if (peakHour.HasValue)
        {
            int h = peakHour.Value;
            if (h >= 0 && h < 6) peakPeriod = "Night (00:00–06:00)";
            else if (h >= 6 && h < 12) peakPeriod = "Morning (06:00–12:00)";
            else if (h >= 12 && h < 18) peakPeriod = "Afternoon (12:00–18:00)";
            else peakPeriod = "Evening (18:00–00:00)";
        }

        double dlRatio = (todayDl + todayUl > 0) ? (double)todayDl / (todayDl + todayUl) : 0.5;

        return new ApplicationNetworkProfile
        {
            ProcessName = processName,
            Pid = pid,
            StartTimeTicks = startTimeTicks,
            ExecutablePath = execPath,
            Username = username,
            DataSource = dataSource,

            TodayDownload = todayDl,
            TodayUpload = todayUl,

            SevenDayDownload = sevenDl,
            SevenDayUpload = sevenUl,

            ThirtyDayDownload = thirtyDl,
            ThirtyDayUpload = thirtyUl,

            PercentageOfTotalSystemUsage = pctOfSystem,
            DownloadUploadRatio = dlRatio,
            AverageDailyUsage = avgDailyUsage,
            PeakHourlyUsage = peakHourlyUsage,

            FirstObserved = firstObserved,
            LastObserved = lastObserved,
            ObservedSessionsCount = sampleCount,

            TrendState = trendState,
            AnomalyState = anomalyState,
            DataSufficiencyState = hasSufficientBaseline ? "Sufficient" : "Collecting Baseline",

            PeakHour = peakHour,
            PeakDay = peakDay,
            PeakUsagePeriod = peakPeriod,
            PeakDownloadHour = peakDlHour,
            PeakUploadHour = peakUlHour
        };
    }
}
