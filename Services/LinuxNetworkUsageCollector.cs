using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

/// <summary>
/// Multi-interface Linux network collector that reads OS counters from `/sys/class/net` with `/proc/net/dev` fallback.
/// Implements strict interface classification and exclusion policies to prevent double-counting.
/// </summary>
public class LinuxNetworkUsageCollector : INetworkUsageCollector
{
    private const string SysClassNetPath = "/sys/class/net";
    private const string ProcNetDevPath = "/proc/net/dev";

    public async Task<IReadOnlyList<InterfaceRawCounters>> CollectAllInterfacesAsync()
    {
        var result = new List<InterfaceRawCounters>();
        var seenInterfaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Primary path: /sys/class/net
        if (Directory.Exists(SysClassNetPath))
        {
            try
            {
                var directories = Directory.GetDirectories(SysClassNetPath);
                foreach (var dir in directories)
                {
                    string iface = Path.GetFileName(dir);
                    if (string.IsNullOrEmpty(iface) || ShouldExcludeInterface(iface))
                        continue;

                    var counters = await ReadInterfaceCountersFromSysFsAsync(iface, dir);
                    if (counters != null)
                    {
                        result.Add(counters);
                        seenInterfaces.Add(iface);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LinuxNetworkUsageCollector] Error reading /sys/class/net: {ex.Message}");
            }
        }

        // 2. Fallback / Complementary path: /proc/net/dev if sysfs was unavailable or empty
        if (result.Count == 0 && File.Exists(ProcNetDevPath))
        {
            try
            {
                var procCounters = await ReadCountersFromProcNetDevAsync();
                foreach (var counter in procCounters)
                {
                    if (!ShouldExcludeInterface(counter.InterfaceName) && seenInterfaces.Add(counter.InterfaceName))
                    {
                        result.Add(counter);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LinuxNetworkUsageCollector] Error reading /proc/net/dev: {ex.Message}");
            }
        }

        return result;
    }

    public async Task<InterfaceRawCounters?> CollectInterfaceAsync(string interfaceName)
    {
        if (string.IsNullOrWhiteSpace(interfaceName) || ShouldExcludeInterface(interfaceName))
            return null;

        string dir = Path.Combine(SysClassNetPath, interfaceName);
        if (Directory.Exists(dir))
        {
            return await ReadInterfaceCountersFromSysFsAsync(interfaceName, dir);
        }

        if (File.Exists(ProcNetDevPath))
        {
            var procCounters = await ReadCountersFromProcNetDevAsync();
            return procCounters.FirstOrDefault(c => c.InterfaceName.Equals(interfaceName, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    /// <summary>
    /// Evaluates interface inclusion/exclusion policy.
    /// Excludes loopback and container veth/bridge interfaces that cause double-counting.
    /// </summary>
    public static bool ShouldExcludeInterface(string iface)
    {
        if (string.IsNullOrWhiteSpace(iface)) return true;

        string name = iface.Trim().ToLowerInvariant();

        // Always exclude loopback
        if (name == "lo") return true;

        // Exclude Docker and container veth pairs to avoid double-counting bridged packets
        if (name.StartsWith("veth") ||
            name.StartsWith("docker") ||
            name.StartsWith("br-") ||
            name.StartsWith("virbr"))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Classifies the connection type from the interface name and sysfs attributes.
    /// </summary>
    public static string ClassifyConnectionType(string iface, string? dir = null)
    {
        if (string.IsNullOrWhiteSpace(iface)) return "Other";
        string name = iface.Trim().ToLowerInvariant();

        // Check wireless sysfs folder if available
        if (!string.IsNullOrEmpty(dir) && (Directory.Exists(Path.Combine(dir, "wireless")) || Directory.Exists(Path.Combine(dir, "phy80211"))))
        {
            return "WiFi";
        }

        if (name.StartsWith("wl") || name.StartsWith("wlan") || name.StartsWith("wifi"))
            return "WiFi";

        if (name.StartsWith("eth") || name.StartsWith("en") || name.StartsWith("lan"))
            return "Ethernet";

        if (name.StartsWith("tun") || name.StartsWith("tap") || name.StartsWith("wg") || name.StartsWith("ppp"))
            return "VPN";

        if (name.StartsWith("usb"))
            return "Cellular"; // USB tethering / modem

        if (name.StartsWith("ww") || name.StartsWith("wwan"))
            return "Cellular";

        return "Other";
    }

    private static async Task<InterfaceRawCounters?> ReadInterfaceCountersFromSysFsAsync(string iface, string dir)
    {
        try
        {
            string operstatePath = Path.Combine(dir, "operstate");
            string operstate = File.Exists(operstatePath) ? (await File.ReadAllTextAsync(operstatePath)).Trim() : "unknown";
            bool isUp = operstate.Equals("up", StringComparison.OrdinalIgnoreCase) || operstate.Equals("unknown", StringComparison.OrdinalIgnoreCase);

            string statsDir = Path.Combine(dir, "statistics");
            if (!Directory.Exists(statsDir))
            {
                return null;
            }

            long rxBytes = ReadSysLong(Path.Combine(statsDir, "rx_bytes"));
            long txBytes = ReadSysLong(Path.Combine(statsDir, "tx_bytes"));
            long rxPackets = ReadSysLong(Path.Combine(statsDir, "rx_packets"));
            long txPackets = ReadSysLong(Path.Combine(statsDir, "tx_packets"));
            long rxErrors = ReadSysLong(Path.Combine(statsDir, "rx_errors"));
            long txErrors = ReadSysLong(Path.Combine(statsDir, "tx_errors"));
            long rxDropped = ReadSysLong(Path.Combine(statsDir, "rx_dropped"));
            long txDropped = ReadSysLong(Path.Combine(statsDir, "tx_dropped"));

            // If the interface is down and has zero traffic, we can skip it
            if (!isUp && rxBytes == 0 && txBytes == 0)
            {
                return null;
            }

            string mac = string.Empty;
            string addressPath = Path.Combine(dir, "address");
            if (File.Exists(addressPath))
            {
                mac = (await File.ReadAllTextAsync(addressPath)).Trim();
            }

            return new InterfaceRawCounters
            {
                InterfaceName = iface,
                TimestampUtc = DateTime.UtcNow,
                RawBytesReceived = rxBytes,
                RawBytesSent = txBytes,
                IsOperational = isUp,
                ConnectionType = ClassifyConnectionType(iface, dir),
                MacAddress = string.IsNullOrEmpty(mac) || mac == "00:00:00:00:00:00" ? null : mac,
                RawPacketsReceived = rxPackets,
                RawPacketsSent = txPackets,
                RawRxErrors = rxErrors,
                RawTxErrors = txErrors,
                RawRxDropped = rxDropped,
                RawTxDropped = txDropped
            };
        }
        catch
        {
            return null;
        }
    }

    private static async Task<List<InterfaceRawCounters>> ReadCountersFromProcNetDevAsync()
    {
        var list = new List<InterfaceRawCounters>();
        if (!File.Exists(ProcNetDevPath)) return list;

        var lines = await File.ReadAllLinesAsync(ProcNetDevPath);
        var now = DateTime.UtcNow;

        foreach (var line in lines.Skip(2))
        {
            var parts = line.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2) continue;

            string iface = parts[0];
            var stats = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (stats.Length < 16) continue;

            if (long.TryParse(stats[0], out long rxBytes) &&
                long.TryParse(stats[8], out long txBytes))
            {
                _ = long.TryParse(stats[1], out long rxPackets);
                _ = long.TryParse(stats[2], out long rxErrors);
                _ = long.TryParse(stats[3], out long rxDrop);
                _ = long.TryParse(stats[9], out long txPackets);
                _ = long.TryParse(stats[10], out long txErrors);
                _ = long.TryParse(stats[11], out long txDrop);

                list.Add(new InterfaceRawCounters
                {
                    InterfaceName = iface,
                    TimestampUtc = now,
                    RawBytesReceived = rxBytes,
                    RawBytesSent = txBytes,
                    IsOperational = rxBytes > 0 || txBytes > 0,
                    ConnectionType = ClassifyConnectionType(iface),
                    RawPacketsReceived = rxPackets,
                    RawPacketsSent = txPackets,
                    RawRxErrors = rxErrors,
                    RawTxErrors = txErrors,
                    RawRxDropped = rxDrop,
                    RawTxDropped = txDrop
                });
            }
        }

        return list;
    }

    private static long ReadSysLong(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                string text = File.ReadAllText(path).Trim();
                if (long.TryParse(text, out long val))
                    return val;
            }
        }
        catch { }
        return 0;
    }
}
