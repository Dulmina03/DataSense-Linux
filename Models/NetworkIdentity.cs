using System;

namespace DataSense.Models;

public enum NetworkType
{
    Unknown,
    WiFi,
    Ethernet,
    Cellular,
    Loopback,
    Other
}

public sealed class NetworkIdentity
{
    public string? Ssid { get; init; }
    public string? ConnectionName { get; init; }
    public string? InterfaceName { get; init; }
    public NetworkType Type { get; init; }
    public bool IsConnected { get; init; }

    public string DisplayName { get; init; } = "Unknown Network";
    public string CanonicalKey { get; init; } = "unknown network";

    public static NetworkIdentity Disconnected(string? interfaceName = null) => new()
    {
        InterfaceName = interfaceName,
        Type = NetworkType.Unknown,
        IsConnected = false,
        DisplayName = "Disconnected",
        CanonicalKey = "disconnected"
    };

    public override string ToString() => DisplayName;
}
