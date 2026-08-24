using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

public class LinuxNetworkMonitorService : INetworkMonitorService
{
    private const string ProcNetDevPath = "/proc/net/dev";
    private const string SysClassNetPath = "/sys/class/net";
    
    private readonly ConcurrentDictionary<string, MeasurementSnapshot> _lastMeasurements = new();

    private class MeasurementSnapshot
    {
        public long BytesReceived { get; set; }
        public long BytesSent { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public void ResetMeasurement(string? interfaceName = null)
    {
        if (string.IsNullOrEmpty(interfaceName))
        {
            _lastMeasurements.Clear();
        }
        else
        {
            _lastMeasurements.TryRemove(interfaceName, out _);
        }
    }

    public async Task<IEnumerable<string>> GetAvailableInterfacesAsync()
    {
        var interfaces = new List<string>();

        // 1. Try scanning /sys/class/net (fast and accurate)
        if (Directory.Exists(SysClassNetPath))
        {
            try
            {
                var dirs = Directory.GetDirectories(SysClassNetPath);
                foreach (var dir in dirs)
                {
                    string iface = Path.GetFileName(dir);
                    if (string.IsNullOrEmpty(iface) || iface == "lo") continue;

                    string operstatePath = Path.Combine(dir, "operstate");
                    bool isUp = false;
                    if (File.Exists(operstatePath))
                    {
                        string state = (await File.ReadAllTextAsync(operstatePath)).Trim();
                        isUp = state.Equals("up", StringComparison.OrdinalIgnoreCase);
                    }

                    if (isUp)
                    {
                        interfaces.Insert(0, iface); // Prioritize operational interfaces
                    }
                    else
                    {
                        interfaces.Add(iface);
                    }
                }

                if (interfaces.Count > 0)
                {
                    return interfaces;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error scanning /sys/class/net: {ex}");
            }
        }

        // 2. Fallback to /proc/net/dev
        if (File.Exists(ProcNetDevPath))
        {
            try
            {
                var lines = await File.ReadAllLinesAsync(ProcNetDevPath);
                foreach (var line in lines.Skip(2))
                {
                    var parts = line.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (parts.Length < 2) continue;

                    var interfaceName = parts[0];
                    if (interfaceName == "lo") continue;

                    var stats = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (stats.Length >= 9 &&
                        long.TryParse(stats[0], out long bytesReceived) &&
                        long.TryParse(stats[8], out long bytesSent))
                    {
                        if (bytesReceived > 0 || bytesSent > 0)
                        {
                            interfaces.Add(interfaceName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error scanning /proc/net/dev: {ex}");
            }
        }

        return interfaces;
    }

    public async Task<NetworkUsage?> GetUsageAsync(string interfaceName)
    {
        if (string.IsNullOrWhiteSpace(interfaceName) || interfaceName == "None" || interfaceName == "Disconnected")
            return null;

        long currentBytesReceived = -1;
        long currentBytesSent = -1;

        // 1. Try reading directly from /sys/class/net/<interface>/statistics/
        string sysNetDir = Path.Combine(SysClassNetPath, interfaceName, "statistics");
        string rxPath = Path.Combine(sysNetDir, "rx_bytes");
        string txPath = Path.Combine(sysNetDir, "tx_bytes");

        if (File.Exists(rxPath) && File.Exists(txPath))
        {
            try
            {
                string rxStr = (await File.ReadAllTextAsync(rxPath)).Trim();
                string txStr = (await File.ReadAllTextAsync(txPath)).Trim();

                if (long.TryParse(rxStr, out long rx) && long.TryParse(txStr, out long tx))
                {
                    currentBytesReceived = rx;
                    currentBytesSent = tx;
                }
            }
            catch
            {
                // Fallback to /proc/net/dev
            }
        }

        // 2. Fallback to /proc/net/dev if sysfs was not available
        if (currentBytesReceived < 0 || currentBytesSent < 0)
        {
            if (!File.Exists(ProcNetDevPath))
                return null;

            try
            {
                var lines = await File.ReadAllLinesAsync(ProcNetDevPath);
                var line = lines.Skip(2).FirstOrDefault(l => l.Trim().StartsWith(interfaceName + ":"));
                if (line == null) return null;

                var parts = line.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length < 2) return null;

                var stats = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (stats.Length < 9) return null;

                if (!long.TryParse(stats[0], out currentBytesReceived) || 
                    !long.TryParse(stats[8], out currentBytesSent))
                {
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        var currentTimestamp = DateTime.UtcNow;
        double downloadSpeed = 0;
        double uploadSpeed = 0;

        if (_lastMeasurements.TryGetValue(interfaceName, out var last))
        {
            double timeDiff = (currentTimestamp - last.Timestamp).TotalSeconds;

            // Enforce realistic sample interval (0 < timeDiff <= 10.0)
            if (timeDiff > 0 && timeDiff <= 10.0)
            {
                long rxDelta = currentBytesReceived - last.BytesReceived;
                long txDelta = currentBytesSent - last.BytesSent;

                // Handle counters wrapping or resetting safely
                if (rxDelta < 0) rxDelta = 0;
                if (txDelta < 0) txDelta = 0;

                downloadSpeed = rxDelta / timeDiff;
                uploadSpeed = txDelta / timeDiff;

                // Protect against NaN or Infinity
                if (double.IsNaN(downloadSpeed) || double.IsInfinity(downloadSpeed) || downloadSpeed < 0)
                    downloadSpeed = 0;
                if (double.IsNaN(uploadSpeed) || double.IsInfinity(uploadSpeed) || uploadSpeed < 0)
                    uploadSpeed = 0;
            }
        }

        _lastMeasurements[interfaceName] = new MeasurementSnapshot
        {
            BytesReceived = currentBytesReceived,
            BytesSent = currentBytesSent,
            Timestamp = currentTimestamp
        };

        return new NetworkUsage
        {
            InterfaceName = interfaceName,
            BytesReceived = currentBytesReceived,
            BytesSent = currentBytesSent,
            DownloadSpeed = downloadSpeed,
            UploadSpeed = uploadSpeed,
            Timestamp = currentTimestamp
        };
    }
}
