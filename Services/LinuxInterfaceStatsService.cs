using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace DataSense.Services;

public class LinuxInterfaceStatsService : ILinuxInterfaceStatsService
{
    private readonly ConcurrentDictionary<string, InterfaceSample> _previousSamples = new();

    public Task<IEnumerable<NetworkInterfaceStats>> GetAllInterfaceStatsAsync()
    {
        var result = new List<NetworkInterfaceStats>();

        try
        {
            string sysNetPath = "/sys/class/net";
            if (Directory.Exists(sysNetPath))
            {
                var directories = Directory.GetDirectories(sysNetPath);
                foreach (var dir in directories)
                {
                    string iface = Path.GetFileName(dir);
                    if (string.IsNullOrEmpty(iface) || iface == "lo") continue; // Skip loopback

                    var stats = ReadInterfaceStatsFromSysFs(iface, dir);
                    if (stats != null)
                    {
                        result.Add(stats);
                    }
                }
            }

            // Fallback / complement with System.Net.NetworkInformation if /sys is sparse or on fallback systems
            if (result.Count == 0)
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;

                    var stats = ReadInterfaceStatsFromNic(nic);
                    if (stats != null) result.Add(stats);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed reading interface stats: {ex.Message}");
        }

        return Task.FromResult<IEnumerable<NetworkInterfaceStats>>(result);
    }

    public Task<NetworkInterfaceStats?> GetInterfaceStatsAsync(string interfaceName)
    {
        if (string.IsNullOrWhiteSpace(interfaceName)) return Task.FromResult<NetworkInterfaceStats?>(null);

        string dir = Path.Combine("/sys/class/net", interfaceName);
        if (Directory.Exists(dir))
        {
            var stats = ReadInterfaceStatsFromSysFs(interfaceName, dir);
            return Task.FromResult(stats);
        }

        var nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Name.Equals(interfaceName, StringComparison.OrdinalIgnoreCase));
        if (nic != null)
        {
            return Task.FromResult(ReadInterfaceStatsFromNic(nic));
        }

