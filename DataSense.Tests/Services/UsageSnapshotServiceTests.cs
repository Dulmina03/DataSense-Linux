using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Models;
using DataSense.Services;
using Moq;
using Xunit;

namespace DataSense.Tests.Services;

public class UsageSnapshotServiceTests
{
    private class FakeNetworkUsageCollector : INetworkUsageCollector
    {
        public List<InterfaceRawCounters> NextCounters { get; set; } = new();

        public Task<IReadOnlyList<InterfaceRawCounters>> CollectAllInterfacesAsync()
        {
            return Task.FromResult<IReadOnlyList<InterfaceRawCounters>>(NextCounters.AsReadOnly());
        }

        public Task<InterfaceRawCounters?> CollectInterfaceAsync(string interfaceName)
        {
            var item = NextCounters.Find(c => c.InterfaceName.Equals(interfaceName, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(item);
        }
    }

    [Fact]
    public async Task MeasureCycleAsync_FirstSample_EstablishesBaselineWithZeroDelta()
    {
        var collector = new FakeNetworkUsageCollector();
        collector.NextCounters = new List<InterfaceRawCounters>
        {
            new()
            {
                InterfaceName = "wlan0",
                TimestampUtc = DateTime.UtcNow,
                RawBytesReceived = 10_000_000,
                RawBytesSent = 5_000_000,
                IsOperational = true,
                ConnectionType = "WiFi"
            }
        };

        var service = new UsageSnapshotService(collector);
        var snapshots = await service.MeasureCycleAsync();

        Assert.Single(snapshots);
        var snap = snapshots[0];
        Assert.Equal("wlan0", snap.InterfaceName);
        Assert.Equal(10_000_000, snap.RawBytesReceived);
        Assert.Equal(5_000_000, snap.RawBytesSent);
        Assert.Equal(0, snap.DeltaBytesReceived);
        Assert.Equal(0, snap.DeltaBytesSent);
        Assert.Equal(0, snap.DownloadSpeedBps);
        Assert.Equal(0, snap.UploadSpeedBps);
        Assert.True(snap.IsInitialBaseline);
        Assert.False(snap.IsCounterReset);
    }

    [Fact]
    public async Task MeasureCycleAsync_NormalIncreasingCounter_CalculatesAccurateDeltaAndSpeed()
    {
        var collector = new FakeNetworkUsageCollector();
        var t0 = DateTime.UtcNow;
        collector.NextCounters = new List<InterfaceRawCounters>
        {
            new()
            {
                InterfaceName = "eth0",
                TimestampUtc = t0,
                RawBytesReceived = 100_000,
                RawBytesSent = 50_000,
                IsOperational = true,
                ConnectionType = "Ethernet"
            }
        };

        var service = new UsageSnapshotService(collector);
        await service.MeasureCycleAsync(); // Baseline

        // Sample 2: +10,000 bytes Rx, +5,000 bytes Tx
        collector.NextCounters = new List<InterfaceRawCounters>
        {
            new()
            {
                InterfaceName = "eth0",
                TimestampUtc = t0.AddSeconds(1),
                RawBytesReceived = 110_000,
                RawBytesSent = 55_000,
                IsOperational = true,
                ConnectionType = "Ethernet"
            }
        };

        var snapshots = await service.MeasureCycleAsync();

        Assert.Single(snapshots);
        var snap = snapshots[0];
        Assert.Equal(10_000, snap.DeltaBytesReceived);
        Assert.Equal(5_000, snap.DeltaBytesSent);
        Assert.True(snap.DownloadSpeedBps > 0);
        Assert.True(snap.UploadSpeedBps > 0);
        Assert.False(snap.IsInitialBaseline);
        Assert.False(snap.IsCounterReset);
    }

    [Fact]
    public async Task MeasureCycleAsync_ZeroTraffic_YieldsZeroDeltaAndZeroSpeed()
    {
        var collector = new FakeNetworkUsageCollector();
        var t0 = DateTime.UtcNow;
        collector.NextCounters = new List<InterfaceRawCounters>
        {
            new()
            {
                InterfaceName = "eth0",
                TimestampUtc = t0,
                RawBytesReceived = 200_000,
                RawBytesSent = 100_000,
                IsOperational = true,
                ConnectionType = "Ethernet"
            }
        };

        var service = new UsageSnapshotService(collector);
        await service.MeasureCycleAsync(); // Baseline

        // Sample 2: No byte changes
        collector.NextCounters = new List<InterfaceRawCounters>
        {
            new()
            {
                InterfaceName = "eth0",
                TimestampUtc = t0.AddSeconds(1),
                RawBytesReceived = 200_000,
                RawBytesSent = 100_000,
                IsOperational = true,
                ConnectionType = "Ethernet"
            }
        };

        var snapshots = await service.MeasureCycleAsync();

        Assert.Single(snapshots);
        var snap = snapshots[0];
        Assert.Equal(0, snap.DeltaBytesReceived);
        Assert.Equal(0, snap.DeltaBytesSent);
        Assert.Equal(0, snap.DownloadSpeedBps);
        Assert.Equal(0, snap.UploadSpeedBps);
    }

    [Fact]
    public async Task MeasureCycleAsync_CounterReset_ProducesZeroDeltaAndNoSpike()
    {
        var collector = new FakeNetworkUsageCollector();
        var t0 = DateTime.UtcNow;
        // Large counter before reboot / reset
        collector.NextCounters = new List<InterfaceRawCounters>
        {
            new()
            {
                InterfaceName = "wlan0",
                TimestampUtc = t0,
                RawBytesReceived = 1_000_000_000,
                RawBytesSent = 500_000_000,
                IsOperational = true,
                ConnectionType = "WiFi"
            }
        };

        var service = new UsageSnapshotService(collector);
        await service.MeasureCycleAsync(); // Baseline

        // Reset occurs: kernel counter restarts at 500 bytes
        collector.NextCounters = new List<InterfaceRawCounters>
        {
            new()
            {
                InterfaceName = "wlan0",
                TimestampUtc = t0.AddSeconds(1),
                RawBytesReceived = 500,
                RawBytesSent = 200,
                IsOperational = true,
                ConnectionType = "WiFi"
            }
        };

        var snapshots = await service.MeasureCycleAsync();

        Assert.Single(snapshots);
        var snap = snapshots[0];
        // CRITICAL: Delta must be 0, NOT 500, NOT 1,000,000,000!
        Assert.Equal(0, snap.DeltaBytesReceived);
        Assert.Equal(0, snap.DeltaBytesSent);
        Assert.Equal(0, snap.DownloadSpeedBps);
        Assert.Equal(0, snap.UploadSpeedBps);
        Assert.True(snap.IsCounterReset);

        // Next consecutive sample: 500 -> 800 Rx, 200 -> 350 Tx
        collector.NextCounters = new List<InterfaceRawCounters>
        {
            new()
            {
                InterfaceName = "wlan0",
                TimestampUtc = t0.AddSeconds(2),
                RawBytesReceived = 800,
                RawBytesSent = 350,
                IsOperational = true,
                ConnectionType = "WiFi"
            }
        };

        var snapAfterReset = (await service.MeasureCycleAsync())[0];
        Assert.Equal(300, snapAfterReset.DeltaBytesReceived); // 800 - 500
        Assert.Equal(150, snapAfterReset.DeltaBytesSent);     // 350 - 200
        Assert.False(snapAfterReset.IsCounterReset);
    }

    [Fact]
    public async Task MeasureCycleAsync_MultipleInterfaces_TrackedIndependentlyWithoutCrosstalk()
    {
        var collector = new FakeNetworkUsageCollector();
        var t0 = DateTime.UtcNow;
        collector.NextCounters = new List<InterfaceRawCounters>
        {
            new() { InterfaceName = "eth0", TimestampUtc = t0, RawBytesReceived = 1000, RawBytesSent = 500, IsOperational = true, ConnectionType = "Ethernet" },
            new() { InterfaceName = "wlan0", TimestampUtc = t0, RawBytesReceived = 5000, RawBytesSent = 2000, IsOperational = true, ConnectionType = "WiFi" }
        };

        var service = new UsageSnapshotService(collector);
        await service.MeasureCycleAsync(); // Baseline for both

        // Sample 2: eth0 transfers +500 Rx; wlan0 transfers +1000 Rx
        collector.NextCounters = new List<InterfaceRawCounters>
        {
            new() { InterfaceName = "eth0", TimestampUtc = t0.AddSeconds(1), RawBytesReceived = 1500, RawBytesSent = 500, IsOperational = true, ConnectionType = "Ethernet" },
            new() { InterfaceName = "wlan0", TimestampUtc = t0.AddSeconds(1), RawBytesReceived = 6000, RawBytesSent = 2000, IsOperational = true, ConnectionType = "WiFi" }
        };

        var snapshots = await service.MeasureCycleAsync();

        Assert.Equal(2, snapshots.Count);
        var ethSnap = snapshots.FirstOrDefault(s => s.InterfaceName == "eth0");
        var wifiSnap = snapshots.FirstOrDefault(s => s.InterfaceName == "wlan0");

        Assert.NotNull(ethSnap);
        Assert.NotNull(wifiSnap);
        Assert.Equal(500, ethSnap.DeltaBytesReceived);
        Assert.Equal(1000, wifiSnap.DeltaBytesReceived);

        // Verify Host Snapshot combined totals
        var host = service.GetLatestHostSnapshot();
        Assert.NotNull(host);
        Assert.Equal(1500, host.DeltaBytesReceived); // 500 + 1000
    }

    [Fact]
    public async Task MeasureCycleAsync_LargeByteCounters_CalculatesWithoutOverflow()
    {
        var collector = new FakeNetworkUsageCollector();
        var t0 = DateTime.UtcNow;
        long largeRx1 = 5_000_000_000_000L; // 5 TB
        long largeTx1 = 2_000_000_000_000L;

        collector.NextCounters = new List<InterfaceRawCounters>
        {
            new() { InterfaceName = "eth0", TimestampUtc = t0, RawBytesReceived = largeRx1, RawBytesSent = largeTx1, IsOperational = true, ConnectionType = "Ethernet" }
        };

        var service = new UsageSnapshotService(collector);
        await service.MeasureCycleAsync();

        // +10 MB
        collector.NextCounters = new List<InterfaceRawCounters>
        {
            new() { InterfaceName = "eth0", TimestampUtc = t0.AddSeconds(1), RawBytesReceived = largeRx1 + 10_485_760, RawBytesSent = largeTx1 + 5_242_880, IsOperational = true, ConnectionType = "Ethernet" }
        };

        var snapshots = await service.MeasureCycleAsync();
        var snap = snapshots[0];

        Assert.Equal(10_485_760, snap.DeltaBytesReceived);
        Assert.Equal(5_242_880, snap.DeltaBytesSent);
    }

    [Fact]
    public async Task MeasureCycleAsync_EventEmitted_ExactlyOncePerCycle()
    {
        var collector = new FakeNetworkUsageCollector();
        collector.NextCounters = new List<InterfaceRawCounters>
        {
            new() { InterfaceName = "wlan0", TimestampUtc = DateTime.UtcNow, RawBytesReceived = 1000, RawBytesSent = 500, IsOperational = true, ConnectionType = "WiFi" }
        };

        var service = new UsageSnapshotService(collector);
        int eventCount = 0;
        service.SnapshotsGenerated += (snaps) => eventCount++;

        await service.MeasureCycleAsync();
        await service.MeasureCycleAsync();

        Assert.Equal(2, eventCount);
    }
}
