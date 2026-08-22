using System;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Models;
using DataSense.Services;
using DataSense.Tests.Helpers;
using Xunit;

namespace DataSense.Tests.Services;

public class ApplicationIntelligenceServiceTests
{
    private static async Task SeedProcessUsageWithIdentityAsync(
        DataSense.Database.INetworkUsageRepository repo,
        string processName,
        int pid,
        long startTimeTicks,
        DateTime timestamp,
        long rxBytes,
        long txBytes,
        string execPath = "/usr/bin/test",
        string username = "testuser",
        string dataSource = "Nethogs")
    {
        await repo.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            Timestamp = timestamp,
            ProcessName = processName,
            Pid = pid,
            StartTimeTicks = startTimeTicks,
            BytesDownloaded = rxBytes,
            BytesUploaded = txBytes,
            ExecutablePath = execPath,
            UserName = username,
            DataSource = dataSource
        });
    }

    [Fact]
    public async Task GenerateRecommendations_WithEmptyData_ReturnsBaselineRecommendation()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var patternService = new PatternAnalysisService(context.Repository, analytics);
        var appIntelService = new ApplicationIntelligenceService(context.Repository, analytics, patternService);

        var recs = (await appIntelService.GenerateApplicationRecommendationsAsync()).ToList();

        Assert.Single(recs);
        Assert.Equal("Establishing Application Baselines", recs[0].Title);
        Assert.Equal(RecommendationImpact.Low, recs[0].Impact);
    }

    [Fact]
    public async Task GenerateRecommendations_WithDeterministicHighConsumer_ReturnsDeterministicHighBandwidthRecommendation()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var patternService = new PatternAnalysisService(context.Repository, analytics);
        var appIntelService = new ApplicationIntelligenceService(context.Repository, analytics, patternService);

        DateTime now = DateTime.UtcNow;
        long heavyBytes = 500_000_000; // 500 MB

        // Seed 4 days of history so HasSufficientData is true
        for (int i = 4; i >= 0; i--)
        {
            DateTime day = now.AddDays(-i);
            await TestDataBuilder.SeedProcessUsageAsync(context.Repository, "chrome", day, heavyBytes, 50_000_000);
        }

        var recs1 = (await appIntelService.GenerateApplicationRecommendationsAsync()).ToList();
        var recs2 = (await appIntelService.GenerateApplicationRecommendationsAsync()).ToList();

        Assert.NotEmpty(recs1);
        Assert.Equal(recs1.Count, recs2.Count);
        Assert.Equal(recs1[0].Title, recs2[0].Title);
        Assert.Equal(recs1[0].PotentialSavingsBytes, recs2[0].PotentialSavingsBytes);
    }

    [Fact]
    public async Task GetApplicationNetworkProfileAsync_NullOrEmptyProcessName_ReturnsNull()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var patternService = new PatternAnalysisService(context.Repository, analytics);
        var appIntelService = new ApplicationIntelligenceService(context.Repository, analytics, patternService);

        var profile = await appIntelService.GetApplicationNetworkProfileAsync(null!, 123, 4567);
        Assert.Null(profile);

        profile = await appIntelService.GetApplicationNetworkProfileAsync("", 123, 4567);
        Assert.Null(profile);
    }

    [Fact]
    public async Task GetApplicationNetworkProfileAsync_NoTelemetry_ReturnsNull()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var patternService = new PatternAnalysisService(context.Repository, analytics);
        var appIntelService = new ApplicationIntelligenceService(context.Repository, analytics, patternService);

        var profile = await appIntelService.GetApplicationNetworkProfileAsync("nonexistent", 123, 4567);
        Assert.Null(profile);
    }

    [Fact]
    public async Task GetApplicationNetworkProfileAsync_PidFallback_FindsLatestProcessIdentity()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var patternService = new PatternAnalysisService(context.Repository, analytics);
        var appIntelService = new ApplicationIntelligenceService(context.Repository, analytics, patternService);

        DateTime now = DateTime.UtcNow;
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now.AddMinutes(-5), 100, 200);
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1002, 200000, now, 300, 400);

        var profile = await appIntelService.GetApplicationNetworkProfileAsync("chrome", 0, 0);

        Assert.NotNull(profile);
        Assert.Equal(1002, profile.Pid);
        Assert.Equal(200000, profile.StartTimeTicks);
    }

    [Fact]
    public async Task GetApplicationNetworkProfileAsync_MetadataCorrectlyMapped()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var patternService = new PatternAnalysisService(context.Repository, analytics);
        var appIntelService = new ApplicationIntelligenceService(context.Repository, analytics, patternService);

        DateTime now = DateTime.UtcNow;
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now, 100, 200, "/usr/bin/chrome", "alice", "Nethogs");

        var profile = await appIntelService.GetApplicationNetworkProfileAsync("chrome", 1001, 100000);

        Assert.NotNull(profile);
        Assert.Equal("chrome", profile.ProcessName);
        Assert.Equal(1001, profile.Pid);
        Assert.Equal("/usr/bin/chrome", profile.ExecutablePath);
        Assert.Equal("alice", profile.Username);
        Assert.Equal("Nethogs", profile.DataSource);
    }

    [Fact]
    public async Task GetApplicationNetworkProfileAsync_TodayUploadDownload_CalculatedCorrectly()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var patternService = new PatternAnalysisService(context.Repository, analytics);
        var appIntelService = new ApplicationIntelligenceService(context.Repository, analytics, patternService);

        DateTime now = DateTime.UtcNow;
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now.AddHours(-1), 1000, 500);
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now.AddDays(-2), 5000, 2500);

        var profile = await appIntelService.GetApplicationNetworkProfileAsync("chrome", 1001, 100000);

        Assert.NotNull(profile);
        Assert.Equal(1000, profile.TodayDownload);
        Assert.Equal(500, profile.TodayUpload);
        Assert.Equal(1500, profile.TodayTotal);
    }

    [Fact]
    public async Task GetApplicationNetworkProfileAsync_SevenDayUploadDownload_CalculatedCorrectly()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var patternService = new PatternAnalysisService(context.Repository, analytics);
        var appIntelService = new ApplicationIntelligenceService(context.Repository, analytics, patternService);

        DateTime now = DateTime.UtcNow;
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now.AddHours(-1), 1000, 500);
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now.AddDays(-2), 2000, 1000);
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now.AddDays(-8), 5000, 2500);

        var profile = await appIntelService.GetApplicationNetworkProfileAsync("chrome", 1001, 100000);

        Assert.NotNull(profile);
        Assert.Equal(3000, profile.SevenDayDownload);
        Assert.Equal(4500, profile.SevenDayTotal);
    }

    [Fact]
    public async Task GetApplicationNetworkProfileAsync_Ratio_DownloadHeavy_ClassifiedCorrectly()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var patternService = new PatternAnalysisService(context.Repository, analytics);
        var appIntelService = new ApplicationIntelligenceService(context.Repository, analytics, patternService);

        DateTime now = DateTime.UtcNow;
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now, 900, 100);

        var profile = await appIntelService.GetApplicationNetworkProfileAsync("chrome", 1001, 100000);

        Assert.NotNull(profile);
        Assert.Equal(0.9, profile.DownloadUploadRatio);
    }

    [Fact]
    public async Task GetApplicationNetworkProfileAsync_PeakHourlyUsage_CalculatedCorrectly()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var patternService = new PatternAnalysisService(context.Repository, analytics);
        var appIntelService = new ApplicationIntelligenceService(context.Repository, analytics, patternService);

        DateTime now = DateTime.UtcNow.Date; // start of day
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now.AddHours(2), 100, 50); // hour 2
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now.AddHours(14), 500, 250); // hour 14
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now.AddHours(14).AddMinutes(10), 100, 50); // hour 14

        var profile = await appIntelService.GetApplicationNetworkProfileAsync("chrome", 1001, 100000);

        Assert.NotNull(profile);
        Assert.Equal(14, profile.PeakHour);
        Assert.Equal(900, profile.PeakHourlyUsage);
    }

    [Fact]
    public async Task GetApplicationNetworkProfileAsync_PeakUsagePeriod_Night_MappedCorrectly()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var patternService = new PatternAnalysisService(context.Repository, analytics);
        var appIntelService = new ApplicationIntelligenceService(context.Repository, analytics, patternService);

        DateTime now = DateTime.UtcNow.Date;
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now.AddHours(3), 100, 50);

        var profile = await appIntelService.GetApplicationNetworkProfileAsync("chrome", 1001, 100000);

        Assert.NotNull(profile);
        Assert.Contains("Night", profile.PeakUsagePeriod);
    }

    [Fact]
    public async Task GetApplicationNetworkProfileAsync_PeakUsagePeriod_Morning_MappedCorrectly()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var patternService = new PatternAnalysisService(context.Repository, analytics);
        var appIntelService = new ApplicationIntelligenceService(context.Repository, analytics, patternService);

        DateTime now = DateTime.UtcNow.Date;
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now.AddHours(9), 100, 50);

        var profile = await appIntelService.GetApplicationNetworkProfileAsync("chrome", 1001, 100000);

        Assert.NotNull(profile);
        Assert.Contains("Morning", profile.PeakUsagePeriod);
    }

    [Fact]
    public async Task GetApplicationNetworkProfileAsync_FirstAndLastObserved_MappedCorrectly()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var patternService = new PatternAnalysisService(context.Repository, analytics);
        var appIntelService = new ApplicationIntelligenceService(context.Repository, analytics, patternService);

        DateTime now = DateTime.UtcNow.Date;
        DateTime first = now.AddDays(-5);
        DateTime last = now.AddHours(-1);
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, first, 100, 50);
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, last, 200, 100);

        var profile = await appIntelService.GetApplicationNetworkProfileAsync("chrome", 1001, 100000);

        Assert.NotNull(profile);
        Assert.Equal(first.ToUniversalTime().ToString("o"), profile.FirstObserved.ToString("o"));
        Assert.Equal(last.ToUniversalTime().ToString("o"), profile.LastObserved.ToString("o"));
        Assert.Equal(2, profile.ObservedSessionsCount);
    }

    [Fact]
    public async Task GetApplicationNetworkProfileAsync_TrendState_Increasing_CalculatedCorrectly()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var patternService = new PatternAnalysisService(context.Repository, analytics);
        var appIntelService = new ApplicationIntelligenceService(context.Repository, analytics, patternService);

        DateTime now = DateTime.UtcNow;
        // Last 7 days: 2000 bytes
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now.AddDays(-2), 1500, 500);
        // Prev 7 days: 1000 bytes
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now.AddDays(-9), 800, 200);

        var profile = await appIntelService.GetApplicationNetworkProfileAsync("chrome", 1001, 100000);

        Assert.NotNull(profile);
        Assert.Equal("Increasing", profile.TrendState);
    }

    [Fact]
    public async Task GetApplicationNetworkProfileAsync_TrendState_Decreasing_CalculatedCorrectly()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var patternService = new PatternAnalysisService(context.Repository, analytics);
        var appIntelService = new ApplicationIntelligenceService(context.Repository, analytics, patternService);

        DateTime now = DateTime.UtcNow;
        // Last 7 days: 500 bytes
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now.AddDays(-2), 400, 100);
        // Prev 7 days: 1000 bytes
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now.AddDays(-9), 800, 200);

        var profile = await appIntelService.GetApplicationNetworkProfileAsync("chrome", 1001, 100000);

        Assert.NotNull(profile);
        Assert.Equal("Decreasing", profile.TrendState);
    }

    [Fact]
    public async Task GetApplicationNetworkProfileAsync_TrendState_Stable_CalculatedCorrectly()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var patternService = new PatternAnalysisService(context.Repository, analytics);
        var appIntelService = new ApplicationIntelligenceService(context.Repository, analytics, patternService);

        DateTime now = DateTime.UtcNow;
        // Last 7 days: 1000 bytes
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now.AddDays(-2), 800, 200);
        // Prev 7 days: 1000 bytes
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now.AddDays(-9), 800, 200);

        var profile = await appIntelService.GetApplicationNetworkProfileAsync("chrome", 1001, 100000);

        Assert.NotNull(profile);
        Assert.Equal("Stable", profile.TrendState);
    }

    [Fact]
    public async Task GetApplicationNetworkProfileAsync_TrendState_ZeroUsagePrev_ReturnsInsufficientData()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var patternService = new PatternAnalysisService(context.Repository, analytics);
        var appIntelService = new ApplicationIntelligenceService(context.Repository, analytics, patternService);

        DateTime now = DateTime.UtcNow;
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now.AddDays(-2), 800, 200);

        var profile = await appIntelService.GetApplicationNetworkProfileAsync("chrome", 1001, 100000);

        Assert.NotNull(profile);
        Assert.Equal("Insufficient Data", profile.TrendState);
    }

    [Fact]
    public async Task GetApplicationNetworkProfileAsync_AnomalyState_InsufficientData_WhenFewerThanThreeDays()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var patternService = new PatternAnalysisService(context.Repository, analytics);
        var appIntelService = new ApplicationIntelligenceService(context.Repository, analytics, patternService);

        DateTime now = DateTime.UtcNow;
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now.AddDays(-1), 100, 50);
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now, 100, 50);

        var profile = await appIntelService.GetApplicationNetworkProfileAsync("chrome", 1001, 100000);

        Assert.NotNull(profile);
        Assert.Equal("Insufficient Data", profile.AnomalyState);
    }

    [Fact]
    public async Task GetApplicationNetworkProfileAsync_AnomalyState_Critical_WhenZScoreHigh()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var patternService = new PatternAnalysisService(context.Repository, analytics);
        var appIntelService = new ApplicationIntelligenceService(context.Repository, analytics, patternService);

        DateTime now = DateTime.UtcNow;
        // Seed 4 days of very low stable usage
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now.AddDays(-4), 10, 10);
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now.AddDays(-3), 10, 10);
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now.AddDays(-2), 10, 10);
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now.AddDays(-1), 10, 10);
        // Today is huge usage
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now, 1_000_000, 1_000_000);

        var profile = await appIntelService.GetApplicationNetworkProfileAsync("chrome", 1001, 100000);

        Assert.NotNull(profile);
        Assert.Equal("Critical", profile.AnomalyState);
    }

    [Fact]
    public async Task GetApplicationNetworkProfileAsync_AnomalyState_Normal_WhenZScoreLow()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var patternService = new PatternAnalysisService(context.Repository, analytics);
        var appIntelService = new ApplicationIntelligenceService(context.Repository, analytics, patternService);

        DateTime now = DateTime.UtcNow;
        // Seed 4 days of stable usage
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now.AddDays(-4), 1000, 1000);
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now.AddDays(-3), 1050, 1050);
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now.AddDays(-2), 980, 980);
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now.AddDays(-1), 1020, 1020);
        // Today is normal
        await SeedProcessUsageWithIdentityAsync(context.Repository, "chrome", 1001, 100000, now, 1010, 1010);

        var profile = await appIntelService.GetApplicationNetworkProfileAsync("chrome", 1001, 100000);

        Assert.NotNull(profile);
        Assert.Equal("Normal", profile.AnomalyState);
    }
}
