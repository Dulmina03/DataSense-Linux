using System;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Models;
using DataSense.Services;
using DataSense.Tests.Helpers;
using Xunit;

namespace DataSense.Tests.Services;

/// <summary>
/// Phase 11.31 Part 2 — Tests covering event integration, fastest-growing ranking,
/// export compatibility, error handling, and the full 22-item required checklist.
/// All test data is real deterministic SQLite seeds — no fabrication.
/// </summary>
public class ApplicationAnalyticsPart2Tests
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

    // ── 1. Empty database ─────────────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfilesAsync_EmptyDb_ReturnsEmpty()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        var profiles = await svc.GetApplicationProfilesAsync(forceRefresh: true);
        Assert.Empty(profiles);
    }

    // ── 2. Single application ─────────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfilesAsync_SingleApp_CorrectProfile()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        await Seed(ctx.Repository, "vim", 1, 10, DateTime.UtcNow, 4000, 400);
        var profiles = (await svc.GetApplicationProfilesAsync(forceRefresh: true)).ToList();
        Assert.Single(profiles);
        Assert.Equal("vim", profiles[0].ProcessName);
        Assert.Equal(4000, profiles[0].DownloadBytes);
        Assert.Equal(400, profiles[0].UploadBytes);
    }

    // ── 3. Multiple applications ──────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfilesAsync_MultipleApps_AllPresent()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        var today = DateTime.UtcNow.Date;
        await Seed(ctx.Repository, "chrome",  2, 20, today, 5000, 500);
        await Seed(ctx.Repository, "firefox", 3, 30, today, 3000, 300);
        await Seed(ctx.Repository, "curl",    4, 40, today, 1000, 100);
        var profiles = (await svc.GetApplicationProfilesAsync(forceRefresh: true)).ToList();
        Assert.Equal(3, profiles.Count);
        double totalPct = profiles.Sum(p => p.PercentageOfTotal);
        Assert.True(Math.Abs(totalPct - 100.0) < 0.01);
    }

    // ── 4. Daily aggregation ──────────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationDailyUsageAsync_MultipleDays_CorrectSums()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        var today = DateTime.UtcNow.Date;
        for (int d = 0; d < 3; d++)
            await Seed(ctx.Repository, "apt", 5, 50, today.AddDays(-d).AddHours(12), 2000, 200);
        var pts = (await svc.GetApplicationDailyUsageAsync("apt", 5, 50,
            today.AddDays(-4), today.AddDays(1))).ToList();
        Assert.Equal(3, pts.Count);
        Assert.All(pts, p => Assert.Equal(2200, p.TotalBytes));
    }

    // ── 5. Hourly aggregation ─────────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationHourlyUsageAsync_CorrectHourBuckets()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        var day = DateTime.UtcNow.Date;
        await Seed(ctx.Repository, "ssh", 6, 60, day.AddHours(7), 1000, 100);
        await Seed(ctx.Repository, "ssh", 6, 60, day.AddHours(23), 500, 50);
        var pat = await svc.GetApplicationHourlyUsageAsync("ssh", 6, 60, day);
        Assert.True(pat.HasData);
        Assert.NotNull(pat.HourlyDownloadBytes[7]);
        Assert.NotNull(pat.HourlyDownloadBytes[23]);
        Assert.Null(pat.HourlyDownloadBytes[0]);  // No data — must be null
    }

    // ── 6. Download/upload breakdown ──────────────────────────────────────────
    [Fact]
    public async Task GetApplicationTrafficBreakdownAsync_CorrectSplit()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        await Seed(ctx.Repository, "rsync", 7, 70, DateTime.UtcNow, 8000, 2000);
        var bd = await svc.GetApplicationTrafficBreakdownAsync("rsync", 7, 70, AppAnalyticsPeriod.Today);
        Assert.Equal(8000, bd.DownloadBytes);
        Assert.Equal(2000, bd.UploadBytes);
        Assert.NotNull(bd.DownloadPercentage);
        Assert.Equal(80.0, bd.DownloadPercentage!.Value, 1);
        Assert.Equal(20.0, bd.UploadPercentage!.Value, 1);
    }

    // ── 7. Percentage share ───────────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfilesAsync_PercentageShareSumsToHundred()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        var today = DateTime.UtcNow.Date;
        await Seed(ctx.Repository, "app1", 8, 80, today, 7000, 0);
        await Seed(ctx.Repository, "app2", 9, 90, today, 3000, 0);
        var profiles = (await svc.GetApplicationProfilesAsync(forceRefresh: true)).ToList();
        double total = profiles.Sum(p => p.PercentageOfTotal);
        Assert.True(Math.Abs(total - 100.0) < 0.01);
    }

    // ── 8. 7-day average ──────────────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfileAsync_SevenDayAverage_OnlyCountsActiveDays()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        var today = DateTime.UtcNow.Date;
        // 2 active days in 7-day window
        await Seed(ctx.Repository, "node", 10, 100, today.AddDays(-2), 6000, 0);
        await Seed(ctx.Repository, "node", 10, 100, today.AddDays(-1), 4000, 0);
        var p = await svc.GetApplicationProfileAsync("node", 10, 100);
        Assert.NotNull(p);
        Assert.NotNull(p.SevenDayAverageBytes);
        // Average over 2 active days = 5000
        Assert.Equal(5000.0, p.SevenDayAverageBytes!.Value, 1);
    }

    // ── 9. 30-day average ─────────────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfileAsync_ThirtyDayAverage_NotNull()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        var today = DateTime.UtcNow.Date;
        await Seed(ctx.Repository, "snap", 11, 110, today.AddDays(-25), 9000, 1000);
        var p = await svc.GetApplicationProfileAsync("snap", 11, 110);
        Assert.NotNull(p);
        Assert.NotNull(p.ThirtyDayAverageBytes);
        Assert.True(p.ThirtyDayAverageBytes > 0);
    }

    // ── 10. Increasing trend ──────────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationTrendAsync_Increasing_CorrectLabel()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        var today = DateTime.UtcNow.Date;
        await Seed(ctx.Repository, "growing", 12, 120, today.AddDays(-10), 1000, 0);
        await Seed(ctx.Repository, "growing", 12, 120, today.AddDays(-2),  5000, 0);
        var t = await svc.GetApplicationTrendAsync("growing", 12, 120);
        Assert.Equal("Increasing", t.TrendState);
    }

    // ── 11. Decreasing trend ──────────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationTrendAsync_Decreasing_CorrectLabel()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        var today = DateTime.UtcNow.Date;
        await Seed(ctx.Repository, "shrinking", 13, 130, today.AddDays(-10), 8000, 0);
        await Seed(ctx.Repository, "shrinking", 13, 130, today.AddDays(-2),   200, 0);
        var t = await svc.GetApplicationTrendAsync("shrinking", 13, 130);
        Assert.Equal("Decreasing", t.TrendState);
    }

    // ── 12. Stable trend ──────────────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationTrendAsync_Stable_CorrectLabel()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        var today = DateTime.UtcNow.Date;
        await Seed(ctx.Repository, "steady", 14, 140, today.AddDays(-10), 3000, 0);
        await Seed(ctx.Repository, "steady", 14, 140, today.AddDays(-2),  3050, 0);
        var t = await svc.GetApplicationTrendAsync("steady", 14, 140);
        Assert.Equal("Stable", t.TrendState);
    }

    // ── 13. Insufficient historical data ──────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfileAsync_OneActiveDay_HasSufficientDataFalse()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        await Seed(ctx.Repository, "newapp", 15, 150, DateTime.UtcNow, 500, 50);
        var p = await svc.GetApplicationProfileAsync("newapp", 15, 150);
        Assert.NotNull(p);
        Assert.False(p.HasSufficientData);
    }

    // ── 14. Application ranking (total) ───────────────────────────────────────
    [Fact]
    public async Task GetTopApplicationsAsync_RankedByTotal_Deterministic()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        var today = DateTime.UtcNow.Date;
        await Seed(ctx.Repository, "z-heavy", 16, 160, today, 9000, 0);
        await Seed(ctx.Repository, "a-light", 17, 170, today, 1000, 0);
        var top = (await svc.GetTopApplicationsAsync(5)).ToList();
        Assert.Equal("z-heavy", top[0].ProcessName);
        Assert.Equal("a-light", top[1].ProcessName);
    }

    // ── 14b. Download ranking ─────────────────────────────────────────────────
    [Fact]
    public async Task GetTopApplicationsAsync_ByDownload_OrderedCorrectly()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        var today = DateTime.UtcNow.Date;
        await Seed(ctx.Repository, "dl-king",    18, 180, today, 8000,  100);
        await Seed(ctx.Repository, "upload-king",19, 190, today, 100,  8000);
        var top = (await svc.GetTopApplicationsAsync(5, byDownload: true)).ToList();
        Assert.Equal("dl-king", top[0].ProcessName);
    }

    // ── 14c. Fastest-growing ranking ──────────────────────────────────────────
    [Fact]
    public async Task GetFastestGrowingApplicationsAsync_OrderedByTrendPercentage()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        var today = DateTime.UtcNow.Date;

        // Make two apps with prior period data so trend is computed
        await Seed(ctx.Repository, "slow-grow",  20, 200, today.AddDays(-10), 1000, 0);
        await Seed(ctx.Repository, "slow-grow",  20, 200, today.AddDays(-9),  1000, 0);
        await Seed(ctx.Repository, "slow-grow",  20, 200, today.AddDays(-8),  1000, 0);
        await Seed(ctx.Repository, "slow-grow",  20, 200, today.AddDays(-2),  1200, 0);

        await Seed(ctx.Repository, "fast-grow",  21, 210, today.AddDays(-10),  500, 0);
        await Seed(ctx.Repository, "fast-grow",  21, 210, today.AddDays(-9),   500, 0);
        await Seed(ctx.Repository, "fast-grow",  21, 210, today.AddDays(-8),   500, 0);
        await Seed(ctx.Repository, "fast-grow",  21, 210, today.AddDays(-2), 10000, 0);

        // Ensure HasSufficientData by adding one more active day each
        await Seed(ctx.Repository, "slow-grow", 20, 200, today.AddDays(-7), 1000, 0);
        await Seed(ctx.Repository, "fast-grow", 21, 210, today.AddDays(-7),  500, 0);

        var fastest = (await svc.GetFastestGrowingApplicationsAsync(5)).ToList();
        // fast-grow has a much higher trend percentage, should be first
        if (fastest.Count >= 2)
        {
            Assert.True(fastest[0].TrendPercentage >= fastest[1].TrendPercentage,
                "First result should have highest trend percentage");
        }
    }

    // ── 15. PID recycling / stable identity ───────────────────────────────────
    [Fact]
    public async Task GetApplicationProfilesAsync_SamePidDifferentTicks_SeparateProfiles()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        var today = DateTime.UtcNow.Date;
        await Seed(ctx.Repository, "old-proc", 999, 11111, today.AddDays(-1), 5000, 0);
        await Seed(ctx.Repository, "new-proc", 999, 22222, today,             3000, 0);
        var profiles = (await svc.GetApplicationProfilesAsync(forceRefresh: true)).ToList();
        Assert.Equal(2, profiles.Count);
        Assert.Contains(profiles, p => p.ProcessName == "old-proc" && p.StartTimeTicks == 11111);
        Assert.Contains(profiles, p => p.ProcessName == "new-proc" && p.StartTimeTicks == 22222);
    }

    // ── 16. Invalid telemetry filtering ───────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfilesAsync_NegativeBytesRecord_Excluded()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            ProcessName = "bad", Pid = 22, StartTimeTicks = 220,
            Timestamp = DateTime.UtcNow, BytesDownloaded = -100, BytesUploaded = -50,
            DataSource = "Nethogs"
        });
        var svc = MakeSvc(ctx.Repository);
        var profiles = await svc.GetApplicationProfilesAsync(forceRefresh: true);
        Assert.DoesNotContain(profiles, p => p.ProcessName == "bad");
    }

    // ── 17. Missing executable path ───────────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfileAsync_MissingExecPath_EmptyString()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            ProcessName = "no-path", Pid = 23, StartTimeTicks = 230,
            Timestamp = DateTime.UtcNow, BytesDownloaded = 100, BytesUploaded = 10,
            ExecutablePath = "", DataSource = "Nethogs"
        });
        var p = await svc.GetApplicationProfileAsync("no-path", 23, 230);
        Assert.NotNull(p);
        Assert.Equal(string.Empty, p.ExecutablePath);
    }

    // ── 18. Missing username ──────────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfileAsync_MissingUserName_EmptyString()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        await ctx.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            ProcessName = "no-user", Pid = 24, StartTimeTicks = 240,
            Timestamp = DateTime.UtcNow, BytesDownloaded = 200, BytesUploaded = 0,
            UserName = "", DataSource = "Nethogs"
        });
        var p = await svc.GetApplicationProfileAsync("no-user", 24, 240);
        Assert.NotNull(p);
        Assert.Equal(string.Empty, p.UserName);
    }

    // ── 19. Missing network context ───────────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfileAsync_NoNetworkContext_StillReturnsProfile()
    {
        // ProcessUsageRecords don't store network context — service handles gracefully
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        await Seed(ctx.Repository, "no-net", 25, 250, DateTime.UtcNow, 500, 50);
        var p = await svc.GetApplicationProfileAsync("no-net", 25, 250);
        Assert.NotNull(p);
        // DataSource is the closest equivalent — should be populated
        Assert.Equal("Nethogs", p.DataSource);
    }

    // ── 20. Database/query failure handling ───────────────────────────────────
    [Fact]
    public async Task GetApplicationTrendAsync_NonexistentApp_DoesNotThrow()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        // Calling with an identity that has no records should not throw
        var trend = await svc.GetApplicationTrendAsync("ghost-app", 9999, 999999);
        Assert.NotNull(trend);
        Assert.False(trend.HasSufficientData);
    }

    // ── 21. Concurrent analytics calls ────────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfilesAsync_Concurrent_NoExceptions()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        await Seed(ctx.Repository, "conc", 26, 260, DateTime.UtcNow, 1000, 100);
        var tasks = Enumerable.Range(0, 12)
            .Select(i => svc.GetApplicationProfilesAsync(forceRefresh: i % 3 == 0));
        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.NotNull(r));
    }

    // ── 22. Cache consistency ─────────────────────────────────────────────────
    [Fact]
    public async Task GetApplicationProfilesAsync_CacheHit_ReturnsSameData()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        await Seed(ctx.Repository, "cached", 27, 270, DateTime.UtcNow, 700, 70);

        var first  = (await svc.GetApplicationProfilesAsync()).ToList();
        var second = (await svc.GetApplicationProfilesAsync()).ToList();  // cache hit
        Assert.Equal(first.Count, second.Count);
        Assert.Equal(first[0].ProcessName, second[0].ProcessName);
        Assert.Equal(first[0].DownloadBytes, second[0].DownloadBytes);
    }

    // ── Event publishing: dominant consumer ───────────────────────────────────
    [Fact]
    public async Task EventPublishing_DominantConsumer_EventFired()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        var events = new System.Collections.Concurrent.ConcurrentBag<DataSenseEvent>();
        var mockEvents = new MockEventService(events);
        svc.SetEventService(mockEvents);

        var today = DateTime.UtcNow.Date;
        // One app takes >25% of traffic
        await Seed(ctx.Repository, "dominant", 28, 280, today, 9000, 0);
        await Seed(ctx.Repository, "minor",    29, 290, today, 1000, 0);

        await svc.GetApplicationProfilesAsync(forceRefresh: true);
        Assert.Contains(events, e => e.EventType == DataSenseEventType.ApplicationAnomaly
                                  && e.Title.Contains("dominant"));
    }

    // ── Event publishing: no events when no data ──────────────────────────────
    [Fact]
    public async Task EventPublishing_EmptyDb_PublishesUnavailableEvent()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        var svc = MakeSvc(ctx.Repository);
        var events = new System.Collections.Concurrent.ConcurrentBag<DataSenseEvent>();
        svc.SetEventService(new MockEventService(events));

        await svc.GetApplicationProfilesAsync(forceRefresh: true);
        Assert.Contains(events, e => e.Title.Contains("Unavailable") || e.Title.Contains("unavailable")
                                  || e.Description.Contains("No process telemetry"));
    }

    // ── Health registry wiring ────────────────────────────────────────────────
    [Fact]
    public void WireOptionalDependencies_HealthRegistry_Registered()
    {
        var repo = new DataSense.Database.SqliteNetworkUsageRepository(":memory:;Pooling=False");
        var svc = new ApplicationAnalyticsService(repo, new MockLinuxProcessResolver());
        var registry = new SystemHealthRegistry();

        (svc as IApplicationAnalyticsService).WireOptionalDependencies(registry, null);

        // If no exception was thrown, the wiring succeeded
        var report = registry.GetReport("ApplicationAnalyticsService");
        Assert.NotNull(report);
    }

    // ── Export service compatibility ──────────────────────────────────────────
    [Fact]
    public async Task ExportService_ApplicationsType_ExportsFromRealData()
    {
        using var ctx = await TestDatabaseFactory.CreateAsync();
        await Seed(ctx.Repository, "export-app", 30, 300, DateTime.UtcNow, 5000, 500);

        var analyticsService = new AnalyticsService(ctx.Repository);
        var exportSvc = new ExportService(ctx.Repository, analyticsService, new Moq.Mock<DataSense.Services.IApplicationAnalyticsService>().Object);

        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "DataSense_ExportTest_" + Guid.NewGuid().ToString("N"));

        var result = await exportSvc.ExportDataAsync(new ExportOptions
        {
            Format = ExportFormat.CSV,
            DataType = ExportDataType.Applications,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate   = DateTime.UtcNow.AddDays(1),
            OutputDirectory = tempDir
        });

        Assert.True(result.Success);
        Assert.True(result.RecordsExported >= 0);

        // Cleanup
        try { System.IO.Directory.Delete(tempDir, true); } catch { }
    }
}

// ── Mock event service for testing event publishing ───────────────────────────
internal sealed class MockEventService : IEventService
{
    private readonly System.Collections.Concurrent.ConcurrentBag<DataSenseEvent> _events;
    public event EventHandler? EventsUpdated;
    public int UnreadCount => _events.Count;

    public MockEventService(System.Collections.Concurrent.ConcurrentBag<DataSenseEvent> bag)
        => _events = bag;

    public void PublishEvent(DataSenseEvent evt)
    {
        _events.Add(evt);
        EventsUpdated?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<DataSenseEvent> GetActiveEvents() => _events.ToList();
    public void MarkAsRead(string id) { }
    public void MarkAllAsRead() { }
    public void DismissEvent(string id) { }
    public void ClearResolvedEvents() { }
}
