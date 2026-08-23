using System;
using System.IO;
using System.Threading.Tasks;
using DataSense.Services;
using DataSense.Tests.Helpers;
using Xunit;

namespace DataSense.Tests.Services;

public class BackupRecoveryServiceTests
{
    [Fact]
    public async Task CreateBackupAsync_CreatesRecoveryPointAndFiresEvent()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var exportService = new ExportService(context.Repository, analytics, new Moq.Mock<DataSense.Services.IApplicationAnalyticsService>().Object);
        var eventService = new EventService();
        var backupService = new BackupRecoveryService(exportService, eventService);

        var result = await backupService.CreateBackupAsync("TestManual");

        Assert.True(result.Success);
        Assert.True(File.Exists(result.FilePath));
        Assert.Equal(1, eventService.UnreadCount);

        var points = await backupService.GetRecoveryPointsAsync();
        Assert.NotEmpty(points);

        var health = await backupService.InspectBackupHealthAsync();
        Assert.True(health.TotalBackups > 0);
    }
}
