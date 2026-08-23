using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Database;
using DataSense.Models;
using DataSense.Services;
using DataSense.Tests.Helpers;
using Xunit;

namespace DataSense.Tests.Services;

public class LiveMonitoringTests
{
    [Fact]
    public void LiveMonitoringEngine_AddsSamples_AndEnforcesBoundedMemoryEviction()
    {
        // Arrange
        var engine = new LiveMonitoringEngine();

        // Act - Add 350 samples (exceeding MaxSamples limit of 300)
        for (int i = 0; i < 350; i++)
        {
            engine.AddSample(1000 + i, 500 + i);
        }

        // Assert
        Assert.Equal(300, engine.SampleCount);
        var samples = engine.GetRollingSamples(GraphWindowTime.FiveMinutes);
        Assert.True(samples.Count <= 300);
    }

    [Fact]
    public void LiveMonitoringEngine_CalculatesCombinedSpeed_Correctly()
    {
        // Arrange
        var sample = new LiveTrafficSample
        {
            DownloadRateBytesPerSec = 1048576, // 1 MB/s
            UploadRateBytesPerSec = 524288     // 512 KB/s
        };

        // Assert
        Assert.Equal(1572864, sample.CombinedRateBytesPerSec);
    }

    [Fact]
    public void LiveMonitoringEngine_DetectsTrafficSpike_WhenRateExceedsBaseline()
    {
        // Arrange
        var engine = new LiveMonitoringEngine();

        // Add 15 baseline samples around 20 KB/s
        for (int i = 0; i < 15; i++)
        {
            engine.AddSample(15000, 5000); // 20 KB/s combined
        }

        // Act - Add a massive spike sample (2 MB/s)
        engine.AddSample(1500000, 500000);

        // Assert
        var spike = engine.CheckTrafficSpike();
        Assert.True(spike.IsSpikeDetected);
        Assert.True(spike.DeviationPercentage > 100);
    }

    [Fact]
    public void LiveMonitoringEngine_ReturnsNoSpike_WhenInsufficientSamples()
    {
        // Arrange
        var engine = new LiveMonitoringEngine();

        // Add only 3 samples
        engine.AddSample(500000, 500000);
        engine.AddSample(500000, 500000);
        engine.AddSample(500000, 500000);

        // Act
        var spike = engine.CheckTrafficSpike();

        // Assert
        Assert.False(spike.IsSpikeDetected);
    }

    [Fact]
    public void LiveMonitoringEngine_RanksProcesses_AndComputesSharePercentages()
    {
        // Arrange
        var engine = new LiveMonitoringEngine();
        var processes = new List<ProcessNetworkUsage>
        {
            new() { ProcessIdentifier = "firefox", Pid = 100, DownloadRateBytesPerSec = 7000, UploadRateBytesPerSec = 3000 },
            new() { ProcessIdentifier = "chrome", Pid = 200, DownloadRateBytesPerSec = 2000, UploadRateBytesPerSec = 1000 },
            new() { ProcessIdentifier = "slack", Pid = 300, DownloadRateBytesPerSec = 500, UploadRateBytesPerSec = 500 }
        };

        // Act
        engine.UpdateLiveProcesses(processes);
        var rankedTotal = engine.GetRankedProcesses(ProcessSortMode.HighestTotal, ProcessRankCount.Top5);
        var rankedDL = engine.GetRankedProcesses(ProcessSortMode.HighestDownload, ProcessRankCount.Top5);

        // Assert
        Assert.Equal(3, rankedTotal.Count);
        Assert.Equal("firefox", rankedTotal[0].ProcessName);
        Assert.Equal(100, rankedTotal[0].Pid);
        Assert.Equal(71.4, rankedTotal[0].PercentageOfTotalTraffic, 1);
        Assert.Equal("chrome", rankedDL[1].ProcessName);
    }

    [Fact]
    public void LiveMonitoringEngine_RespectsPauseAndResumeLifecycle()
    {
        // Arrange
        var engine = new LiveMonitoringEngine();
        engine.AddSample(1000, 500);
        int initialCount = engine.SampleCount;

        // Act - Pause
        engine.Pause();
        Assert.True(engine.IsPaused);
        engine.AddSample(5000, 5000); // Should be ignored while paused

        // Assert
        Assert.Equal(initialCount, engine.SampleCount);

        // Act - Resume
        engine.Resume();
        Assert.False(engine.IsPaused);
        engine.AddSample(5000, 5000); // Should be recorded
        Assert.Equal(initialCount + 1, engine.SampleCount);
    }

    [Fact]
    public void NetworkInterfaceStats_CalculatesErrorAndDropRates_Correctly()
    {
        // Arrange
        var stats = new NetworkInterfaceStats
        {
            InterfaceName = "eth0",
            RxPackets = 950,
            TxPackets = 50,
            RxErrors = 10,
            TxErrors = 0,
            RxDropped = 20,
            TxDropped = 0
        };

        // Assert
        Assert.Equal(1.0, stats.PacketErrorRatePercentage, 2); // 10 / 1000 = 1%
        Assert.Equal(2.0, stats.PacketDropRatePercentage, 2);  // 20 / 1000 = 2%
    }

    [Fact]
    public async Task ExportService_ExportsCurrentSnapshot_Successfully()
    {
        // Arrange
        using var dbContext = await TestDatabaseFactory.CreateAsync();
        var analyticsMock = new AnalyticsService(dbContext.Repository);
        var exportService = new ExportService(dbContext.Repository, analyticsMock, new Moq.Mock<DataSense.Services.IApplicationAnalyticsService>().Object);

        string tempFile = Path.Combine(Path.GetTempPath(), $"live_snapshot_test_{Guid.NewGuid():N}.json");
        var activeIface = new NetworkInterfaceStats
        {
            InterfaceName = "wlan0",
            ConnectionType = "Wi-Fi",
            State = "up",
            DownloadRateBytesPerSec = 524288,
            UploadRateBytesPerSec = 131072
        };
        var processes = new List<LiveProcessRankItem>
        {
            new() { ProcessName = "firefox", Pid = 1234, DownloadRateBytesPerSec = 524288, PercentageOfTotalTraffic = 80.0 }
        };

        try
        {
            // Act
            var result = await exportService.ExportCurrentSnapshotAsync(tempFile, activeIface, processes);

            // Assert
            Assert.True(result.Success);
            Assert.True(File.Exists(tempFile));
            string json = await File.ReadAllTextAsync(tempFile);
            Assert.Contains("DataSense Live Traffic Monitor", json);
            Assert.Contains("firefox", json);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
