using System;
using System.IO;
using System.Threading.Tasks;
using DataSense.Models;
using DataSense.Services;
using DataSense.Tests.Helpers;
using Xunit;

namespace DataSense.Tests.Services;

public class ExportServiceTests
{
    [Fact]
    public async Task ExportDataAsync_CSVFormat_CreatesValidCSVFile()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var exportService = new ExportService(context.Repository, analytics, new Moq.Mock<DataSense.Services.IApplicationAnalyticsService>().Object);

        string tempFolder = Path.Combine(Path.GetTempPath(), "DataSense_ExportTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);

        DateTime now = DateTime.UtcNow;
        await TestDataBuilder.SeedCumulativeUsageAsync(context.Repository, "wlan0", now, TimeSpan.FromHours(1), (100, 50), (200, 100));

        var options = new ExportOptions
        {
            OutputDirectory = tempFolder,
            Format = ExportFormat.CSV,
            DataType = ExportDataType.Usage,
            StartDate = now.AddDays(-1),
            EndDate = now.AddDays(1)
        };

        var result = await exportService.ExportDataAsync(options);

        Assert.True(result.Success);
        Assert.True(File.Exists(result.FilePath));
        string content = File.ReadAllText(result.FilePath);
        Assert.Contains("Timestamp", content);

        Directory.Delete(tempFolder, true);
    }

    [Fact]
    public async Task CreateCompleteBackupAsync_And_ValidateBackupAsync()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var exportService = new ExportService(context.Repository, analytics, new Moq.Mock<DataSense.Services.IApplicationAnalyticsService>().Object);

        string tempFolder = Path.Combine(Path.GetTempPath(), "DataSense_BackupTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        string zipPath = Path.Combine(tempFolder, "test_backup.zip");

        var result = await exportService.CreateCompleteBackupAsync(zipPath);

        Assert.True(result.Success);
        Assert.True(File.Exists(zipPath));

        bool isValid = await exportService.ValidateBackupAsync(zipPath);
        Assert.True(isValid);

        // Invalid file validation
        string invalidPath = Path.Combine(tempFolder, "invalid.zip");
        File.WriteAllText(invalidPath, "not a zip file");
        bool isInvalidValid = await exportService.ValidateBackupAsync(invalidPath);
        Assert.False(isInvalidValid);

        Directory.Delete(tempFolder, true);
    }
}
