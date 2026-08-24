using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using DataSense.Helpers;
using DataSense.Models;

namespace DataSense.Services;

public class NetworkIdentityService : INetworkIdentityService
{
    private readonly INetworkConnectionService _connectionService;
    private static readonly ConcurrentDictionary<string, NetworkIdentity> _lastKnownCache = new(StringComparer.OrdinalIgnoreCase);

    public NetworkIdentityService(INetworkConnectionService connectionService)
    {
        _connectionService = connectionService;
    }

    public async Task<NetworkIdentity> GetCurrentIdentityAsync(string interfaceName)
    {
        if (string.IsNullOrWhiteSpace(interfaceName) ||
            interfaceName.Equals("None", StringComparison.OrdinalIgnoreCase) ||
            interfaceName.Equals("Disconnected", StringComparison.OrdinalIgnoreCase))
        {
            return NetworkIdentity.Disconnected(interfaceName);
        }

        try
        {
            var details = await _connectionService.GetConnectionDetailsAsync(interfaceName);
            bool isWifi = details.ConnectionType.Equals("wifi", StringComparison.OrdinalIgnoreCase) ||
                          interfaceName.StartsWith("wl", StringComparison.OrdinalIgnoreCase);
            bool isEthernet = details.ConnectionType.Equals("ethernet", StringComparison.OrdinalIgnoreCase) ||
                              interfaceName.StartsWith("en", StringComparison.OrdinalIgnoreCase) ||
                              interfaceName.StartsWith("eth", StringComparison.OrdinalIgnoreCase);

            string? validSsid = null;
            if (NetworkIdentityValidator.IsValidNetworkName(details.WifiSsid))
            {
                validSsid = details.WifiSsid.Trim();
            }

            string? validConnName = null;
            if (NetworkIdentityValidator.IsValidNetworkName(details.ConnectionName) &&
                !details.ConnectionName.StartsWith("Wired connection", StringComparison.OrdinalIgnoreCase) &&
                !details.ConnectionName.StartsWith("Wired Connection", StringComparison.OrdinalIgnoreCase))
            {
                validConnName = details.ConnectionName.Trim();
            }

            // Priority 1: Active Wi-Fi SSID
            if (!string.IsNullOrEmpty(validSsid))
            {
                var identity = new NetworkIdentity
                {
                    Ssid = validSsid,
                    ConnectionName = validConnName ?? validSsid,
                    InterfaceName = interfaceName,
                    Type = NetworkType.WiFi,
                    IsConnected = true,
                    DisplayName = validSsid,
                    CanonicalKey = validSsid.ToLowerInvariant()
                };

                _lastKnownCache[interfaceName] = identity;
                return identity;
            }

            // Priority 2: Valid NetworkManager Connection Profile Name
            if (!string.IsNullOrEmpty(validConnName))
            {
                var type = isWifi ? NetworkType.WiFi : (isEthernet ? NetworkType.Ethernet : NetworkType.Other);
                var identity = new NetworkIdentity
                {
                    Ssid = isWifi ? validConnName : null,
                    ConnectionName = validConnName,
                    InterfaceName = interfaceName,
                    Type = type,
                    IsConnected = true,
                    DisplayName = validConnName,
                    CanonicalKey = validConnName.ToLowerInvariant()
                };

                _lastKnownCache[interfaceName] = identity;
                return identity;
            }

            // Priority 3: Ethernet
            if (isEthernet)
            {
                var identity = new NetworkIdentity
                {
                    ConnectionName = "Ethernet",
                    InterfaceName = interfaceName,
                    Type = NetworkType.Ethernet,
                    IsConnected = true,
                    DisplayName = "Ethernet",
                    CanonicalKey = "ethernet"
                };

                _lastKnownCache[interfaceName] = identity;
                return identity;
            }

            // Priority 4: Last-known cached identity (protects during temporary SSID query dropouts)
            if (_lastKnownCache.TryGetValue(interfaceName, out var cached) &&
                NetworkIdentityValidator.IsValidNetworkName(cached.DisplayName))
            {
                return cached;
            }

            // Priority 5: Interface fallback
            string fallbackName = $"Interface: {interfaceName.Trim()}";
            return new NetworkIdentity
            {
                InterfaceName = interfaceName,
                Type = isWifi ? NetworkType.WiFi : NetworkType.Unknown,
                IsConnected = true,
                DisplayName = fallbackName,
                CanonicalKey = fallbackName.ToLowerInvariant()
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NetworkIdentityService resolution error: {ex.Message}");
            if (_lastKnownCache.TryGetValue(interfaceName, out var cached) &&
                NetworkIdentityValidator.IsValidNetworkName(cached.DisplayName))
            {
                return cached;
            }

            string fallbackName = $"Interface: {interfaceName.Trim()}";
            return new NetworkIdentity
            {
                InterfaceName = interfaceName,
                Type = NetworkType.Unknown,
                IsConnected = true,
                DisplayName = fallbackName,
                CanonicalKey = fallbackName.ToLowerInvariant()
            };
        }
    }

    public NetworkIdentity GetLastKnownIdentity(string interfaceName)
    {
        if (!string.IsNullOrWhiteSpace(interfaceName) &&
            _lastKnownCache.TryGetValue(interfaceName, out var cached))
        {
            return cached;
        }

        return NetworkIdentity.Disconnected(interfaceName);
    }

    public string NormalizeNetworkName(string? rawName, string? interfaceName = null)
    {
        if (IsValidNetworkName(rawName))
        {
            return rawName!.Trim();
        }

        // If rawName is a placeholder or interface fallback, check if this interface has a known valid network identity
        if (!string.IsNullOrWhiteSpace(interfaceName) &&
            _lastKnownCache.TryGetValue(interfaceName, out var cached) &&
            IsValidNetworkName(cached.DisplayName))
        {
            return cached.DisplayName.Trim();
        }

        return NetworkIdentityValidator.NormalizeNetworkName(rawName, interfaceName);
    }

    public string GetCanonicalKey(string? rawName, string? interfaceName = null)
    {
        string normalized = NormalizeNetworkName(rawName, interfaceName);
        return normalized.ToLowerInvariant().Trim();
    }

    public bool IsValidNetworkName(string? name)
    {
        return NetworkIdentityValidator.IsValidNetworkName(name);
    }
}
