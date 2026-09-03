using System;

namespace DataSense.Models;

/// <summary>
/// Immutable canonical measurement snapshot representing verified interval deltas and speeds for a network interface.
/// </summary>
public record NetworkUsageSnapshot
{
    public required DateTime TimestampUtc { get; init; }
    public required string InterfaceName { get; init; }
    public required string NetworkKey { get; init; }         // e.g. "wifi:home-5g", "ethernet:enp3s0", "vpn:tun0"
    public required string NetworkDisplayName { get; init; }    // e.g. "Home-5G", "Ethernet", "VPN (tun0)"
    public required string ConnectionType { get; init; }     // "WiFi", "Ethernet", "VPN", "Cellular", "Virtual", "Other"
    
    public required long RawBytesReceived { get; init; }
    public required long RawBytesSent { get; init; }
    
    public required long DeltaBytesReceived { get; init; }
    public required long DeltaBytesSent { get; init; }
    public long DeltaBytesTotal => DeltaBytesReceived + DeltaBytesSent;
    
    public required double DownloadSpeedBps { get; init; }
    public required double UploadSpeedBps { get; init; }
    public double TotalSpeedBps => DownloadSpeedBps + UploadSpeedBps;
    
    public double ElapsedSeconds { get; init; }
    public bool IsCounterReset { get; init; }
    public bool IsInitialBaseline { get; init; }
}
