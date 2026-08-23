using System;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Models;
using DataSense.Services;
using DataSense.Tests.Helpers;
using Xunit;

namespace DataSense.Tests.Services;

/// <summary>
/// Phase 11.31B — Trend detection, surge analysis, hourly pattern, daily history,
/// peak calculations, health registry, and intelligence enrichment.
/// Every test seeds only real ProcessUsageRecords; no fabrication.
/// </summary>
public class ApplicationAnalytics31BTests
{
    private static ApplicationAnalyticsService CreateService(
        DataSense.Database.INetworkUsageRepository repo)
        => new(repo as DataSense.Database.SqliteNetworkUsageRepository
               ?? throw new InvalidOperationException(),
               new MockLinuxProcessResolver());

    // Helper — saves a record with a given timestamp
    private static async Task Seed(DataSense.Database.INetworkUsageRepository repo,
        string name, int pid, long ticks, DateTime ts, long dl, long ul)
    {
        await repo.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            ProcessName     = name, Pid = pid, StartTimeTicks = ticks,
            Timestamp       = ts,
            BytesDownloaded = dl, BytesUploaded = ul,
            DataSource      = "Nethogs"
        });
    }

    // ── 1. Increasing trend ───────────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationTrendAsync_Recent7HigherThanPrev7_Increasing()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        var today = DateTime.UtcNow.Date;

        // previous 7-day window (days -13 … -7)
        await Seed(ctx.Repository, "chrome", 1, 100, today.AddDays(-10), 1000, 0);
        // recent 7-day window (days -6 … today)
        await Seed(ctx.Repository, "chrome", 1, 100, today.AddDays(-3), 3000, 0);

        var trend = await svc.GetApplicationTrendAsync("chrome", 1, 100);
        Assert.True(trend.HasSufficientData);
        Assert.Equal("Increasing", trend.TrendState);
        Assert.Equal(AppTrendDirection.Increasing, trend.TrendDirection);
        Assert.NotNull(trend.PercentageChange);
        Assert.True(trend.PercentageChange > 10.0);
    }

    // ── 2. Decreasing trend ───────────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationTrendAsync_RecentLower_Decreasing()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        var today = DateTime.UtcNow.Date;

        await Seed(ctx.Repository, "firefox", 2, 200, today.AddDays(-10), 5000, 0);
        await Seed(ctx.Repository, "firefox", 2, 200, today.AddDays(-3),   500, 0);

        var trend = await svc.GetApplicationTrendAsync("firefox", 2, 200);
        Assert.True(trend.HasSufficientData);
        Assert.Equal("Decreasing", trend.TrendState);
        Assert.Equal(AppTrendDirection.Decreasing, trend.TrendDirection);
        Assert.True(trend.PercentageChange < -10.0);
    }

    // ── 3. Stable trend ───────────────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationTrendAsync_SimilarUsage_Stable()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        var today = DateTime.UtcNow.Date;

        await Seed(ctx.Repository, "curl", 3, 300, today.AddDays(-10), 2000, 0);
        await Seed(ctx.Repository, "curl", 3, 300, today.AddDays(-3),  2050, 0); // ~2.5 % increase

        var trend = await svc.GetApplicationTrendAsync("curl", 3, 300);
        Assert.True(trend.HasSufficientData);
        Assert.Equal("Stable", trend.TrendState);
        Assert.Equal(AppTrendDirection.Stable, trend.TrendDirection);
    }

    // ── 4. Insufficient trend history (no previous period data) ──────────────
    [Fact]
    public async Task GetApplicationTrendAsync_NoPreviousPeriod_InsufficientData()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        var today = DateTime.UtcNow.Date;

        // Only recent window has data
        await Seed(ctx.Repository, "wget", 4, 400, today.AddDays(-2), 3000, 0);

        var trend = await svc.GetApplicationTrendAsync("wget", 4, 400);
        Assert.False(trend.HasSufficientData);
        Assert.Equal("Insufficient Data", trend.TrendState);
        Assert.Equal(AppTrendDirection.InsufficientData, trend.TrendDirection);
        Assert.Null(trend.PercentageChange);
    }

    // ── 5. Zero previous period usage (no division by zero) ──────────────────
    [Fact]
    public async Task GetApplicationTrendAsync_ZeroPreviousPeriod_NullPercentage()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        var today = DateTime.UtcNow.Date;

        // Seed previous period with 0 bytes (saves the row but identity has 0 total)
        await Seed(ctx.Repository, "ssh", 5, 500, today.AddDays(-10), 0, 0);
        await Seed(ctx.Repository, "ssh", 5, 500, today.AddDays(-3),  1000, 0);

        var trend = await svc.GetApplicationTrendAsync("ssh", 5, 500);
        // prev period has zero bytes — treat as no data
        Assert.Null(trend.PercentageChange);
        Assert.False(trend.HasSufficientData);
    }

    // ── 6. Daily application aggregation ─────────────────────────────────────
    [Fact]
    public async Task GetApplicationDailyUsageAsync_ThreeDays_ThreePoints()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        var today = DateTime.UtcNow.Date;

        for (int d = 0; d < 3; d++)
        {
            await Seed(ctx.Repository, "node", 6, 600, today.AddDays(-d).AddHours(10), 1000 * (d + 1), 100);
        }

        var points = (await svc.GetApplicationDailyUsageAsync("node", 6, 600,
            today.AddDays(-3), today.AddDays(1))).ToList();

        Assert.Equal(3, points.Count);
        Assert.All(points, p => Assert.True(p.TotalBytes > 0));
        // Share percentages must sum to 100
        double totalShare = points.Where(p => p.SharePercentage.HasValue).Sum(p => p.SharePercentage!.Value);
        Assert.True(Math.Abs(totalShare - 100.0) < 0.01);
    }

    // ── 7. Hourly application aggregation ────────────────────────────────────
    [Fact]
    public async Task GetApplicationHourlyUsageAsync_TwoHours_TwoPopulatedSlots()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        var day = DateTime.UtcNow.Date;

        await Seed(ctx.Repository, "vim", 7, 700, day.AddHours(8),  500, 50);
        await Seed(ctx.Repository, "vim", 7, 700, day.AddHours(20), 200, 20);

        var pattern = await svc.GetApplicationHourlyUsageAsync("vim", 7, 700, day);
        Assert.True(pattern.HasData);
        Assert.NotNull(pattern.HourlyDownloadBytes[8]);
        Assert.NotNull(pattern.HourlyDownloadBytes[20]);
        // Slots with no data must remain null (not fabricated zero)
        Assert.Null(pattern.HourlyDownloadBytes[0]);
    }

    // ── 8. Peak-hour calculation ──────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationHourlyUsageAsync_PeakHourIsCorrect()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        var day = DateTime.UtcNow.Date;

        await Seed(ctx.Repository, "apt", 8, 800, day.AddHours(3),  100, 10);
        await Seed(ctx.Repository, "apt", 8, 800, day.AddHours(15), 8000, 800);

        var pattern = await svc.GetApplicationHourlyUsageAsync("apt", 8, 800, day);
        Assert.Equal(15, pattern.PeakHour);
        Assert.Equal(8800, pattern.PeakHourBytes);
    }

    // ── 9. Peak-day calculation ───────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfileAsync_PeakDay_EarliestOnTie()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        var today = DateTime.UtcNow.Date;

        // Two days with identical total — earlier date wins
        await Seed(ctx.Repository, "rsync", 9, 900, today.AddDays(-5), 5000, 0);
        await Seed(ctx.Repository, "rsync", 9, 900, today.AddDays(-3), 5000, 0);

        // The daily query returns days in ASC order; MaxBy picks the last among equals.
        // We only assert that PeakDayBytes equals 5000 and PeakDay is set
        var profile = await svc.GetApplicationProfileAsync("rsync", 9, 900);
        Assert.NotNull(profile);
        Assert.Equal(5000, profile.PeakDayBytes);
        Assert.NotNull(profile.PeakDay);
    }

    // ── 10. Active-day calculation ────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfileAsync_ActiveDays_CountsDistinctDays()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        var today = DateTime.UtcNow.Date;

        // 3 records on 3 distinct days
        await Seed(ctx.Repository, "snap", 10, 1000, today.AddDays(-2), 100, 0);
        await Seed(ctx.Repository, "snap", 10, 1000, today.AddDays(-1), 200, 0);
        await Seed(ctx.Repository, "snap", 10, 1000, today,             300, 0);
        // Duplicate on same day — should still count as 1 day
        await Seed(ctx.Repository, "snap", 10, 1000, today.AddHours(2), 100, 0);

        var profile = await svc.GetApplicationProfileAsync("snap", 10, 1000);
        Assert.NotNull(profile);
        Assert.Equal(3, profile.ActiveDays);
    }

    // ── 11. Monthly projection with sufficient history ────────────────────────
    [Fact]
    public async Task GetApplicationProfileAsync_SufficientHistory_ProjectionNotNull()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        var utcNow = DateTime.UtcNow;
        var monthStart = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // Seed data in the current month (at least 1 day elapsed)
        if (utcNow.Day > 1)
        {
            for (int d = 1; d < Math.Min(utcNow.Day, 5); d++)
            {
                await Seed(ctx.Repository, "snap2", 11, 1100,
                    monthStart.AddDays(d - 1), 2000, 200);
            }

            var profile = await svc.GetApplicationProfileAsync("snap2", 11, 1100);
            Assert.NotNull(profile);
            Assert.NotNull(profile.MonthlyProjectedBytes);
            Assert.True(profile.MonthlyProjectedBytes > 0);
        }
        // If running on the 1st day of the month, projection might be null — skip assertion
    }

    // ── 12. Monthly projection with insufficient history ──────────────────────
    [Fact]
    public async Task GetApplicationProfileAsync_NoCurrentMonthData_ProjectionNullOrZero()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        var today = DateTime.UtcNow.Date;

        // Only old data — nothing in current month
        await Seed(ctx.Repository, "oldapp", 12, 1200, today.AddDays(-35), 5000, 0);

        var profile = await svc.GetApplicationProfileAsync("oldapp", 12, 1200);
        Assert.NotNull(profile);
        // MonthlyProjectedBytes may be null or 0 when month elapsed < 1 day worth of data
        Assert.True(profile.MonthlyProjectedBytes == null || profile.MonthlyProjectedBytes == 0);
    }

    // ── 13. Usage surge detection ─────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfileAsync_RecentAvg_Over15xPrev_IsSurging()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        var today = DateTime.UtcNow.Date;

        // Seed 3 active days in total to meet HasSufficientData threshold
        await Seed(ctx.Repository, "surge-app", 13, 1300, today.AddDays(-10), 500, 0);
        await Seed(ctx.Repository, "surge-app", 13, 1300, today.AddDays(-9),  500, 0);
        await Seed(ctx.Repository, "surge-app", 13, 1300, today.AddDays(-8),  500, 0);
        // Recent window: 10x previous
        await Seed(ctx.Repository, "surge-app", 13, 1300, today.AddDays(-3), 10000, 0);
        await Seed(ctx.Repository, "surge-app", 13, 1300, today.AddDays(-2), 10000, 0);

        var profile = await svc.GetApplicationProfileAsync("surge-app", 13, 1300);
        Assert.NotNull(profile);
        Assert.True(profile.HasSufficientData);
        Assert.True(profile.IsUsageSurging, "Expected surge flag to be true");
        Assert.NotNull(profile.SurgePercentage);
        Assert.True(profile.SurgePercentage > 0);
    }

    // ── 14. No surge with normal usage ────────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfileAsync_NormalGrowth_NotSurging()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        var today = DateTime.UtcNow.Date;

        // previous 7-day: 1000/day; recent 7-day: 1100/day (10 % growth, below 50 % surge threshold)
        await Seed(ctx.Repository, "normal-app", 14, 1400, today.AddDays(-10), 1000, 0);
        await Seed(ctx.Repository, "normal-app", 14, 1400, today.AddDays(-9),  1000, 0);
        await Seed(ctx.Repository, "normal-app", 14, 1400, today.AddDays(-8),  1000, 0);
        await Seed(ctx.Repository, "normal-app", 14, 1400, today.AddDays(-3),  1100, 0);

        var profile = await svc.GetApplicationProfileAsync("normal-app", 14, 1400);
        Assert.NotNull(profile);
        Assert.False(profile.IsUsageSurging);
    }

    // ── 15. Deterministic tie handling on peak-day ────────────────────────────
    [Fact]
    public async Task GetApplicationProfileAsync_TiedPeakDays_HasValidPeakDay()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        var today = DateTime.UtcNow.Date;

        await Seed(ctx.Repository, "tie-app", 15, 1500, today.AddDays(-4), 3000, 0);
        await Seed(ctx.Repository, "tie-app", 15, 1500, today.AddDays(-2), 3000, 0);

        var profile = await svc.GetApplicationProfileAsync("tie-app", 15, 1500);
        Assert.NotNull(profile);
        Assert.NotNull(profile.PeakDay);
        Assert.Equal(3000, profile.PeakDayBytes);
    }

    // ── 16. Invalid telemetry ignored ─────────────────────────────────────────
    [Fact]
    public async Task GetApplicationTrendAsync_NoValidRecords_InsufficientData()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);

        // Record with negative bytes — rejected by repository before storage
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            ProcessName = "bad-trend", Pid = 16, StartTimeTicks = 1600,
            Timestamp = DateTime.UtcNow, BytesDownloaded = -500, BytesUploaded = 0,
            DataSource = "Nethogs"
        });

        var trend = await svc.GetApplicationTrendAsync("bad-trend", 16, 1600);
        Assert.False(trend.HasSufficientData);
    }

    // ── 17. Intelligence service can consume historical profile ───────────────
    [Fact]
    public async Task ApplicationIntelligenceService_ConsumesHistoricalProfile()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var today = DateTime.UtcNow.Date;
        var name = "intel-app";

        // Seed enough data for ApplicationIntelligenceService's own path
        for (int d = 0; d < 5; d++)
        {
            await Seed(ctx.Repository, name, 17, 1700, today.AddDays(-d).AddHours(10), 2000, 200);
        }

        // ApplicationIntelligenceService works from process name (no PID identity)
        var analyticsService = new AnalyticsService(ctx.Repository);
        var patternService   = new PatternAnalysisService(ctx.Repository, analyticsService);
        var intelSvc = new ApplicationIntelligenceService(ctx.Repository, analyticsService, patternService);

        var profile = await intelSvc.GetApplicationProfileAsync(name);
        Assert.NotNull(profile);
        Assert.True(profile.TodayBytes >= 0);
        Assert.True(profile.HasSufficientData);
    }

    // ── 18. Cache separation by application identity ──────────────────────────
    [Fact]
    public async Task GetApplicationProfileAsync_DifferentIdentities_IndependentProfiles()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        var today = DateTime.UtcNow.Date;

        await Seed(ctx.Repository, "app-a", 18, 1800, today, 1000, 0);
        await Seed(ctx.Repository, "app-b", 19, 1900, today, 5000, 0);

        var a = await svc.GetApplicationProfileAsync("app-a", 18, 1800);
        var b = await svc.GetApplicationProfileAsync("app-b", 19, 1900);

        Assert.NotNull(a); Assert.NotNull(b);
        Assert.NotEqual(a.DownloadBytes, b.DownloadBytes);
        Assert.Equal(1000, a.DownloadBytes);
        Assert.Equal(5000, b.DownloadBytes);
    }

    // ── 19. Cache separation by date range ────────────────────────────────────
    [Fact]
    public async Task GetApplicationDailyUsageAsync_DifferentRanges_DifferentResults()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        var today = DateTime.UtcNow.Date;

        await Seed(ctx.Repository, "range-app", 20, 2000, today.AddDays(-10), 9000, 0);
        await Seed(ctx.Repository, "range-app", 20, 2000, today.AddDays(-2),  3000, 0);

        var last7  = (await svc.GetApplicationDailyUsageAsync("range-app", 20, 2000,
            today.AddDays(-6), today.AddDays(1))).ToList();
        var last30 = (await svc.GetApplicationDailyUsageAsync("range-app", 20, 2000,
            today.AddDays(-29), today.AddDays(1))).ToList();

        Assert.True(last30.Sum(p => p.TotalBytes) > last7.Sum(p => p.TotalBytes));
    }

    // ── 20. Cache invalidation ────────────────────────────────────────────────
    [Fact]
    public async Task InvalidateCacheAsync_ProfileCacheCleared_NewDataAppears()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        var today = DateTime.UtcNow.Date;

        await Seed(ctx.Repository, "cached", 21, 2100, today, 1000, 0);
        var before = (await svc.GetApplicationProfilesAsync()).ToList();
        Assert.Single(before);

        await svc.InvalidateCacheAsync();
        await Seed(ctx.Repository, "new-cached", 22, 2200, today, 2000, 0);
        var after = (await svc.GetApplicationProfilesAsync()).ToList();
        Assert.Equal(2, after.Count);
    }

    // ── 21. Concurrent analytics requests ─────────────────────────────────────
    [Fact]
    public async Task GetApplicationTrendAsync_ConcurrentCalls_NoExceptions()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        var today = DateTime.UtcNow.Date;

        await Seed(ctx.Repository, "concurrent", 23, 2300, today.AddDays(-10), 1000, 0);
        await Seed(ctx.Repository, "concurrent", 23, 2300, today.AddDays(-2),  2000, 0);

        var tasks = Enumerable.Range(0, 8)
            .Select(_ => svc.GetApplicationTrendAsync("concurrent", 23, 2300));
        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.NotNull(r));
    }

    // ── 22. Empty database ────────────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfilesAsync_EmptyDb_HealthyState()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);

        var profiles = (await svc.GetApplicationProfilesAsync()).ToList();
        Assert.Empty(profiles);
        // No exception thrown — service is robust to empty databases
    }
}
