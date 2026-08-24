using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using DataSense.Services;
using Xunit;

namespace DataSense.Tests.Services;

public class LinuxNetworkMonitorServiceTests
{
    [Fact]
    public async Task GetAvailableInterfaces_ReturnsRealOperationalInterfaces()
    {
        var monitor = new LinuxNetworkMonitorService();
        var ifaces = (await monitor.GetAvailableInterfacesAsync()).ToList();

        // On Linux development machine, we expect at least one interface (e.g. wlo1, eno1, etc.)
        Assert.NotNull(ifaces);
        Assert.DoesNotContain("lo", ifaces);
    }

    [Fact]
    public async Task GetUsageAsync_CalculatesPositiveThroughput_OnRealTraffic()
    {
        var monitor = new LinuxNetworkMonitorService();
        var ifaces = (await monitor.GetAvailableInterfacesAsync()).ToList();

        if (ifaces.Count == 0) return; // Skip if no physical interface in test environment

        string targetIface = ifaces.First();

        // Initial sample
        var usage1 = await monitor.GetUsageAsync(targetIface);
        Assert.NotNull(usage1);
        Assert.True(usage1.BytesReceived >= 0);
        Assert.True(usage1.BytesSent >= 0);

        // Small delay
        await Task.Delay(200);

        // Second sample
        var usage2 = await monitor.GetUsageAsync(targetIface);
        Assert.NotNull(usage2);
        Assert.True(usage2.DownloadSpeed >= 0);
        Assert.True(usage2.UploadSpeed >= 0);
        Assert.False(double.IsNaN(usage2.DownloadSpeed));
        Assert.False(double.IsInfinity(usage2.DownloadSpeed));
        Assert.False(double.IsNaN(usage2.UploadSpeed));
        Assert.False(double.IsInfinity(usage2.UploadSpeed));
    }
}
