using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DataSense.Database;
using DataSense.Models;

namespace DataSense.Tests.Helpers;

public static class TestDataBuilder
{
    public static async Task SeedCumulativeUsageAsync(
        INetworkUsageRepository repo,
        string interfaceName,
        DateTime startTime,
        TimeSpan step,
        params (long rx, long tx)[] cumulativePoints)
    {
        DateTime current = startTime;
        for (int i = 0; i < cumulativePoints.Length; i++)
        {
            var (rx, tx) = cumulativePoints[i];
            long dlDelta = i > 0 ? Math.Max(0, rx - cumulativePoints[i - 1].rx) : 0;
            long ulDelta = i > 0 ? Math.Max(0, tx - cumulativePoints[i - 1].tx) : 0;
            await repo.SaveUsageAsync(new NetworkUsage
            {
                Timestamp = current,
                InterfaceName = interfaceName,
                DownloadSpeed = 1.0,
                UploadSpeed = 0.5,
                BytesReceived = rx,
                BytesSent = tx,
                DownloadDelta = dlDelta,
                UploadDelta = ulDelta
            });
            current = current.Add(step);
        }
    }

    public static async Task SeedSpeedTestAsync(
        INetworkUsageRepository repo,
        string networkName,
        double dlMbps,
        double ulMbps,
        double pingMs,
        DateTime timestamp)
    {
        await repo.SaveSpeedTestAsync(new SpeedTestRecord
        {
            Timestamp = timestamp,
            NetworkName = networkName,
            ConnectionType = "Wi-Fi",
            DownloadSpeedMbps = dlMbps,
            UploadSpeedMbps = ulMbps,
            PingMs = pingMs,
            JitterMs = 2.0,
            ServerName = "TestServer"
        });
    }

    public static async Task SeedProcessUsageAsync(
        INetworkUsageRepository repo,
        string processName,
        DateTime timestamp,
        long rxBytes,
        long txBytes)
    {
        await repo.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            Timestamp = timestamp,
            ProcessName = processName,
            BytesDownloaded = rxBytes,
            BytesUploaded = txBytes
        });
    }

    public static async Task SeedSessionAsync(
        INetworkUsageRepository repo,
        string networkName,
        string interfaceName,
        DateTime startTime,
        TimeSpan duration,
        long dlBytes,
        long ulBytes)
    {
        await repo.SaveSessionAsync(new NetworkSession
        {
            NetworkName = networkName,
            InterfaceName = interfaceName,
            ConnectionType = "Wi-Fi",
            StartTime = startTime,
            EndTime = startTime.Add(duration),
            BytesDownloaded = dlBytes,
            BytesUploaded = ulBytes
        });
    }
}
