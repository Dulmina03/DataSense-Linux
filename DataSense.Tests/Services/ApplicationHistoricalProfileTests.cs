using System;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Models;
using DataSense.Services;
using DataSense.Tests.Helpers;
using Xunit;

namespace DataSense.Tests.Services;

/// <summary>
/// Phase 11.31A — ApplicationAnalyticsService new methods.
/// All tests use isolated in-memory SQLite databases seeded with
/// deterministic ProcessUsageRecord data only.
/// </summary>
public class ApplicationHistoricalProfileTests
{
    private static ApplicationAnalyticsService CreateService(DataSense.Database.INetworkUsageRepository repo)
        => new(repo as DataSense.Database.SqliteNetworkUsageRepository ?? throw new InvalidOperationException(), new MockLinuxProcessResolver());

    // ── 1. Empty database ───────────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfilesAsync_EmptyDb_ReturnsEmpty()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        var profiles = await svc.GetApplicationProfilesAsync();
        Assert.Empty(profiles);
    }

    // ── 2. Single application ───────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfilesAsync_SingleApp_ReturnsOneProfile()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            Timestamp = DateTime.UtcNow, ProcessName = "firefox",
            Pid = 1001, StartTimeTicks = 99999,
            BytesDownloaded = 2000, BytesUploaded = 500,
            DataSource = "Nethogs", UserName = "alice",
            ExecutablePath = "/usr/bin/firefox"
        });

        var profiles = (await svc.GetApplicationProfilesAsync()).ToList();
        Assert.Single(profiles);
        Assert.Equal("firefox", profiles[0].ProcessName);
        Assert.Equal(2000, profiles[0].DownloadBytes);
        Assert.Equal(500, profiles[0].UploadBytes);
        Assert.Equal(100.0, profiles[0].PercentageOfTotal, 1);
    }

    // ── 3. Multiple applications — correct percentage split ─────────────────
    [Fact]
    public async Task GetApplicationProfilesAsync_MultipleApps_PercentageSumsTo100()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            Timestamp = DateTime.UtcNow, ProcessName = "chrome",
            Pid = 1, StartTimeTicks = 1, BytesDownloaded = 3000, BytesUploaded = 1000, DataSource = "Nethogs"
        });
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            Timestamp = DateTime.UtcNow, ProcessName = "curl",
            Pid = 2, StartTimeTicks = 2, BytesDownloaded = 1000, BytesUploaded = 0, DataSource = "Nethogs"
        });

        var profiles = (await svc.GetApplicationProfilesAsync()).ToList();
        Assert.Equal(2, profiles.Count);
        double total = profiles.Sum(p => p.PercentageOfTotal);
        Assert.True(Math.Abs(total - 100.0) < 0.01, $"Sum of percentages should be 100 but was {total}");
    }

    // ── 4. Download / upload aggregation ────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfileAsync_CorrectDownloadUploadSplit()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            Timestamp = DateTime.UtcNow, ProcessName = "wget",
            Pid = 5, StartTimeTicks = 500,
            BytesDownloaded = 8000, BytesUploaded = 200, DataSource = "Nethogs"
        });

        var p = await svc.GetApplicationProfileAsync("wget", 5, 500);
        Assert.NotNull(p);
        Assert.Equal(8000, p.DownloadBytes);
        Assert.Equal(200, p.UploadBytes);
        Assert.Equal(8200, p.TotalBytes);
    }

    // ── 5. Daily aggregation via GetApplicationDailyUsageAsync ──────────────
    [Fact]
    public async Task GetApplicationDailyUsageAsync_MultipleRows_SumsPerDay()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        var today = DateTime.UtcNow.Date;
        for (int i = 0; i < 3; i++)
        {
            await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
            {
                Timestamp = today.AddHours(i), ProcessName = "ssh",
                Pid = 10, StartTimeTicks = 100,
                BytesDownloaded = 1000, BytesUploaded = 100, DataSource = "Nethogs"
            });
        }

        var points = (await svc.GetApplicationDailyUsageAsync("ssh", 10, 100, today, today.AddDays(1))).ToList();
        Assert.Single(points);
        Assert.Equal(3000, points[0].DownloadBytes);
        Assert.Equal(300, points[0].UploadBytes);
    }

    // ── 6. 7-day aggregation ─────────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfilesAsync_7DayTotal_CoversPast7Days()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        var today = DateTime.UtcNow.Date;
        // Day in 7-day window
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            Timestamp = today.AddDays(-3), ProcessName = "node",
            Pid = 20, StartTimeTicks = 200, BytesDownloaded = 5000, BytesUploaded = 500, DataSource = "Nethogs"
        });
        // Day outside window
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            Timestamp = today.AddDays(-8), ProcessName = "node",
            Pid = 20, StartTimeTicks = 200, BytesDownloaded = 9000, BytesUploaded = 900, DataSource = "Nethogs"
        });

        var profile = await svc.GetApplicationProfileAsync("node", 20, 200);
        Assert.NotNull(profile);
        Assert.Equal(5000 + 500, profile.SevenDayTotalBytes);
    }

    // ── 7. 30-day aggregation ────────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfilesAsync_30DayTotal_CoversPast30Days()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        var today = DateTime.UtcNow.Date;
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            Timestamp = today.AddDays(-20), ProcessName = "rsync",
            Pid = 30, StartTimeTicks = 300, BytesDownloaded = 4000, BytesUploaded = 400, DataSource = "Nethogs"
        });

        var profile = await svc.GetApplicationProfileAsync("rsync", 30, 300);
        Assert.NotNull(profile);
        Assert.Equal(4400, profile.ThirtyDayTotalBytes);
    }

    // ── 8. Insufficient historical data ─────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfileAsync_LessThan3ActiveDays_HasSufficientDataFalse()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        // Only 1 distinct day
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            Timestamp = DateTime.UtcNow.Date, ProcessName = "vim",
            Pid = 40, StartTimeTicks = 400, BytesDownloaded = 100, BytesUploaded = 10, DataSource = "Nethogs"
        });

        var profile = await svc.GetApplicationProfileAsync("vim", 40, 400);
        Assert.NotNull(profile);
        Assert.False(profile.HasSufficientData);
    }

    // ── 9. Peak-hour calculation ─────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationHourlyUsageAsync_IdentifiesPeakHour()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        var day = DateTime.UtcNow.Date;
        // Hour 14 has most traffic
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            Timestamp = day.AddHours(9), ProcessName = "apt",
            Pid = 50, StartTimeTicks = 500, BytesDownloaded = 100, BytesUploaded = 10, DataSource = "Nethogs"
        });
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            Timestamp = day.AddHours(14), ProcessName = "apt",
            Pid = 50, StartTimeTicks = 500, BytesDownloaded = 5000, BytesUploaded = 500, DataSource = "Nethogs"
        });

        var pattern = await svc.GetApplicationHourlyUsageAsync("apt", 50, 500, day);
        Assert.True(pattern.HasData);
        Assert.Equal(14, pattern.PeakHour);
        Assert.Equal(5500, pattern.PeakHourBytes);
    }

    // ── 10. Peak-day calculation ─────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfileAsync_IdentifiesPeakDay()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        var today = DateTime.UtcNow.Date;
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            Timestamp = today.AddDays(-2), ProcessName = "snap",
            Pid = 60, StartTimeTicks = 600, BytesDownloaded = 500, BytesUploaded = 50, DataSource = "Nethogs"
        });
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            Timestamp = today.AddDays(-1), ProcessName = "snap",
            Pid = 60, StartTimeTicks = 600, BytesDownloaded = 10000, BytesUploaded = 1000, DataSource = "Nethogs"
        });

        var profile = await svc.GetApplicationProfileAsync("snap", 60, 600);
        Assert.NotNull(profile);
        Assert.Equal(today.AddDays(-1), profile.PeakDay);
        Assert.Equal(11000, profile.PeakDayBytes);
    }

    // ── 11. Deterministic ranking ────────────────────────────────────────────
    [Fact]
    public async Task GetTopApplicationsAsync_OrderedByTotalBytesDescThenName()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            Timestamp = DateTime.UtcNow, ProcessName = "b-proc",
            Pid = 71, StartTimeTicks = 710, BytesDownloaded = 3000, BytesUploaded = 0, DataSource = "Nethogs"
        });
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            Timestamp = DateTime.UtcNow, ProcessName = "a-proc",
            Pid = 72, StartTimeTicks = 720, BytesDownloaded = 5000, BytesUploaded = 0, DataSource = "Nethogs"
        });

        var top = (await svc.GetTopApplicationsAsync(10)).ToList();
        Assert.Equal("a-proc", top[0].ProcessName); // 5000 > 3000
        Assert.Equal("b-proc", top[1].ProcessName);
    }

    // ── 12. Invalid telemetry exclusion ─────────────────────────────────────
    [Fact]
    public async Task Repository_RejectsNegativeBytes()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            Timestamp = DateTime.UtcNow, ProcessName = "bad-proc",
            Pid = 80, StartTimeTicks = 800, BytesDownloaded = -100, BytesUploaded = -50,
            DataSource = "Nethogs"
        });
        // Repo silently rejects; GetProcessUsageIdentities returns empty
        var svc = CreateService(ctx.Repository);
        var profiles = await svc.GetApplicationProfilesAsync(forceRefresh: true);
        Assert.DoesNotContain(profiles, p => p.ProcessName == "bad-proc");
    }

    // ── 13. Missing executable path ──────────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfileAsync_MissingExecPath_ExecutablePathIsEmpty()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            Timestamp = DateTime.UtcNow, ProcessName = "unknown-proc",
            Pid = 90, StartTimeTicks = 900, BytesDownloaded = 100, BytesUploaded = 10,
            ExecutablePath = "", DataSource = "Nethogs"
        });
        var p = await svc.GetApplicationProfileAsync("unknown-proc", 90, 900);
        Assert.NotNull(p);
        Assert.Equal(string.Empty, p.ExecutablePath);
    }

    // ── 14. Missing username ─────────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfileAsync_MissingUserName_UserNameIsEmpty()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            Timestamp = DateTime.UtcNow, ProcessName = "daemon-proc",
            Pid = 100, StartTimeTicks = 1000, BytesDownloaded = 200, BytesUploaded = 0,
            UserName = "", DataSource = "Nethogs"
        });
        var p = await svc.GetApplicationProfileAsync("daemon-proc", 100, 1000);
        Assert.NotNull(p);
        Assert.Equal(string.Empty, p.UserName);
    }

    // ── 15. Missing data source ──────────────────────────────────────────────
    [Fact]
    public async Task Repository_RejectsEmptyDataSource()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            Timestamp = DateTime.UtcNow, ProcessName = "no-source",
            Pid = 110, StartTimeTicks = 1100, BytesDownloaded = 300, BytesUploaded = 0,
            DataSource = ""   // rejected by repository validation
        });
        var svc = CreateService(ctx.Repository);
        var profiles = await svc.GetApplicationProfilesAsync(forceRefresh: true);
        Assert.DoesNotContain(profiles, p => p.ProcessName == "no-source");
    }

    // ── 16. PID identity safety ──────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfilesAsync_SamePidDifferentStartTicks_TreatedAsDifferentProcess()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        // PID 200 reused by two different processes
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            Timestamp = DateTime.UtcNow.AddDays(-1), ProcessName = "proc-a",
            Pid = 200, StartTimeTicks = 2001, BytesDownloaded = 1000, BytesUploaded = 0, DataSource = "Nethogs"
        });
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            Timestamp = DateTime.UtcNow, ProcessName = "proc-b",
            Pid = 200, StartTimeTicks = 2002, BytesDownloaded = 2000, BytesUploaded = 0, DataSource = "Nethogs"
        });

        var profiles = (await svc.GetApplicationProfilesAsync(forceRefresh: true)).ToList();
        Assert.Equal(2, profiles.Count);
        Assert.Contains(profiles, p => p.ProcessName == "proc-a" && p.StartTimeTicks == 2001);
        Assert.Contains(profiles, p => p.ProcessName == "proc-b" && p.StartTimeTicks == 2002);
    }

    // ── 17. Cache behaviour ──────────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfilesAsync_UsesCache_SecondCallReturnsSameReference()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            Timestamp = DateTime.UtcNow, ProcessName = "cached-app",
            Pid = 300, StartTimeTicks = 3000, BytesDownloaded = 500, BytesUploaded = 50, DataSource = "Nethogs"
        });

        var first  = (await svc.GetApplicationProfilesAsync()).ToList();
        var second = (await svc.GetApplicationProfilesAsync()).ToList();
        // Same data — cache should return identical results
        Assert.Equal(first.Count, second.Count);
        Assert.Equal(first[0].ProcessName, second[0].ProcessName);
    }

    // ── 17b. Cache invalidation ──────────────────────────────────────────────
    [Fact]
    public async Task InvalidateCacheAsync_ClearsProfileCache()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            Timestamp = DateTime.UtcNow, ProcessName = "pre-invalidate",
            Pid = 310, StartTimeTicks = 3100, BytesDownloaded = 100, BytesUploaded = 10, DataSource = "Nethogs"
        });

        var before = (await svc.GetApplicationProfilesAsync()).ToList();
        Assert.Single(before);

        await svc.InvalidateCacheAsync();

        // Add a second process after invalidation
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            Timestamp = DateTime.UtcNow, ProcessName = "post-invalidate",
            Pid = 311, StartTimeTicks = 3110, BytesDownloaded = 200, BytesUploaded = 20, DataSource = "Nethogs"
        });

        var after = (await svc.GetApplicationProfilesAsync()).ToList();
        Assert.Equal(2, after.Count);
    }

    // ── 18. Concurrent analytics calls ──────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfilesAsync_ConcurrentCalls_NoExceptions()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            Timestamp = DateTime.UtcNow, ProcessName = "concurrent-app",
            Pid = 400, StartTimeTicks = 4000, BytesDownloaded = 1000, BytesUploaded = 100, DataSource = "Nethogs"
        });

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => svc.GetApplicationProfilesAsync(forceRefresh: true));
        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.NotNull(r));
    }

    // ── Traffic breakdown ────────────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationTrafficBreakdownAsync_CorrectPercentages()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            Timestamp = DateTime.UtcNow, ProcessName = "dl-heavy",
            Pid = 500, StartTimeTicks = 5000, BytesDownloaded = 9000, BytesUploaded = 1000, DataSource = "Nethogs"
        });

        var bd = await svc.GetApplicationTrafficBreakdownAsync("dl-heavy", 500, 5000, AppAnalyticsPeriod.Today);
        Assert.Equal(9000, bd.DownloadBytes);
        Assert.Equal(1000, bd.UploadBytes);
        Assert.NotNull(bd.DownloadPercentage);
        Assert.NotNull(bd.UploadPercentage);
        Assert.Equal(90.0, bd.DownloadPercentage!.Value, 1);
        Assert.Equal(10.0, bd.UploadPercentage!.Value, 1);
    }

    // ── Zero-byte breakdown ──────────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationTrafficBreakdownAsync_ZeroTotal_PercentagesAreNull()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        // No records exist for this identity
        var bd = await svc.GetApplicationTrafficBreakdownAsync("ghost", 999, 9999, AppAnalyticsPeriod.Today);
        Assert.Null(bd.DownloadPercentage);
        Assert.Null(bd.UploadPercentage);
        Assert.Equal(0, bd.TotalBytes);
    }

    // ── Trend: insufficient data (no previous 7-day records) ────────────────
    [Fact]
    public async Task GetApplicationProfileAsync_NoPrev7DayData_TrendIsInsufficientData()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        // Only have records in the recent 7-day window, nothing in previous 7 days
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            Timestamp = DateTime.UtcNow.AddDays(-2), ProcessName = "newapp",
            Pid = 600, StartTimeTicks = 6000, BytesDownloaded = 2000, BytesUploaded = 200, DataSource = "Nethogs"
        });

        var p = await svc.GetApplicationProfileAsync("newapp", 600, 6000);
        Assert.NotNull(p);
        Assert.Equal("Insufficient Data", p.TrendState);
        Assert.Null(p.TrendPercentage);
    }

    // ── GetTopApplicationsAsync byDownload ───────────────────────────────────
    [Fact]
    public async Task GetTopApplicationsAsync_ByDownload_OrdersByDownloadBytes()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = CreateService(ctx.Repository);
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            Timestamp = DateTime.UtcNow, ProcessName = "low-dl",
            Pid = 701, StartTimeTicks = 7010, BytesDownloaded = 100, BytesUploaded = 9000, DataSource = "Nethogs"
        });
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            Timestamp = DateTime.UtcNow, ProcessName = "high-dl",
            Pid = 702, StartTimeTicks = 7020, BytesDownloaded = 8000, BytesUploaded = 100, DataSource = "Nethogs"
        });

        var top = (await svc.GetTopApplicationsAsync(5, byDownload: true)).ToList();
        Assert.Equal("high-dl", top[0].ProcessName);
    }
}
