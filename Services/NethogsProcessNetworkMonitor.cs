using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DataSense.Helpers;
using DataSense.Models;

namespace DataSense.Services;

public class NethogsProcessNetworkMonitor : IProcessNetworkMonitor
{
    private readonly ILinuxPlatformService? _platformService;
    private readonly ILinuxProcessResolver? _processResolver;

    public NethogsProcessNetworkMonitor(
        ILinuxPlatformService? platformService = null,
        ILinuxProcessResolver? processResolver = null)
    {
        _platformService = platformService;
        _processResolver = processResolver;
    }

    public string NethogsPath => _platformService?.GetExecutablePath("nethogs") ?? "nethogs";

    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            if (_platformService != null)
            {
                return _platformService.HasNethogs;
            }

            var result = await ProcessExecutionHelper.ExecuteAsync("which", new[] { "nethogs" }, timeoutMs: 1500);
            return result.Success;
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
            string nethogsExec = _platformService?.GetExecutablePath("nethogs") ?? "nethogs";
            var result = await ProcessExecutionHelper.ExecuteAsync(nethogsExec, new[] { "-t", "-c", "1" }, timeoutMs: 2000);

            if (!result.Success || result.StandardError.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) || result.StandardError.Contains("root", StringComparison.OrdinalIgnoreCase))
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
        string nethogsExec = _platformService?.GetExecutablePath("nethogs") ?? "nethogs";

        // Use ProcessStartInfo.ArgumentList (no shell execution) per security policy
        var psi = new ProcessStartInfo
        {
            FileName = nethogsExec,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        // ArgumentList: machine-readable trace mode
        psi.ArgumentList.Add("-t");

        using var process = Process.Start(psi);
        if (process == null) yield break;

        // Ensure process is killed when token is cancelled
        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        });

        using var reader = process.StandardOutput;
        var currentBatch = new List<ProcessNetworkUsage>();

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            if (line == null) break; // EOF — nethogs process exited
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

    /// <summary>
    /// Parses a single nethogs trace-mode output line.
    /// Format: /path/to/exec/PID/user\tSENT_KB/s\tRECV_KB/s
    /// </summary>
    public ProcessNetworkUsage? ParseNethogsLine(string line)
    {
        var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        if (parts.Length < 3) return null;

        string identifierPart = parts[0];
        var idParts = identifierPart.Split('/');
        if (idParts.Length < 4) return null;

        // nethogs trace format is /path/to/exec/PID/USER_OR_UID
        int pidIndex = idParts.Length - 2;
        if (pidIndex <= 0 || !int.TryParse(idParts[pidIndex], out int pid)) return null;

        string executablePath = string.Join("/", idParts[0..pidIndex]);
        string processName = Path.GetFileName(executablePath);

        if (string.IsNullOrWhiteSpace(processName) || processName == "unknown program")
        {
            processName = "unknown";
        }

        string user = idParts[idParts.Length - 1];

        if (!double.TryParse(parts[1], out double sentKbps)) return null;
        if (!double.TryParse(parts[2], out double recvKbps)) return null;

        // Attempt richer identity from /proc if resolver is available
        string identityKey = $"{processName}_{pid}_0";
        string resolvedExePath = executablePath;
        string resolvedUser = user;

        if (_processResolver != null)
        {
            var identity = _processResolver.ResolveProcessIdentity(pid);
            if (identity != null)
            {
                if (!string.IsNullOrEmpty(identity.ProcessName) && identity.ProcessName != $"pid_{pid}")
                {
                    processName = identity.ProcessName;
                }
                if (!string.IsNullOrEmpty(identity.ExecutablePath))
                {
                    resolvedExePath = identity.ExecutablePath;
                }
                if (!string.IsNullOrEmpty(identity.UserName) && identity.UserName != "unknown")
                {
                    resolvedUser = identity.UserName;
                }
                identityKey = identity.CompositeKey;
            }
        }

        return new ProcessNetworkUsage
        {
            ProcessIdentifier = processName,
            ExecutablePath = resolvedExePath,
            Pid = pid,
            User = resolvedUser,
            UploadRateBytesPerSec = sentKbps * 1024,
            DownloadRateBytesPerSec = recvKbps * 1024,
            Timestamp = DateTime.UtcNow,
            DataSource = "Nethogs",
            ProcessIdentityKey = identityKey
        };
    }
}