        return Task.FromResult<NetworkInterfaceStats?>(null);
    }

    private NetworkInterfaceStats? ReadInterfaceStatsFromSysFs(string iface, string dir)
    {
        try
        {
            string state = ReadSysFile(Path.Combine(dir, "operstate")).Trim();
            bool isUp = state.Equals("up", StringComparison.OrdinalIgnoreCase);

            string mac = ReadSysFile(Path.Combine(dir, "address")).Trim();
            if (mac == "00:00:00:00:00:00") mac = "Unavailable";

            string statsDir = Path.Combine(dir, "statistics");
            long rxBytes = ReadSysLong(Path.Combine(statsDir, "rx_bytes"));
            long txBytes = ReadSysLong(Path.Combine(statsDir, "tx_bytes"));
            long rxPackets = ReadSysLong(Path.Combine(statsDir, "rx_packets"));
            long txPackets = ReadSysLong(Path.Combine(statsDir, "tx_packets"));
            long rxErrors = ReadSysLong(Path.Combine(statsDir, "rx_errors"));
            long txErrors = ReadSysLong(Path.Combine(statsDir, "tx_errors"));
            long rxDropped = ReadSysLong(Path.Combine(statsDir, "rx_dropped"));
            long txDropped = ReadSysLong(Path.Combine(statsDir, "tx_dropped"));

            string connType = DetermineConnectionType(iface);
            DateTime now = DateTime.UtcNow;

            double dlRate = 0;
            double ulRate = 0;

            if (_previousSamples.TryGetValue(iface, out var prev))
            {
                double elapsed = (now - prev.Timestamp).TotalSeconds;
                if (elapsed > 0 && elapsed < 10)
                {
                    dlRate = Math.Max(0, (rxBytes - prev.RxBytes) / elapsed);
                    ulRate = Math.Max(0, (txBytes - prev.TxBytes) / elapsed);
                }
            }

            _previousSamples[iface] = new InterfaceSample
            {
                Timestamp = now,
                RxBytes = rxBytes,
                TxBytes = txBytes
            };

            // Get IP addresses using System.Net.NetworkInformation if matching
            string? ipv4 = null;
            string? ipv6 = null;
            var nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Name.Equals(iface, StringComparison.OrdinalIgnoreCase));
            if (nic != null)
            {
                var ipProps = nic.GetIPProperties();
                var v4Addr = ipProps.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                if (v4Addr != null) ipv4 = v4Addr.Address.ToString();

                var v6Addr = ipProps.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6);
                if (v6Addr != null) ipv6 = v6Addr.Address.ToString();
            }

            return new NetworkInterfaceStats
            {
                InterfaceName = iface,
                ConnectionType = connType,
                IsUp = isUp,
                State = string.IsNullOrEmpty(state) ? "Unknown" : state,
                RxBytes = rxBytes,
                TxBytes = txBytes,
                RxPackets = rxPackets,
                TxPackets = txPackets,
                RxErrors = rxErrors,
                TxErrors = txErrors,
                RxDropped = rxDropped,
                TxDropped = txDropped,
                DownloadRateBytesPerSec = dlRate,
                UploadRateBytesPerSec = ulRate,
                MacAddress = string.IsNullOrEmpty(mac) ? "Unavailable" : mac,
                IPv4Address = ipv4 ?? "Unavailable",
                IPv6Address = ipv6 ?? "Unavailable",
                NetworkName = nic?.Description ?? connType,
                LastUpdated = now
            };
        }
        catch
        {
            return null;
        }
    }

    private NetworkInterfaceStats? ReadInterfaceStatsFromNic(NetworkInterface nic)
    {
        try
        {
            var ipStats = nic.GetIPStatistics();
            string iface = nic.Name;
            DateTime now = DateTime.UtcNow;

            double dlRate = 0;
            double ulRate = 0;

            if (_previousSamples.TryGetValue(iface, out var prev))
            {
                double elapsed = (now - prev.Timestamp).TotalSeconds;
                if (elapsed > 0 && elapsed < 10)
                {
                    dlRate = Math.Max(0, (ipStats.BytesReceived - prev.RxBytes) / elapsed);
                    ulRate = Math.Max(0, (ipStats.BytesSent - prev.TxBytes) / elapsed);
                }
            }

            _previousSamples[iface] = new InterfaceSample
            {
                Timestamp = now,
                RxBytes = ipStats.BytesReceived,
                TxBytes = ipStats.BytesSent
            };

            var ipProps = nic.GetIPProperties();
            var v4Addr = ipProps.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            var v6Addr = ipProps.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6);

            return new NetworkInterfaceStats
            {
                InterfaceName = iface,
                ConnectionType = DetermineConnectionType(iface),
                IsUp = nic.OperationalStatus == OperationalStatus.Up,
                State = nic.OperationalStatus.ToString(),
                RxBytes = ipStats.BytesReceived,
                TxBytes = ipStats.BytesSent,
                RxPackets = GetRxPacketsSafe(ipStats),
                TxPackets = GetTxPacketsSafe(ipStats),
                RxErrors = ipStats.IncomingPacketsWithErrors,
                TxErrors = ipStats.OutgoingPacketsWithErrors,
                RxDropped = ipStats.IncomingPacketsDiscarded,
                TxDropped = GetTxDroppedSafe(ipStats),
                DownloadRateBytesPerSec = dlRate,
                UploadRateBytesPerSec = ulRate,
                MacAddress = nic.GetPhysicalAddress().ToString(),
                IPv4Address = v4Addr?.Address.ToString() ?? "Unavailable",
                IPv6Address = v6Addr?.Address.ToString() ?? "Unavailable",
                NetworkName = nic.Description,
                LastUpdated = now
            };
        }
        catch
        {
            return null;
        }
    }

    private static string DetermineConnectionType(string iface)
    {
        if (iface.StartsWith("wlan") || iface.StartsWith("wlp") || iface.StartsWith("wifi")) return "Wi-Fi";
        if (iface.StartsWith("eth") || iface.StartsWith("eno") || iface.StartsWith("enp")) return "Ethernet";
        if (iface.StartsWith("tun") || iface.StartsWith("tap") || iface.StartsWith("wg")) return "VPN";
        if (iface.StartsWith("usb")) return "USB Tethering";
        return "Network";
    }

    private static string ReadSysFile(string path)
    {
        try
        {
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        catch { }
        return string.Empty;
    }

    private static long ReadSysLong(string path)
    {
        string text = ReadSysFile(path).Trim();
        return long.TryParse(text, out long val) ? val : 0;
    }

    private static long GetRxPacketsSafe(IPInterfaceStatistics ipStats)
    {
        try { return ipStats.UnicastPacketsReceived + ipStats.NonUnicastPacketsReceived; }
        catch { return ipStats.UnicastPacketsReceived; }
    }

    private static long GetTxPacketsSafe(IPInterfaceStatistics ipStats)
    {
        try { return ipStats.UnicastPacketsSent; }
        catch { return 0; }
    }

#pragma warning disable CA1416
    private static long GetTxDroppedSafe(IPInterfaceStatistics ipStats)
    {
        try { return ipStats.OutgoingPacketsDiscarded; }
        catch { return 0; }
    }
#pragma warning restore CA1416

    private class InterfaceSample
    {
        public DateTime Timestamp { get; set; }
        public long RxBytes { get; set; }
        public long TxBytes { get; set; }
    }
}
