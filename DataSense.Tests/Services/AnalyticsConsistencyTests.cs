using System;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Helpers;
using DataSense.Models;
using DataSense.Tests.Helpers;
using Xunit;

namespace DataSense.Tests.Services;

public class AnalyticsConsistencyTests
{
    private static (DateTime start, DateTime end) GetUtcTodayRange()
    {
        var utcNow = DateTime.UtcNow;
        var start = DateTime.SpecifyKind(utcNow.Date, DateTimeKind.Utc);
        var end = start.AddDays(1).AddTicks(-1);
        return (start, end);
    }

    [Fact]
    public async Task TodayDownloadPlusUpload_EqualsTodayTotal()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        string iface = "eth0";
        var (start, end) = GetUtcTodayRange();

        long mb = 1024 * 1024;
        await TestDataBuilder.SeedCumulativeUsageAsync(
            context.Repository,
            iface,
            start.AddHours(2),
            TimeSpan.FromMinutes(10),
            (100 * mb, 40 * mb),
            (250 * mb, 90 * mb)
        );

        var (dl, ul) = await context.Repository.GetTodaySummaryAsync(iface);
        long total = dl + ul;

        Assert.Equal(150 * mb, dl);
        Assert.Equal(50 * mb, ul);
        Assert.Equal(200 * mb, total);
        Assert.Equal(dl + ul, total);
    }

    [Fact]
    public async Task ActiveOpenSession_IsIncludedInTodaySummaryAndSessions()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        string iface = "wlan0";
        var (start, end) = GetUtcTodayRange();

        long mb = 1024 * 1024;
        var activeSession = new NetworkSession
        {
            InterfaceName = iface,
            ConnectionType = "WiFi",
            NetworkName = "TestWifi",
            StartTime = start.AddHours(1),
            EndTime = null, // Active open session
            BytesDownloaded = 80 * mb,
            BytesUploaded = 20 * mb
        };
        await context.Repository.SaveSessionAsync(activeSession);

        var (dl, ul) = await context.Repository.GetTodaySummaryAsync(iface);
        var sessions = (await context.Repository.GetSessionsAsync(start, end, iface)).ToList();

        Assert.Equal(80 * mb, dl);
        Assert.Equal(20 * mb, ul);
        Assert.Single(sessions);
        Assert.Equal(activeSession.StartTime.ToUniversalTime(), sessions[0].StartTime.ToUniversalTime());
    }

    [Fact]
    public async Task ZeroTrafficCase_HandlesWithoutNaNOrDivideByZero()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var (dl, ul) = await context.Repository.GetTodaySummaryAsync("eth0");
        long total = dl + ul;
        double dlPct = total > 0 ? (double)dl / total * 100.0 : 0.0;
        double ulPct = total > 0 ? (double)ul / total * 100.0 : 0.0;

        Assert.Equal(0, dl);
        Assert.Equal(0, ul);
        Assert.Equal(0, total);
        Assert.Equal(0.0, dlPct);
        Assert.Equal(0.0, ulPct);
        Assert.False(double.IsNaN(dlPct));
        Assert.False(double.IsNaN(ulPct));
    }

    [Fact]
    public void LocalMidnight_CorrectlySeparatesDates()
    {
        var (startUtc, endUtc) = DateRangeHelper.GetLocalTodayRange();

        Assert.Equal(DateTimeKind.Utc, startUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, endUtc.Kind);
        Assert.True(endUtc > startUtc);
        Assert.Equal(24 * 3600, (int)Math.Round((endUtc.AddTicks(1) - startUtc).TotalSeconds));
    }

    [Fact]
    public async Task NetworkSwitching_PreservesSessionsWithoutDoubleCounting()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var (start, end) = GetUtcTodayRange();
        long mb = 1024 * 1024;

        // Session 1: WiFi
        var wifiSession = new NetworkSession
        {
            InterfaceName = "wlan0",
            ConnectionType = "WiFi",
            NetworkName = "HomeWifi",
            StartTime = start.AddHours(1),
            EndTime = start.AddHours(2),
            BytesDownloaded = 50 * mb,
            BytesUploaded = 10 * mb
        };
        await context.Repository.SaveSessionAsync(wifiSession);

        // Session 2: Ethernet
        var ethSession = new NetworkSession
        {
            InterfaceName = "eth0",
            ConnectionType = "Ethernet",
            NetworkName = "Wired",
            StartTime = start.AddHours(2),
            EndTime = null, // active
            BytesDownloaded = 100 * mb,
            BytesUploaded = 30 * mb
        };
        await context.Repository.SaveSessionAsync(ethSession);

        var (todayDl, todayUl) = await context.Repository.GetTodaySummaryAsync(null);

        Assert.Equal(150 * mb, todayDl);
        Assert.Equal(40 * mb, todayUl);
        Assert.Equal(190 * mb, todayDl + todayUl);
    }

    [Fact]
    public async Task TodayProcessAggregation_UsesTodayBytes_NotLifetimeAllTimeBytes()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var (start, end) = GetUtcTodayRange();
        long mb = 1024 * 1024;

        // Seed lifetime record for process from 10 days ago (50 GB)
        var pastRecord = new ProcessUsageRecord
        {
            ProcessName = "language_server",
            Pid = 1234,
            StartTimeTicks = start.AddDays(-10).Ticks,
            BytesDownloaded = 50000 * mb,
            BytesUploaded = 7000 * mb,
            ExecutablePath = "/usr/bin/language_server",
            UserName = "user",
            DataSource = "Nethogs",
            Timestamp = start.AddDays(-10)
        };
        await context.Repository.SaveProcessUsageAsync(pastRecord);

        // Seed today record for process (50 MB)
        var todayRecord = new ProcessUsageRecord
        {
            ProcessName = "language_server",
            Pid = 1234,
            StartTimeTicks = start.AddHours(1).Ticks,
            BytesDownloaded = 40 * mb,
            BytesUploaded = 10 * mb,
            ExecutablePath = "/usr/bin/language_server",
            UserName = "user",
            DataSource = "Nethogs",
            Timestamp = start.AddHours(1)
        };
        await context.Repository.SaveProcessUsageAsync(todayRecord);

        var appAnalyticsSvc = new DataSense.Services.ApplicationAnalyticsService(context.Repository, new DataSense.Services.LinuxProcessResolver());
        var profiles = (await appAnalyticsSvc.GetApplicationProfilesAsync(forceRefresh: true)).ToList();
        var profile = profiles.FirstOrDefault(p => p.ProcessName == "language_server" && p.TodayBytes > 0);

        Assert.NotNull(profile);
        Assert.Equal(50 * mb, profile.TodayBytes);
        Assert.Equal(40 * mb, profile.TodayDownloadBytes);
        Assert.Equal(10 * mb, profile.TodayUploadBytes);
        // Lifetime bytes (57 GB) must not leak into TodayBytes
        Assert.True(profile.TodayBytes < 100 * mb);
    }
}
