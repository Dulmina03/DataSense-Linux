using System;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Models;
using DataSense.Services;
using DataSense.Tests.Helpers;
using Xunit;

namespace DataSense.Tests.Services;

public class MockLinuxProcessResolver : ILinuxProcessResolver
{
    public ProcessIdentityInfo? ResolveProcessIdentity(int pid)
    {
        if (pid == Environment.ProcessId)
        {
            return new ProcessIdentityInfo
            {
                Pid = Environment.ProcessId,
                ProcessName = "firefox",
                UserName = "testuser",
                StartTimeTicks = 55555
            };
        }
        return null;
    }

    public string GetUserNameFromUid(int uid) => "testuser";
}

public class ApplicationAnalyticsServiceTests
{
    [Fact]
    public async Task GetApplicationSummariesAsync_WithData_ReturnsCorrectAggregations()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var processResolver = new MockLinuxProcessResolver();
        var service = new ApplicationAnalyticsService(context.Repository, processResolver);

        var now = DateTime.UtcNow;
        var record = new ProcessUsageRecord
        {
            Timestamp = now,
            ProcessName = "firefox",
            Pid = Environment.ProcessId,
            StartTimeTicks = 55555,
            ExecutablePath = "/usr/bin/firefox",
            UserName = "testuser",
            DataSource = "nethogs",
            BytesDownloaded = 1000,
            BytesUploaded = 500
        };
        await context.Repository.SaveProcessUsageAsync(record);

        // Get summaries for Today
        var summaries = (await service.GetApplicationSummariesAsync(AppAnalyticsPeriod.Today)).ToList();

        Assert.NotEmpty(summaries);
        var firefox = summaries.First(x => x.ProcessName == "firefox");
        Assert.Equal(1000, firefox.DownloadBytes);
        Assert.Equal(500, firefox.UploadBytes);
        Assert.Equal(1500, firefox.TotalBytes);
        Assert.Equal(100.0, firefox.PercentageOfTotal);
        Assert.True(firefox.IsCurrentlyRunning);
    }

    [Fact]
    public async Task GetProcessDetailAsync_QueriesDatabaseCorrectly()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var processResolver = new MockLinuxProcessResolver();
        var service = new ApplicationAnalyticsService(context.Repository, processResolver);

        var now = DateTime.UtcNow;
        var record = new ProcessUsageRecord
        {
            Timestamp = now,
            ProcessName = "firefox",
            Pid = Environment.ProcessId,
            StartTimeTicks = 55555,
            ExecutablePath = "/usr/bin/firefox",
            UserName = "testuser",
            DataSource = "nethogs",
            BytesDownloaded = 1000,
            BytesUploaded = 500
        };
        await context.Repository.SaveProcessUsageAsync(record);

        var detail = await service.GetProcessDetailAsync("firefox", Environment.ProcessId, 55555, AppAnalyticsPeriod.Today);

        Assert.NotNull(detail);
        Assert.Equal(1, detail.SamplesCount);
        Assert.Equal(1, detail.ActiveDaysCount);
        Assert.Equal(now.Date, detail.PeakUsageDay?.Date);
        Assert.Equal(1500, detail.PeakUsageDayBytes);
    }

    [Fact]
    public async Task GetProcessTimelineAsync_ReturnsTimelinePoints()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var processResolver = new MockLinuxProcessResolver();
        var service = new ApplicationAnalyticsService(context.Repository, processResolver);

        var now = DateTime.UtcNow;
        var record = new ProcessUsageRecord
        {
            Timestamp = now,
            ProcessName = "firefox",
            Pid = Environment.ProcessId,
            StartTimeTicks = 55555,
            ExecutablePath = "/usr/bin/firefox",
            UserName = "testuser",
            DataSource = "nethogs",
            BytesDownloaded = 1000,
            BytesUploaded = 500
        };
        await context.Repository.SaveProcessUsageAsync(record);

        var timeline = (await service.GetProcessTimelineAsync("firefox", Environment.ProcessId, 55555, AppAnalyticsPeriod.Today)).ToList();

        Assert.NotEmpty(timeline);
        Assert.Equal(1000, timeline[0].DownloadBytes);
        Assert.Equal(500, timeline[0].UploadBytes);
    }
}
