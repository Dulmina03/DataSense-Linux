using System;
using System.Threading.Tasks;
using DataSense.Services;
using DataSense.Tests.Helpers;
using Xunit;

namespace DataSense.Tests.Services;

public class PauseResumeLifecycleTests
{
    [Fact]
    public async Task ProcessNetworkMonitorWorker_StartStop_IdempotentAndClean()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var platform = new LinuxPlatformService();
        var monitor = new NethogsProcessNetworkMonitor(platform);
        var worker = new ProcessNetworkMonitorWorker(monitor, context.Repository);

        Assert.False(worker.IsRunning);

        worker.Start();
        // Calling Start again should be idempotent and not crash
        worker.Start();

        worker.Stop();
        Assert.False(worker.IsRunning);

        // Calling Stop again should be safe
        worker.Stop();
    }
}
