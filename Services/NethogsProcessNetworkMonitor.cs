using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

public class NethogsProcessNetworkMonitor : IProcessNetworkMonitor
{
    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "which",
                Arguments = "nethogs",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> HasPermissionsAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "nethogs",
                // -t = trace mode (machine readable)
                // -c 1 = run for 1 cycle and exit
                Arguments = "-t -c 1",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null) return false;
            
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            // nethogs returns non-zero if it lacks cap_net_admin / cap_net_raw or root
            if (process.ExitCode != 0 || stderr.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) || stderr.Contains("root", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async IAsyncEnumerable<IEnumerable<ProcessNetworkUsage>> StartMonitoringAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "nethogs",
            Arguments = "-t", // Trace mode: output is machine-readable
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) yield break;

        // Ensure process is killed when token is cancelled
        using var registration = cancellationToken.Register(() => 
        {
            try { if (!process.HasExited) process.Kill(); } catch { }
        });

        using var reader = process.StandardOutput;
        var currentBatch = new List<ProcessNetworkUsage>();

        while (!cancellationToken.IsCancellationRequested && !reader.EndOfStream)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith("Refreshing:"))
            {
                // Yield the previous batch and start a new one
                if (currentBatch.Count > 0)
                {
                    yield return new List<ProcessNetworkUsage>(currentBatch);
                    currentBatch.Clear();
                }
                continue;
            }

            var usage = ParseNethogsLine(line);
            if (usage != null)
            {
                currentBatch.Add(usage);
            }
        }

        if (currentBatch.Count > 0)
        {
            yield return currentBatch;
        }
    }

    private ProcessNetworkUsage? ParseNethogsLine(string line)
    {
        // Format is typically:
        // /path/to/executable/PID/USER/DEV SENT_KBPS RECV_KBPS
        // OR
        // processname/PID/USER SENT_KBPS RECV_KBPS

        var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) 
        {
            parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        if (parts.Length < 3) return null;

        string identifierPart = parts[0];
        
        // nethogs uses '/' to separate parts in the identifier.
        // It can be tricky because the executable path itself contains '/'.
        // Let's split by '/' and take the last few components as DEV, USER, PID.
        var idParts = identifierPart.Split('/');
        
        if (idParts.Length < 4) return null; // We need at least executable, pid, user, dev

        // The DEV part is often the last one, or sometimes it's missing.
        // Usually: [...path...]/[executable]/[PID]/[USER]/[DEV] or similar.
        // To be safe, we'll try to find the PID by checking which component is numeric.
        int pidIndex = -1;
        for (int i = idParts.Length - 1; i >= 0; i--)
        {
            if (int.TryParse(idParts[i], out _))
            {
                pidIndex = i;
                break;
            }
        }

        if (pidIndex == -1 || pidIndex == 0) return null;

        string executablePath = string.Join("/", idParts[0..pidIndex]);
        string processName = Path.GetFileName(executablePath);
        
        // Handle cases like "unknown program" or missing name
        if (string.IsNullOrWhiteSpace(processName) || processName == "unknown program")
        {
            processName = "unknown";
        }

        int pid = int.Parse(idParts[pidIndex]);
        string user = idParts.Length > pidIndex + 1 ? idParts[pidIndex + 1] : "unknown";

        if (!double.TryParse(parts[1], out double sentKbps)) return null;
        if (!double.TryParse(parts[2], out double recvKbps)) return null;

        return new ProcessNetworkUsage
        {
            ProcessIdentifier = processName,
            ExecutablePath = executablePath,
            Pid = pid,
            User = user,
            // Convert KB/s to Bytes/s
            UploadRateBytesPerSec = sentKbps * 1024,
            DownloadRateBytesPerSec = recvKbps * 1024,
            Timestamp = DateTime.UtcNow
        };
    }
}
