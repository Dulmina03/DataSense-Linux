using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

public class LinuxNetworkMonitorService : INetworkMonitorService
{
    private const string ProcNetDevPath = "/proc/net/dev";
    private readonly Dictionary<string, (long bytesReceived, long bytesSent, DateTime timestamp)> _lastMeasurements = new();
    private readonly object _lock = new();

    public async Task<IEnumerable<string>> GetAvailableInterfacesAsync()
    {
        var interfaces = new List<string>();

        if (!File.Exists(ProcNetDevPath))
            return interfaces;

        var lines = await File.ReadAllLinesAsync(ProcNetDevPath);
        
        // Skip header lines (first 2)
        foreach (var line in lines.Skip(2))
        {
            var parts = line.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2) continue;

            var interfaceName = parts[0];
            
            // Ignore loopback
            if (interfaceName == "lo") continue;

            // Get stats to check if there is any traffic
            var stats = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (stats.Length >= 9)
            {
                if (long.TryParse(stats[0], out long bytesReceived) && 
                    long.TryParse(stats[8], out long bytesSent))
                {
                    // Basic filter: only include interfaces that have seen some traffic
                    if (bytesReceived > 0 || bytesSent > 0)
                    {
                        interfaces.Add(interfaceName);
                    }
                }
            }
        }

        return interfaces;
    }

    public async Task<NetworkUsage?> GetUsageAsync(string interfaceName)
    {
        if (!File.Exists(ProcNetDevPath))
            return null;

        var lines = await File.ReadAllLinesAsync(ProcNetDevPath);
        var line = lines.Skip(2).FirstOrDefault(l => l.Trim().StartsWith(interfaceName + ":"));

        if (line == null)
            return null;

        var parts = line.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return null;

        var stats = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (stats.Length < 9) return null;

        if (!long.TryParse(stats[0], out long currentBytesReceived) || 
            !long.TryParse(stats[8], out long currentBytesSent))
        {
            return null;
        }

        var currentTimestamp = DateTime.UtcNow;
        double downloadSpeed = 0;
        double uploadSpeed = 0;

        lock (_lock)
        {
            if (_lastMeasurements.TryGetValue(interfaceName, out var last))
            {
                var timeDiff = (currentTimestamp - last.timestamp).TotalSeconds;
                if (timeDiff > 0)
                {
                    downloadSpeed = (currentBytesReceived - last.bytesReceived) / timeDiff;
                    uploadSpeed = (currentBytesSent - last.bytesSent) / timeDiff;

                    // Handle counters wrapping or resetting
                    if (downloadSpeed < 0) downloadSpeed = 0;
                    if (uploadSpeed < 0) uploadSpeed = 0;
                }
            }

            _lastMeasurements[interfaceName] = (currentBytesReceived, currentBytesSent, currentTimestamp);
        }

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
