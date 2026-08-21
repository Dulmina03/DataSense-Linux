using System;
using DataSense.Services;
using Xunit;

namespace DataSense.Tests.Services;

public class LinuxPlatformServiceTests
{
    [Fact]
    public void LinuxPlatformService_DetectsBasicPlatformProperties()
    {
        var service = new LinuxPlatformService();

        Assert.NotNull(service.DistributionName);
        Assert.NotNull(service.DesktopEnvironment);
        Assert.NotNull(service.DisplayServer);
        Assert.NotNull(service.KernelVersion);
        Assert.NotNull(service.DotNetRuntime);
        Assert.NotNull(service.ApplicationVersion);
    }

    [Fact]
    public void LinuxPlatformService_GetSystemSummary_ContainsRequiredKeys()
    {
        var service = new LinuxPlatformService();
        var summary = service.GetSystemSummary();

        Assert.True(summary.ContainsKey("Operating System"));
        Assert.True(summary.ContainsKey("Distribution"));
        Assert.True(summary.ContainsKey("Desktop Environment"));
        Assert.True(summary.ContainsKey("Display Server"));
        Assert.True(summary.ContainsKey("Kernel"));
        Assert.True(summary.ContainsKey("Architecture"));
        Assert.True(summary.ContainsKey(".NET Runtime"));
        Assert.True(summary.ContainsKey("DataSense Version"));
    }

    [Fact]
    public void LinuxPlatformService_GetExecutablePath_HandlesInvalidInput()
    {
        var service = new LinuxPlatformService();
        string emptyPath = service.GetExecutablePath("");
        string nonExistent = service.GetExecutablePath("non_existent_utility_12345");

        Assert.Equal(string.Empty, emptyPath);
        Assert.Equal(string.Empty, nonExistent);
    }
}
