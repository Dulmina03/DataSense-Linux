using System;
using System.IO;
using System.Threading.Tasks;
using DataSense.Services;
using DataSense.Tests.Helpers;
using Xunit;

namespace DataSense.Tests.Services;

public class AutostartIntegrationTests
{
    [Fact]
    public async Task AutostartService_EnableAndDisable_CreatesAndDeletesDesktopFile()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var storage = new LinuxStorageService();
        var platform = new LinuxPlatformService();
        var startup = new LinuxStartupService(storage, platform, context.Repository);

        bool enabled = await startup.SetAutostartEnabledAsync(true);
        if (enabled)
        {
            Assert.True(File.Exists(startup.GetAutostartFilePath()));
            Assert.True(await startup.VerifyAutostartFileAsync());

            bool disabled = await startup.SetAutostartEnabledAsync(false);
            Assert.True(disabled);
            Assert.False(File.Exists(startup.GetAutostartFilePath()));
        }
    }

    [Fact]
    public void AutostartService_GetApplicationExecutablePath_ReturnsValidString()
    {
        string execPath = LinuxStartupService.GetApplicationExecutablePath();
        Assert.False(string.IsNullOrWhiteSpace(execPath));
    }
}
