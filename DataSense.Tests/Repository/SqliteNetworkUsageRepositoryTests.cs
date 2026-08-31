using System;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Models;
using DataSense.Tests.Helpers;
using Xunit;

namespace DataSense.Tests.Repository;

public class SqliteNetworkUsageRepositoryTests
{
    [Fact]
    public async Task InitializeAsync_CreatesSchemaAndAllowsQueries()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var interfaces = await context.Repository.GetInterfaceNamesAsync();
        Assert.Empty(interfaces);
    }

    [Fact]
    public async Task GetTodaySummaryAsync_WithCumulativeCounters_CalculatesMaxMinusMin()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        string iface = "wlan0";
        DateTime todayUtc = DateTime.UtcNow.Date.AddHours(1);

        // Cumulative download values: 100 MB, 150 MB, 220 MB, 300 MB
        long mb = 1024 * 1024;
        await TestDataBuilder.SeedCumulativeUsageAsync(
            context.Repository,
            iface,
            todayUtc,
            TimeSpan.FromHours(1),
            (100 * mb, 50 * mb),
            (150 * mb, 70 * mb),
            (220 * mb, 100 * mb),
            (300 * mb, 150 * mb)
        );

        var (downloaded, uploaded) = await context.Repository.GetTodaySummaryAsync(iface);

        // Expected downloaded: 300 MB - 100 MB = 200 MB
        Assert.Equal(200 * mb, downloaded);
        // Expected uploaded: 150 MB - 50 MB = 100 MB
        Assert.Equal(100 * mb, uploaded);
    }

    [Fact]
    public async Task GetTodaySummaryAsync_WhenEmpty_ReturnsZero()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var (downloaded, uploaded) = await context.Repository.GetTodaySummaryAsync();
        Assert.Equal(0, downloaded);
        Assert.Equal(0, uploaded);
    }

    [Fact]
    public async Task GetTodaySummaryAsync_AggregatesUsageRecordsAccurately()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        string iface = "eth0";
        DateTime todayUtc = DateTime.UtcNow.Date.AddHours(2);

        // Seed continuous usage records for today (100MB rx delta, 50MB tx delta)
        long mb = 1024 * 1024;
        await TestDataBuilder.SeedCumulativeUsageAsync(
            context.Repository,
            iface,
            todayUtc,
            TimeSpan.FromMinutes(10),
            (50 * mb, 10 * mb),
            (150 * mb, 60 * mb)
        );

        var (downloaded, uploaded) = await context.Repository.GetTodaySummaryAsync(iface);

        // Should return canonical usage records (100MB rx, 50MB tx)
        Assert.Equal(100 * mb, downloaded);
        Assert.Equal(50 * mb, uploaded);
    }

    [Fact]
    public async Task GetDailyUsageAsync_AggregatesPerDayCorrectly()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        string iface = "eth0";
        long mb = 1024 * 1024;
        DateTime startDay = DateTime.UtcNow.Date.AddDays(-2);

        // Day 1
        await TestDataBuilder.SeedCumulativeUsageAsync(
            context.Repository,
            iface,
            startDay.AddHours(2),
            TimeSpan.FromHours(2),
            (100 * mb, 10 * mb),
            (250 * mb, 60 * mb) // Day 1 usage = 150MB rx, 50MB tx
        );

        // Day 2
        await TestDataBuilder.SeedCumulativeUsageAsync(
            context.Repository,
            iface,
            startDay.AddDays(1).AddHours(2),
            TimeSpan.FromHours(2),
            (300 * mb, 70 * mb),
            (600 * mb, 170 * mb) // Day 2 usage = 300MB rx, 100MB tx
        );

        var daily = (await context.Repository.GetDailyUsageAsync(startDay, DateTime.UtcNow)).ToList();
        Assert.NotNull(daily);
        Assert.True(daily.Count >= 2);

        var day1 = daily.FirstOrDefault(d => d.Day.Date == startDay.Date);
        Assert.NotNull(day1);
        Assert.Equal(150 * mb, day1.BytesDownloaded);
        Assert.Equal(50 * mb, day1.BytesUploaded);

        var day2 = daily.FirstOrDefault(d => d.Day.Date == startDay.AddDays(1).Date);
        Assert.NotNull(day2);
        Assert.Equal(300 * mb, day2.BytesDownloaded);
        Assert.Equal(100 * mb, day2.BytesUploaded);
    }

    [Fact]
    public async Task Sessions_SaveAndRetrieve_Succeeds()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        DateTime now = DateTime.UtcNow;

        await TestDataBuilder.SeedSessionAsync(context.Repository, "HomeWiFi", "wlan0", now.AddHours(-2), TimeSpan.FromHours(1), 500000, 100000);

        var sessions = (await context.Repository.GetSessionsAsync(now.AddDays(-1), now.AddDays(1))).ToList();
        Assert.Single(sessions);
        Assert.Equal("HomeWiFi", sessions[0].NetworkName);
        Assert.Equal(500000, sessions[0].BytesDownloaded);
    }

    [Fact]
    public async Task SpeedTests_SaveAndRetrieve_Succeeds()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        DateTime now = DateTime.UtcNow;

        await TestDataBuilder.SeedSpeedTestAsync(context.Repository, "OfficeNet", 100.5, 45.2, 12.0, now);

        var tests = (await context.Repository.GetSpeedTestsAsync(10, "OfficeNet")).ToList();
        Assert.Single(tests);
        Assert.Equal("OfficeNet", tests[0].NetworkName);
        Assert.Equal(100.5, tests[0].DownloadSpeedMbps);
    }

    [Fact]
    public async Task ProcessUsage_SaveAndRetrieveTop_Succeeds()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        DateTime now = DateTime.UtcNow;

        await TestDataBuilder.SeedProcessUsageAsync(context.Repository, "chrome", now, 1000000, 200000);
        await TestDataBuilder.SeedProcessUsageAsync(context.Repository, "firefox", now, 500000, 50000);

        var top = (await context.Repository.GetTopProcessesAsync(now.AddHours(-1), now.AddHours(1), 5)).ToList();
        Assert.Equal(2, top.Count);
        Assert.Equal("chrome", top[0].ProcessName);
    }

    [Fact]
    public async Task GetAvailableNetworksAsync_FiltersPlaceholdersAndDeduplicates()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        DateTime now = DateTime.UtcNow;

        await TestDataBuilder.SeedSessionAsync(context.Repository, "uom.wireless", "wlan0", now.AddHours(-3), TimeSpan.FromHours(1), 500000, 100000);
        await TestDataBuilder.SeedSessionAsync(context.Repository, "-", "wlan0", now.AddHours(-2), TimeSpan.FromHours(1), 200000, 50000);
        await TestDataBuilder.SeedSessionAsync(context.Repository, "uom.wireless", "wlan0", now.AddHours(-1), TimeSpan.FromHours(1), 300000, 80000);
        await TestDataBuilder.SeedSessionAsync(context.Repository, "SLT Fiber", "eth0", now.AddHours(-1), TimeSpan.FromHours(1), 1000000, 200000);

        var networks = (await context.Repository.GetAvailableNetworksAsync()).ToList();

        Assert.Equal(2, networks.Count);
        Assert.Contains("uom.wireless", networks);
        Assert.Contains("SLT Fiber", networks);
        Assert.DoesNotContain("-", networks);
    }
}
