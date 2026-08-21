using System;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Models;
using DataSense.Services;
using DataSense.Tests.Helpers;
using Xunit;

namespace DataSense.Tests.Services;

public class AnalyticsServiceTests
{
    [Fact]
    public async Task GetSummaryAsync_WithEmptyRepository_ReturnsZeroesAndNullPeaks()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var service = new AnalyticsService(context.Repository);

        var summary = await service.GetSummaryAsync(AnalyticsPeriod.Last7Days);

        Assert.NotNull(summary);
        Assert.Equal(0, summary.TotalDownloaded);
        Assert.Equal(0, summary.TotalUploaded);
        Assert.Equal(0, summary.AvgDailyBytes);
        Assert.Null(summary.PeakDay);
        Assert.Null(summary.PeakHourToday);
    }

    [Fact]
    public async Task GetSummaryAsync_WithDeterministicData_CalculatesAverageAndPeakCorrectly()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var service = new AnalyticsService(context.Repository);

        long mb = 1024 * 1024;
        DateTime today = DateTime.UtcNow.Date;

        // Day 1 (2 days ago): 100 MB total
        await TestDataBuilder.SeedCumulativeUsageAsync(context.Repository, "wlan0", today.AddDays(-2).AddHours(2), TimeSpan.FromHours(1),
            (10 * mb, 5 * mb), (75 * mb, 40 * mb)); // 65MB rx, 35MB tx = 100MB

        // Day 2 (1 day ago): 200 MB total
        await TestDataBuilder.SeedCumulativeUsageAsync(context.Repository, "wlan0", today.AddDays(-1).AddHours(2), TimeSpan.FromHours(1),
            (100 * mb, 50 * mb), (230 * mb, 120 * mb)); // 130MB rx, 70MB tx = 200MB

        // Day 3 (today): 300 MB total
        await TestDataBuilder.SeedCumulativeUsageAsync(context.Repository, "wlan0", today.AddHours(2), TimeSpan.FromHours(1),
            (300 * mb, 150 * mb), (500 * mb, 250 * mb)); // 200MB rx, 100MB tx = 300MB

        var summary = await service.GetSummaryAsync(AnalyticsPeriod.Last7Days);

        Assert.Equal(600 * mb, summary.TotalDownloaded + summary.TotalUploaded);
        Assert.Equal(200 * mb, summary.AvgDailyBytes); // (100 + 200 + 300) / 3 = 200 MB
        Assert.NotNull(summary.PeakDay);
        Assert.Equal(today, summary.PeakDay.Day.Date);
        Assert.Equal(300 * mb, summary.PeakDay.TotalBytes);
    }

    [Fact]
    public async Task GetDailySeriesAsync_ReturnsChronologicalOrder()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var service = new AnalyticsService(context.Repository);

        long mb = 1024 * 1024;
        DateTime today = DateTime.UtcNow.Date;

        await TestDataBuilder.SeedCumulativeUsageAsync(context.Repository, "wlan0", today.AddDays(-2).AddHours(1), TimeSpan.FromHours(1), (10 * mb, 5 * mb), (50 * mb, 20 * mb));
        await TestDataBuilder.SeedCumulativeUsageAsync(context.Repository, "wlan0", today.AddDays(-1).AddHours(1), TimeSpan.FromHours(1), (60 * mb, 25 * mb), (120 * mb, 50 * mb));

        var series = await service.GetDailySeriesAsync(AnalyticsPeriod.Last7Days);

        Assert.NotNull(series);
        if (series.Count >= 2)
        {
            Assert.True(series[0].Day <= series[1].Day);
        }
    }

    [Fact]
    public async Task ProcessAnalytics_AggregatesSpecificProcessData()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var service = new AnalyticsService(context.Repository);

        DateTime now = DateTime.UtcNow;
        await TestDataBuilder.SeedProcessUsageAsync(context.Repository, "firefox", now, 500000, 100000);
        await TestDataBuilder.SeedProcessUsageAsync(context.Repository, "chrome", now, 2000000, 400000);

        var firefoxSummary = await service.GetProcessSummaryAsync("firefox", AnalyticsPeriod.Last7Days);
        Assert.Equal(500000, firefoxSummary.TotalDownloaded);
        Assert.Equal(100000, firefoxSummary.TotalUploaded);

        var topConsumers = (await service.GetTopDataConsumersAsync(AnalyticsPeriod.Last7Days, 5)).ToList();
        Assert.NotEmpty(topConsumers);
        Assert.Equal("chrome", topConsumers[0].ProcessName);
    }
}
