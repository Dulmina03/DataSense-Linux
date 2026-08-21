using System;
using System.IO;
using System.Threading.Tasks;
using DataSense.Models;
using DataSense.Services;
using DataSense.Tests.Helpers;
using Xunit;

namespace DataSense.Tests.Services;

public class ImportRestoreServiceTests
{
    [Fact]
    public async Task GeneratePreviewAsync_ValidFile_ReturnsPreview()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var exportService = new ExportService(context.Repository, analytics);
        var eventService = new EventService();
        var importService = new ImportRestoreService(exportService, eventService);

        string tempFolder = Path.Combine(Path.GetTempPath(), "DataSense_ImportTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        string csvPath = Path.Combine(tempFolder, "sample.csv");

        File.WriteAllLines(csvPath, new[] { "Header1,Header2", "Val1,Val2", "Val3,Val4" });

        var preview = await importService.GeneratePreviewAsync(csvPath);

        Assert.NotNull(preview);
        Assert.Equal("CSV", preview.Format);
        Assert.Equal(2, preview.ValidRecords);

        Directory.Delete(tempFolder, true);
    }

    [Fact]
    public async Task ImportDataAsync_FiresEventOnSuccess()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var exportService = new ExportService(context.Repository, analytics);
        var eventService = new EventService();
        var importService = new ImportRestoreService(exportService, eventService);

        string tempFolder = Path.Combine(Path.GetTempPath(), "DataSense_ImportTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        string jsonPath = Path.Combine(tempFolder, "sample.json");

        File.WriteAllLines(jsonPath, new[] { "[", "{\"id\":1}", "]" });

        var result = await importService.ImportDataAsync(jsonPath, ImportMode.Merge);

        Assert.True(result.Success);
        Assert.Equal(1, eventService.UnreadCount);

        Directory.Delete(tempFolder, true);
    }
}
