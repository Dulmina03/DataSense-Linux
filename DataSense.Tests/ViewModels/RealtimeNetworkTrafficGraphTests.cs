using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Models;
using DataSense.Services;
using DataSense.ViewModels;
using Xunit;

namespace DataSense.Tests.ViewModels;

public class RealtimeNetworkTrafficGraphTests
{
    [Fact]
    public void RealtimeNetworkPoint_FormatsSpeedTextsCorrectly()
    {
        var point = new RealtimeNetworkPoint
        {
            Timestamp = new DateTime(2026, 8, 23, 20, 30, 0, DateTimeKind.Utc),
            DownloadRateBytesPerSec = 1048576, // 1 MB/s
            UploadRateBytesPerSec = 524288    // 512 KB/s
        };

        Assert.Equal("1.0 MB/s", point.DownloadSpeedText);
        Assert.Equal("512.0 KB/s", point.UploadSpeedText);
        Assert.Equal("1.5 MB/s", point.TotalSpeedText);
        Assert.Equal("20:30:00", point.TimeText);
        Assert.Equal("20:30", point.ShortTimeText);
    }

    [Fact]
    public void RealtimeNetworkPoint_FormatsPeriodBytes_WhenDownloadBytesSpecified()
    {
        var point = new RealtimeNetworkPoint
        {
            CustomLabel = "Aug 24",
            DownloadBytes = 104857600, // 100 MB
            UploadBytes = 52428800     // 50 MB
        };

        Assert.Equal("100.0 MB", point.DownloadSpeedText);
        Assert.Equal("50.0 MB", point.UploadSpeedText);
        Assert.Equal("150.0 MB", point.TotalSpeedText);
        Assert.Equal("Aug 24", point.TimeText);
    }

    [Fact]
    public void RealtimeNetworkPoint_CombinedRate_IsSumOfDownloadAndUpload()
    {
        var point = new RealtimeNetworkPoint
        {
            DownloadRateBytesPerSec = 1000,
            UploadRateBytesPerSec = 500
        };

        Assert.Equal(1500, point.CombinedRateBytesPerSec);
    }

    [Fact]
    public async Task LinuxNetworkMonitorService_ResetMeasurement_ClearsPreviousState()
    {
        var monitor = new LinuxNetworkMonitorService();
        monitor.ResetMeasurement();

        // Querying non-existent interface returns null
        var usage = await monitor.GetUsageAsync("non_existent_iface_xyz");
        Assert.Null(usage);
    }

    [Fact]
    public async Task LinuxNetworkMonitorService_ReturnsNull_ForEmptyOrDisconnectedInterface()
    {
        var monitor = new LinuxNetworkMonitorService();

        Assert.Null(await monitor.GetUsageAsync(""));
        Assert.Null(await monitor.GetUsageAsync("None"));
        Assert.Null(await monitor.GetUsageAsync("Disconnected"));
    }
}
