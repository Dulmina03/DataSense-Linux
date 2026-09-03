using System;

namespace DataSense.Models;

/// <summary>
/// Immutable model representing a raw point-in-time sample of kernel network counters for an interface.
/// </summary>
public record InterfaceRawCounters
{
    public required string InterfaceName { get; init; }
    public required DateTime TimestampUtc { get; init; }
    public required long RawBytesReceived { get; init; }
    public required long RawBytesSent { get; init; }
    public required bool IsOperational { get; init; }
    public required string ConnectionType { get; init; } // "WiFi", "Ethernet", "VPN", "Cellular", "Virtual", "Other"
    public string? MacAddress { get; init; }
    public string? IPv4Address { get; init; }
    public string? IPv6Address { get; init; }
    public long RawPacketsReceived { get; init; }
    public long RawPacketsSent { get; init; }
    public long RawRxErrors { get; init; }
    public long RawTxErrors { get; init; }
    public long RawRxDropped { get; init; }
    public long RawTxDropped { get; init; }
}
