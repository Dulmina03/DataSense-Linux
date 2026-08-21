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
}
