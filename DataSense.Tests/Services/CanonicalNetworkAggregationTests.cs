using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Database;
using DataSense.Helpers;
using DataSense.Models;
using DataSense.Services;
using DataSense.Tests.Helpers;
using Xunit;

namespace DataSense.Tests.Services;

public class CanonicalNetworkAggregationTests
{
    private const long MB = 1024 * 1024;
    private const long GB = 1024 * 1024 * 1024;

    [Fact]
    public async Task Requirement1_NormalCounterIncrements_SumDeltasAccurately()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var (todayStart, _) = DateRangeHelper.GetLocalTodayRange();

        // 3 consecutive records with 50MB and 20MB deltas each
        await context.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = todayStart.AddHours(1),
            InterfaceName = "wlo1",
            BytesReceived = 100 * MB,
            BytesSent = 50 * MB,
            DownloadDelta = 0,
            UploadDelta = 0
        });

        await context.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = todayStart.AddHours(2),
            InterfaceName = "wlo1",
            BytesReceived = 150 * MB,
            BytesSent = 70 * MB,
            DownloadDelta = 50 * MB,
            UploadDelta = 20 * MB
        });

        await context.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = todayStart.AddHours(3),
            InterfaceName = "wlo1",
            BytesReceived = 200 * MB,
            BytesSent = 90 * MB,
            DownloadDelta = 50 * MB,
            UploadDelta = 20 * MB
        });

        var (todayDl, todayUl) = await context.Repository.GetTodaySummaryAsync("wlo1");
        Assert.Equal(100 * MB, todayDl);
        Assert.Equal(40 * MB, todayUl);
    }

    [Fact]
    public async Task Requirement2_CounterReset_HandledWithoutLossOrSpike()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var (todayStart, _) = DateRangeHelper.GetLocalTodayRange();

        // Before reset: counter reached 500MB
        await context.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = todayStart.AddHours(1),
            InterfaceName = "wlo1",
            BytesReceived = 500 * MB,
            BytesSent = 200 * MB,
            DownloadDelta = 100 * MB,
            UploadDelta = 50 * MB
        });

        // Counter reset (e.g. system reboot or interface restart): counter drops to 20MB
        await context.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = todayStart.AddHours(2),
            InterfaceName = "wlo1",
            BytesReceived = 20 * MB,
            BytesSent = 10 * MB,
            DownloadDelta = 20 * MB,
            UploadDelta = 10 * MB
        });

        // Next increment: counter reaches 50MB (delta 30MB)
        await context.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = todayStart.AddHours(3),
            InterfaceName = "wlo1",
            BytesReceived = 50 * MB,
            BytesSent = 25 * MB,
            DownloadDelta = 30 * MB,
            UploadDelta = 15 * MB
        });

        var (todayDl, todayUl) = await context.Repository.GetTodaySummaryAsync("wlo1");
        // Total expected = 100MB + 20MB + 30MB = 150MB dl, 50MB + 10MB + 15MB = 75MB ul
        Assert.Equal(150 * MB, todayDl);
        Assert.Equal(75 * MB, todayUl);
    }

    [Fact]
    public async Task Requirement3_MultiInterface_DoesNotCrossSubtract()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var (todayStart, _) = DateRangeHelper.GetLocalTodayRange();

        // wlo1 traffic
        await context.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = todayStart.AddHours(1),
            InterfaceName = "wlo1",
            BytesReceived = 500 * MB,
            BytesSent = 200 * MB,
            DownloadDelta = 50 * MB,
            UploadDelta = 20 * MB
        });

        // eth0 traffic (independent counter starting at 10GB)
        await context.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = todayStart.AddHours(2),
            InterfaceName = "eth0",
            BytesReceived = 10 * GB,
            BytesSent = 5 * GB,
            DownloadDelta = 100 * MB,
            UploadDelta = 50 * MB
        });

        // Combined summary across all interfaces
        var (allDl, allUl) = await context.Repository.GetTodaySummaryAsync();
        Assert.Equal(150 * MB, allDl);
        Assert.Equal(70 * MB, allUl);

        // Individual interface summaries
        var (wloDl, wloUl) = await context.Repository.GetTodaySummaryAsync("wlo1");
        Assert.Equal(50 * MB, wloDl);
        Assert.Equal(20 * MB, wloUl);

        var (ethDl, ethUl) = await context.Repository.GetTodaySummaryAsync("eth0");
        Assert.Equal(100 * MB, ethDl);
        Assert.Equal(50 * MB, ethUl);
    }

    [Fact]
    public async Task Requirement4_ZeroData_ReturnsZeroDeterministically()
    {
        using var context = await TestDatabaseFactory.CreateAsync();

        var (todayDl, todayUl) = await context.Repository.GetTodaySummaryAsync();
        Assert.Equal(0, todayDl);
        Assert.Equal(0, todayUl);

        var (monthDl, monthUl) = await context.Repository.GetMonthSummaryAsync();
        Assert.Equal(0, monthDl);
        Assert.Equal(0, monthUl);

        var (start, end) = DateRangeHelper.GetLocalTodayRange();
        var daily = (await context.Repository.GetDailyUsageAsync(start, end)).ToList();
        Assert.Empty(daily);

        var hourly = (await context.Repository.GetHourlyUsageAsync(DateTime.Today)).ToList();
        Assert.Empty(hourly);
    }

    [Fact]
    public async Task Requirement5_DailyUsageAndHourlyUsage_MatchSummaryTotals()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var (todayStart, todayEnd) = DateRangeHelper.GetLocalTodayRange();

        // Seed 3 hourly intervals today
        await context.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = todayStart.AddHours(2),
            InterfaceName = "wlo1",
            DownloadDelta = 40 * MB,
            UploadDelta = 10 * MB
        });

        await context.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = todayStart.AddHours(5),
            InterfaceName = "wlo1",
            DownloadDelta = 60 * MB,
            UploadDelta = 30 * MB
        });

        await context.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = todayStart.AddHours(8),
            InterfaceName = "wlo1",
            DownloadDelta = 100 * MB,
            UploadDelta = 60 * MB
        });

        var (todayDl, todayUl) = await context.Repository.GetTodaySummaryAsync();
        var daily = (await context.Repository.GetDailyUsageAsync(todayStart, todayEnd)).ToList();
        var hourly = (await context.Repository.GetHourlyUsageAsync(DateTime.Today)).ToList();

        long dailyDlSum = daily.Sum(d => d.BytesDownloaded);
        long dailyUlSum = daily.Sum(d => d.BytesUploaded);
        long hourlyDlSum = hourly.Sum(h => h.BytesDownloaded);
        long hourlyUlSum = hourly.Sum(h => h.BytesUploaded);

        // Strict equality across all 3 query paths
        Assert.Equal(200 * MB, todayDl);
        Assert.Equal(100 * MB, todayUl);
        Assert.Equal(todayDl, dailyDlSum);
        Assert.Equal(todayUl, dailyUlSum);
        Assert.Equal(todayDl, hourlyDlSum);
        Assert.Equal(todayUl, hourlyUlSum);
    }

    [Fact]
    public async Task Requirement6_AnalyticsService_SynchronizedWithRepository()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var (todayStart, _) = DateRangeHelper.GetLocalTodayRange();

        await context.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = todayStart.AddHours(3),
            InterfaceName = "wlo1",
            DownloadDelta = 75 * MB,
            UploadDelta = 25 * MB
        });

        var analyticsService = new AnalyticsService(context.Repository);
        var summary = await analyticsService.GetSummaryAsync(AnalyticsPeriod.Today);

        Assert.Equal(75 * MB, summary.TotalDownloaded);
        Assert.Equal(25 * MB, summary.TotalUploaded);
        Assert.Equal(100 * MB, summary.TotalUsage);

        var dailySeries = await analyticsService.GetDailySeriesAsync(AnalyticsPeriod.Today);
        Assert.Single(dailySeries);
        Assert.Equal(75 * MB, dailySeries[0].BytesDownloaded);
        Assert.Equal(25 * MB, dailySeries[0].BytesUploaded);
    }

    [Fact]
    public async Task Requirement7_HistoricalAnalyticsService_MatchesCanonicalUsage()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var (todayStart, todayEnd) = DateRangeHelper.GetLocalTodayRange();

        await context.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = todayStart.AddHours(4),
            InterfaceName = "wlo1",
            DownloadDelta = 120 * MB,
            UploadDelta = 40 * MB
        });

        var histService = new HistoricalAnalyticsService(context.Repository);
        var breakdown = await histService.GetDailyBreakdownAsync(DateTime.Today.Year, DateTime.Today.Month);

        long histDl = breakdown.Sum(b => b.BytesDownloaded);
        long histUl = breakdown.Sum(b => b.BytesUploaded);

        var (repoDl, repoUl) = await context.Repository.GetMonthSummaryAsync();

        Assert.Equal(120 * MB, histDl);
        Assert.Equal(40 * MB, histUl);
        Assert.Equal(repoDl, histDl);
        Assert.Equal(repoUl, histUl);
    }

    [Fact]
    public async Task Requirement8_LegacyFallback_ComputesInterfaceScopedMaxMinusMin()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var (todayStart, _) = DateRangeHelper.GetLocalTodayRange();

        // Seed legacy records with DownloadDelta = 0 and UploadDelta = 0
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={context.DbPath};Pooling=False"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO NetworkUsageRecords (Timestamp, InterfaceName, DownloadSpeed, UploadSpeed, BytesReceived, BytesSent, DownloadDelta, UploadDelta)
                VALUES 
                    (@T1, 'wlo1', 1.0, 1.0, 1000000, 500000, 0, 0),
                    (@T2, 'wlo1', 1.0, 1.0, 3000000, 1500000, 0, 0);";
            cmd.Parameters.AddWithValue("@T1", todayStart.AddHours(1).ToString("o"));
            cmd.Parameters.AddWithValue("@T2", todayStart.AddHours(2).ToString("o"));
            await cmd.ExecuteNonQueryAsync();
        }

        var (todayDl, todayUl) = await context.Repository.GetTodaySummaryAsync("wlo1");
        // MAX(3,000,000) - MIN(1,000,000) = 2,000,000 dl
        // MAX(1,500,000) - MIN(500,000) = 1,000,000 ul
        Assert.Equal(2000000, todayDl);
        Assert.Equal(1000000, todayUl);
    }
}
