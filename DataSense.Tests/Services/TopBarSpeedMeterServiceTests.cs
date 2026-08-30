using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using DataSense.Database;
using DataSense.Models;
using DataSense.Services;
using DataSense.Tests.Helpers;
using Moq;
using Xunit;

namespace DataSense.Tests.Services;

public class TopBarSpeedMeterServiceTests
{
    [Fact]
    public async Task RefreshConfiguration_Enabled_WritesContractAndEnablesExtension()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        await context.Repository.SaveSettingAsync("ShowNetworkSpeedMeter", "True");
        await context.Repository.SaveSettingAsync("ShowMeterDownload", "False");
        await context.Repository.SaveSettingAsync("MeterUnits", "MB/s");

        var monitor = new Mock<INetworkMonitorWorker>();
        monitor.SetupGet(m => m.DownloadSpeed).Returns(2 * 1024 * 1024);
        monitor.SetupGet(m => m.UploadSpeed).Returns(512 * 1024);
        monitor.SetupGet(m => m.ActiveInterface).Returns("wlo1");
        var controller = new Mock<IGnomeExtensionController>();
        var service = new TopBarSpeedMeterService(monitor.Object, context.Repository, new ThemeService(), controller.Object);

        await service.RefreshConfigurationAsync();

        var contractPath = GetContractPath();
        Assert.True(File.Exists(contractPath));
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(contractPath));
        Assert.True(document.RootElement.GetProperty("enabled").GetBoolean());
        Assert.False(document.RootElement.GetProperty("showDownload").GetBoolean());
        Assert.Equal("MB/s", document.RootElement.GetProperty("units").GetString());
        controller.Verify(c => c.SetEnabledAsync(true), Times.Once);

        service.Dispose();
    }

    [Fact]
    public async Task RefreshConfiguration_Disabled_WritesDisabledContractAndDisablesExtension()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var contractPath = GetContractPath();
        Directory.CreateDirectory(Path.GetDirectoryName(contractPath)!);
        await File.WriteAllTextAsync(contractPath, "{\"enabled\":true}");
        await context.Repository.SaveSettingAsync("ShowNetworkSpeedMeter", "False");

        var monitor = new Mock<INetworkMonitorWorker>();
        var controller = new Mock<IGnomeExtensionController>();
        var service = new TopBarSpeedMeterService(monitor.Object, context.Repository, new ThemeService(), controller.Object);

        await service.RefreshConfigurationAsync();

        Assert.True(File.Exists(contractPath));
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(contractPath));
        Assert.False(document.RootElement.GetProperty("enabled").GetBoolean());
        controller.Verify(c => c.SetEnabledAsync(false), Times.Once);
        service.Dispose();
        Assert.True(File.Exists(contractPath));
    }

    [Fact]
    public async Task RefreshConfiguration_ControllerFailure_DoesNotThrowAfterContractUpdate()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        await context.Repository.SaveSettingAsync("ShowNetworkSpeedMeter", "True");
        await context.Repository.SaveSettingAsync("MeterPosition", "Center area");
        await context.Repository.SaveSettingAsync("MeterRefreshRate", "5 seconds");

        var monitor = new Mock<INetworkMonitorWorker>();
        var controller = new Mock<IGnomeExtensionController>();
        controller.Setup(c => c.SetEnabledAsync(true)).ThrowsAsync(new InvalidOperationException("GNOME unavailable"));
        var service = new TopBarSpeedMeterService(monitor.Object, context.Repository, new ThemeService(), controller.Object);

        await service.RefreshConfigurationAsync();

        var contractPath = GetContractPath();
        Assert.True(File.Exists(contractPath));
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(contractPath));
        Assert.Equal("Center area", document.RootElement.GetProperty("position").GetString());
        Assert.Equal(5000, document.RootElement.GetProperty("refreshIntervalMs").GetInt32());
        service.Dispose();
    }

    private static string GetContractPath()
    {
        var runtimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        return Path.Combine(
            string.IsNullOrWhiteSpace(runtimeDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DataSense")
                : Path.Combine(runtimeDirectory, "DataSense"),
            "speed-meter.json");
    }
}
