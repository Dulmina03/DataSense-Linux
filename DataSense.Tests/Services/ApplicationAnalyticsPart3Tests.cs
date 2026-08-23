using System;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Models;
using DataSense.Services;
using DataSense.Tests.Helpers;
using Xunit;

namespace DataSense.Tests.Services;

/// <summary>
/// Phase 11.31 Part 3 — ViewModel-state tests for Historical Intelligence UI integration.
/// Tests verify the analytics backend produces the correct state that the ViewModel
/// would bind to. No graphical Avalonia environment is required.
/// </summary>
public class ApplicationAnalyticsPart3Tests
{
    private static ApplicationAnalyticsService MakeSvc(
        DataSense.Database.INetworkUsageRepository repo)
        => new(repo as DataSense.Database.SqliteNetworkUsageRepository
               ?? throw new InvalidOperationException(),
               new MockLinuxProcessResolver());

    private static async Task Seed(DataSense.Database.INetworkUsageRepository repo,
        string name, int pid, long ticks, DateTime ts, long dl, long ul)
        => await repo.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            ProcessName = name, Pid = pid, StartTimeTicks = ticks,
            Timestamp = ts, BytesDownloaded = dl, BytesUploaded = ul,
            DataSource = "Nethogs"
        });

    // ── 1. Application selection — profile not null for a real app ────────────
    [Fact]
    public async Task GetApplicationProfileAsync_AppSelected_ProfileNotNull()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        await Seed(ctx.Repository, "vim", 1, 10, DateTime.UtcNow, 2000, 200);

        var profile = await svc.GetApplicationProfileAsync("vim", 1, 10);
        Assert.NotNull(profile);
        Assert.Equal("vim", profile.ProcessName);
    }

    // ── 2. Period selection — Today gives a profile within today's window ─────
    [Fact]
    public async Task GetApplicationProfileAsync_TodayPeriod_ContainsTodayBytes()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        var today = DateTime.UtcNow.Date;

        await Seed(ctx.Repository, "today-app", 2, 20, today.AddHours(10), 5000, 0);
        await Seed(ctx.Repository, "today-app", 2, 20, today.AddDays(-10).AddHours(10), 1000, 0);

        var profile = await svc.GetApplicationProfileAsync("today-app", 2, 20);
        Assert.NotNull(profile);
        // TodayBytes is the portion from today — must be ≥ 5000
        Assert.True(profile.TodayBytes >= 5000);
    }

    // ── 3. Empty-state: no app selected ──────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfileAsync_UnknownApp_ReturnsNull()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        // No seed — ghost identity
        var profile = await svc.GetApplicationProfileAsync("ghost", 9999, 999999);
        Assert.Null(profile);
    }

    // ── 4. Insufficient history state ─────────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfileAsync_OneActiveDay_HasSufficientDataFalse()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        await Seed(ctx.Repository, "new-app", 3, 30, DateTime.UtcNow, 500, 0);
        var p = await svc.GetApplicationProfileAsync("new-app", 3, 30);
        Assert.NotNull(p);
        Assert.False(p.HasSufficientData, "One active day should not satisfy the 3-day threshold");
    }

    // ── 5. Trend display state ────────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfileAsync_TrendState_CorrectClassification()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        var today = DateTime.UtcNow.Date;

        // Seed 3+ active days in each window
        for (int d = 7; d <= 13; d++)
            await Seed(ctx.Repository, "trend-app", 4, 40, today.AddDays(-d), 1000, 0);
        for (int d = 0; d <= 6; d++)
            await Seed(ctx.Repository, "trend-app", 4, 40, today.AddDays(-d), 5000, 0); // 5× growth

        var p = await svc.GetApplicationProfileAsync("trend-app", 4, 40);
        Assert.NotNull(p);
        Assert.Equal("Increasing", p.TrendState);
        Assert.NotNull(p.TrendPercentage);
        Assert.True(p.TrendPercentage > 10.0);
    }

    // ── 6. Ranking calculations ───────────────────────────────────────────────
    [Fact]
    public async Task GetTopApplicationsAsync_RankingCorrect()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        var today = DateTime.UtcNow.Date;
        await Seed(ctx.Repository, "heavy",  5, 50, today, 9000, 0);
        await Seed(ctx.Repository, "light",  6, 60, today, 1000, 0);
        var top = (await svc.GetTopApplicationsAsync(5)).ToList();
        Assert.True(top.Count >= 2);
        Assert.Equal("heavy", top[0].ProcessName);
        Assert.Equal("light", top[1].ProcessName);
    }

    // ── 6b. Rank position text derivation ────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfilesAsync_RankPosition_CorrectIndex()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        var today = DateTime.UtcNow.Date;
        await Seed(ctx.Repository, "rank-1", 7, 70, today, 8000, 0);
        await Seed(ctx.Repository, "rank-2", 8, 80, today, 4000, 0);
        await Seed(ctx.Repository, "rank-3", 9, 90, today, 2000, 0);

        var all = (await svc.GetApplicationProfilesAsync(forceRefresh: true))
            .OrderByDescending(p => p.TotalBytes).ToList();
        Assert.Equal("rank-1", all[0].ProcessName);
        Assert.Equal(1, all[0].TotalBytes > all[1].TotalBytes ? 1 : 0); // rank-1 is #1
    }

    // ── 7. Download/upload breakdown ──────────────────────────────────────────
    [Fact]
    public async Task GetApplicationTrafficBreakdownAsync_SplitCorrect()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        await Seed(ctx.Repository, "split-app", 10, 100, DateTime.UtcNow, 7500, 2500);
        var bd = await svc.GetApplicationTrafficBreakdownAsync("split-app", 10, 100, AppAnalyticsPeriod.Today);
        Assert.Equal(7500, bd.DownloadBytes);
        Assert.Equal(2500, bd.UploadBytes);
        Assert.Equal(75.0, bd.DownloadPercentage!.Value, 1);
    }

    // ── 8. Historical comparison (today vs yesterday) ─────────────────────────
    [Fact]
    public async Task GetApplicationProfileAsync_TodayAndYesterday_BothPopulated()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        var today = DateTime.UtcNow.Date;
        await Seed(ctx.Repository, "compare", 11, 110, today,            4000, 0);
        await Seed(ctx.Repository, "compare", 11, 110, today.AddDays(-1), 2000, 0);
        var p = await svc.GetApplicationProfileAsync("compare", 11, 110);
        Assert.NotNull(p);
        Assert.True(p.TodayBytes >= 4000);
        Assert.True(p.YesterdayBytes >= 2000);
        // Trend text: today > yesterday → increasing direction
    }

    // ── 9. Dashboard drilldown navigation — NavigateToApplicationAnalytics ────
    [Fact]
    public void MainWindowViewModel_HasNavigateToApplicationAnalytics_Method()
    {
        // Verify the method signature is accessible (compile-time check via reflection)
        var method = typeof(DataSense.ViewModels.MainWindowViewModel)
            .GetMethod("NavigateToApplicationAnalytics");
        Assert.NotNull(method);
        // Method must accept (string processName) or (string, int, long) overloads
        var parms = method.GetParameters();
        Assert.True(parms.Length >= 1);
        Assert.Equal(typeof(string), parms[0].ParameterType);
    }

    // ── 10. Missing process metadata ──────────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfileAsync_MissingExecPath_ProfileReturnedWithEmpty()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            ProcessName = "no-exec", Pid = 12, StartTimeTicks = 120,
            Timestamp = DateTime.UtcNow, BytesDownloaded = 300, BytesUploaded = 0,
            ExecutablePath = string.Empty, DataSource = "Nethogs"
        });
        var p = await svc.GetApplicationProfileAsync("no-exec", 12, 120);
        Assert.NotNull(p);
        Assert.Equal(string.Empty, p.ExecutablePath);
    }

    // ── 11. Missing network metadata ──────────────────────────────────────────
    [Fact]
    public async Task GetApplicationTrafficBreakdownAsync_NoNetworkContext_StillWorks()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        await Seed(ctx.Repository, "no-net-app", 13, 130, DateTime.UtcNow, 800, 200);
        var bd = await svc.GetApplicationTrafficBreakdownAsync("no-net-app", 13, 130, AppAnalyticsPeriod.Today);
        Assert.NotNull(bd);
        Assert.Equal(800, bd.DownloadBytes);
    }

    // ── 12. No fake data when database is empty ───────────────────────────────
    [Fact]
    public async Task AllAnalytics_EmptyDb_NeverReturnFakeValues()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);

        var profiles  = await svc.GetApplicationProfilesAsync(forceRefresh: true);
        var top       = await svc.GetTopApplicationsAsync(10);
        var fastest   = await svc.GetFastestGrowingApplicationsAsync(10);
        var nullProf  = await svc.GetApplicationProfileAsync("none", 0, 0);
        var trend     = await svc.GetApplicationTrendAsync("none", 0, 0);

        Assert.Empty(profiles);
        Assert.Empty(top);
        Assert.Empty(fastest);
        Assert.Null(nullProf);
        Assert.NotNull(trend);
        Assert.False(trend.HasSufficientData);
        Assert.Null(trend.PercentageChange);   // never divide by zero
    }

    // ── 13. Surge text populated correctly ────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfileAsync_SurgeDetected_IsUsageSurgingTrue()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        var today = DateTime.UtcNow.Date;

        // 3 days in previous 7: small usage
        await Seed(ctx.Repository, "surge", 14, 140, today.AddDays(-10), 200, 0);
        await Seed(ctx.Repository, "surge", 14, 140, today.AddDays(-9),  200, 0);
        await Seed(ctx.Repository, "surge", 14, 140, today.AddDays(-8),  200, 0);
        // Recent: 15x bigger
        await Seed(ctx.Repository, "surge", 14, 140, today.AddDays(-3), 10000, 0);
        await Seed(ctx.Repository, "surge", 14, 140, today.AddDays(-2), 10000, 0);

        var p = await svc.GetApplicationProfileAsync("surge", 14, 140);
        Assert.NotNull(p);
        Assert.True(p.IsUsageSurging, "Surge flag must be true for 15x usage increase");
        Assert.NotNull(p.SurgePercentage);
    }

    // ── 14. Trend badge text derivation (state-based) ─────────────────────────
    [Fact]
    public async Task GetApplicationProfileAsync_TrendState_InsufficientDataLabel()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        // Only 1 active day → TrendState should be "Insufficient Data"
        await Seed(ctx.Repository, "singleday", 15, 150, DateTime.UtcNow, 1000, 0);
        var p = await svc.GetApplicationProfileAsync("singleday", 15, 150);
        Assert.NotNull(p);
        Assert.Equal("Insufficient Data", p.TrendState);
    }
}
