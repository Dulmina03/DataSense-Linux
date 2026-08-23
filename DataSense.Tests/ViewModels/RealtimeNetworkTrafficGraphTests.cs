using System;
using System.Linq;
using DataSense.Models;
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
    public void RealtimeNetworkPoint_CombinedRate_IsSumOfDownloadAndUpload()
    {
        var point = new RealtimeNetworkPoint
        {
            DownloadRateBytesPerSec = 1000,
            UploadRateBytesPerSec = 500
        };

        Assert.Equal(1500, point.CombinedRateBytesPerSec);
    }
}
