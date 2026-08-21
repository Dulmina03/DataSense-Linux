using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DataSense.Services;

public class NetworkInterfaceStats
{
    public string InterfaceName { get; set; } = string.Empty;
    public string ConnectionType { get; set; } = "Ethernet";
    public bool IsUp { get; set; }
    public string State { get; set; } = "Unknown";
    public long RxBytes { get; set; }
    public long TxBytes { get; set; }
    public long RxPackets { get; set; }
    public long TxPackets { get; set; }
    public long RxErrors { get; set; }
    public long TxErrors { get; set; }
    public long RxDropped { get; set; }
    public long TxDropped { get; set; }
    public double DownloadRateBytesPerSec { get; set; }
    public double UploadRateBytesPerSec { get; set; }
    public double PacketErrorRatePercentage => (RxPackets + TxPackets) > 0 
        ? ((double)(RxErrors + TxErrors) / (RxPackets + TxPackets)) * 100.0 
        : 0.0;
    public double PacketDropRatePercentage => (RxPackets + TxPackets) > 0 
        ? ((double)(RxDropped + TxDropped) / (RxPackets + TxPackets)) * 100.0 
        : 0.0;
    public string? IPv4Address { get; set; }
    public string? IPv6Address { get; set; }
    public string? MacAddress { get; set; }
    public string? NetworkName { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

public interface ILinuxInterfaceStatsService
{
    Task<IEnumerable<NetworkInterfaceStats>> GetAllInterfaceStatsAsync();
    Task<NetworkInterfaceStats?> GetInterfaceStatsAsync(string interfaceName);
}
