using System;
using System.Threading.Tasks;
using DataSense.Models;
using DataSense.Services;

namespace DataSense.Tests.Helpers;

public class MockNetworkConnectionService : INetworkConnectionService
{
    public Task<NetworkConnectionDetails> GetConnectionDetailsAsync(string interfaceName)
    {
        return Task.FromResult(new NetworkConnectionDetails
        {
            ConnectionName = "TestWiFi",
            InterfaceName = interfaceName ?? "wlan0",
            ConnectionType = "Wi-Fi",
            ConnectionState = "connected",
            Ipv4Address = "192.168.1.100"
        });
    }
}

public class MockNetworkMonitorWorker : INetworkMonitorWorker
{
    public string? ActiveInterface { get; set; } = "wlan0";
    public bool IsRunning => true;
    public double DownloadSpeed => 100;
    public double UploadSpeed => 50;
    public long TotalBytesDownloaded => 1000;
    public long TotalBytesUploaded => 500;
    public long TotalDataUsage => 1500;
    public NetworkUsage? LatestUsage => new NetworkUsage { DownloadSpeed = 100, UploadSpeed = 50 };

    public event Action<NetworkUsage>? NetworkUsageUpdated
    {
        add { }
        remove { }
    }

    public void Start() { }
    public void Stop() { }
}
