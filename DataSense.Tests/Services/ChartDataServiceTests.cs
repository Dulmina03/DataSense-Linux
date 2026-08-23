using System;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Database;
using DataSense.Models;
using DataSense.Services;
using DataSense.Tests.Helpers;
using Xunit;

namespace DataSense.Tests.Services;

public class ChartDataServiceTests
{
    [Fact]
    public async Task GetTopProcessesAsync_EmptyDatabase_ReturnsEmpty()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analyticsService = new AnalyticsService(context.Repository);
        var chartDataService = new ChartDataService(analyticsService);

        var result = await chartDataService.GetTopProcessesAsync(AnalyticsPeriod.Today);
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetTopProcessesAsync_SingleProcess_ReturnsOneItemWithCorrectPercentages()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analyticsService = new AnalyticsService(context.Repository);
        var chartDataService = new ChartDataService(analyticsService);

        await TestDataBuilder.SeedProcessUsageAsync(context.Repository, "Chrome", DateTime.UtcNow, 1000, 500);

        var result = await chartDataService.GetTopProcessesAsync(AnalyticsPeriod.Today);
        var list = result.ToList();

        Assert.Single(list);
        Assert.Equal("Chrome", list[0].ProcessName);
        Assert.Equal(1000, list[0].DownloadBytes);
        Assert.Equal(500, list[0].UploadBytes);
        Assert.Equal(1500, list[0].TotalBytes);
        Assert.Equal(100.0, list[0].Percentage);
    }

    [Fact]
    public async Task GetTopProcessesAsync_MultipleProcesses_CalculatesOthersCorrectly()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analyticsService = new AnalyticsService(context.Repository);
        var chartDataService = new ChartDataService(analyticsService);

        for (int i = 1; i <= 6; i++)
        {
            await TestDataBuilder.SeedProcessUsageAsync(context.Repository, $"App{i}", DateTime.UtcNow, i * 100, i * 10);
        }

        var result = await chartDataService.GetTopProcessesAsync(AnalyticsPeriod.Today, 3);
        var list = result.ToList();

        Assert.Equal(4, list.Count);
        Assert.Equal("App6", list[0].ProcessName);
        Assert.Equal("App5", list[1].ProcessName);
        Assert.Equal("App4", list[2].ProcessName);
        Assert.Equal("Others", list[3].ProcessName);
        
        var expectedOthersDl = 100 + 200 + 300;
        var expectedOthersUl = 10 + 20 + 30;
        Assert.Equal(expectedOthersDl, list[3].DownloadBytes);
        Assert.Equal(expectedOthersUl, list[3].UploadBytes);
    }

    [Fact]
    public async Task GetDownloadUploadDonutAsync_EmptyDatabase_ReturnsZeroItem()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analyticsService = new AnalyticsService(context.Repository);
        var chartDataService = new ChartDataService(analyticsService);

        var result = await chartDataService.GetDownloadUploadDonutAsync(AnalyticsPeriod.ThisMonth);
        Assert.NotNull(result);
        Assert.Equal(0, result.TotalBytes);
    }

    [Fact]
    public async Task GetDownloadUploadDonutAsync_WithData_ReturnsCorrectTotals()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analyticsService = new AnalyticsService(context.Repository);
        var chartDataService = new ChartDataService(analyticsService);

        DateTime today = DateTime.UtcNow.Date;
        long mb = 1;
        await TestDataBuilder.SeedCumulativeUsageAsync(context.Repository, "wlan0", today.AddHours(2), TimeSpan.FromHours(1),
            (1000 * mb, 500 * mb), (2000 * mb, 2500 * mb)); // 1000 rx, 2000 tx

        var result = await chartDataService.GetDownloadUploadDonutAsync(AnalyticsPeriod.Today);
        Assert.NotNull(result);
        Assert.Equal(1000, result.DownloadBytes);
        Assert.Equal(2000, result.UploadBytes);
        Assert.Equal(3000, result.TotalBytes);
        Assert.Equal(100.0, result.Percentage);
    }

    [Fact]
    public async Task GetUsageTrendAsync_MissingDays_DoesNotFabricateData()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analyticsService = new AnalyticsService(context.Repository);
        var chartDataService = new ChartDataService(analyticsService);

        DateTime today = DateTime.UtcNow.Date;
        long mb = 1;
        
        await TestDataBuilder.SeedCumulativeUsageAsync(context.Repository, "wlan0", today.AddHours(2), TimeSpan.FromHours(1),
            (100 * mb, 50 * mb), (200 * mb, 100 * mb)); // 100 rx, 50 tx

        await TestDataBuilder.SeedCumulativeUsageAsync(context.Repository, "wlan0", today.AddDays(-3).AddHours(2), TimeSpan.FromHours(1),
            (200 * mb, 100 * mb), (400 * mb, 200 * mb)); // 200 rx, 100 tx

        var result = await chartDataService.GetUsageTrendAsync(AnalyticsPeriod.Last7Days);
        var list = result.ToList();

        Assert.Equal(2, list.Count);
        
        var earliest = list[0];
        Assert.Equal(today.AddDays(-3), earliest.Timestamp);
        Assert.Equal(200, earliest.DownloadBytes);

        var latest = list[1];
        Assert.Equal(today, latest.Timestamp);
        Assert.Equal(100, latest.DownloadBytes);
    }
}
