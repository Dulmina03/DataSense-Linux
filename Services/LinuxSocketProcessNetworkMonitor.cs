using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DataSense.Helpers;
using DataSense.Models;

namespace DataSense.Services;

/// <summary>
/// Native Linux process network monitor that uses system socket statistics (`ss` / `/proc`)
/// to monitor per-process network bandwidth and cumulative traffic without requiring external nethogs binary or root permissions.
/// </summary>
public class LinuxSocketProcessNetworkMonitor : IProcessNetworkMonitor
{
    private readonly ILinuxPlatformService? _platformService;
    private readonly ILinuxProcessResolver? _processResolver;

    private readonly ConcurrentDictionary<string, ProcessSocketState> _processStates = new();
    private DateTime _lastSampleTime = DateTime.MinValue;

    public LinuxSocketProcessNetworkMonitor(
        ILinuxPlatformService? platformService = null,
        ILinuxProcessResolver? processResolver = null)
    {
        _platformService = platformService;
        _processResolver = processResolver;
    }

    public string NethogsPath => "ss/procfs";

    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var result = await ProcessExecutionHelper.ExecuteAsync("which", new[] { "ss" }, timeoutMs: 1500);
            if (result.Success) return true;
            return File.Exists("/proc/net/tcp") || File.Exists("/proc/net/dev");
        }
        catch
        {
            return false;
        }
    }

    public Task<bool> HasPermissionsAsync()
    {
        // Standard user permissions are sufficient for ss -t -u -p -i on user processes
        return Task.FromResult(true);
    }

    public async IAsyncEnumerable<IEnumerable<ProcessNetworkUsage>> StartMonitoringAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        System.Diagnostics.Debug.WriteLine("[LinuxSocketProcessNetworkMonitor] Started monitoring loop.");

        while (!cancellationToken.IsCancellationRequested)
        {
            List<ProcessNetworkUsage> sampleBatch = new();
            try
            {
                sampleBatch = await CaptureSampleAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LinuxSocketProcessNetworkMonitor] Sample capture error: {ex.Message}");
            }

            if (sampleBatch.Count > 0)
            {
                yield return sampleBatch;
            }

            try
            {
                await Task.Delay(1500, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<List<ProcessNetworkUsage>> CaptureSampleAsync()
    {
        DateTime now = DateTime.UtcNow;
        double elapsedSeconds = _lastSampleTime != DateTime.MinValue ? (now - _lastSampleTime).TotalSeconds : 1.5;
        if (elapsedSeconds <= 0) elapsedSeconds = 1.0;
        _lastSampleTime = now;

        var currentProcessSockets = new Dictionary<int, (string Name, long SentBytes, long RecvBytes)>();

        // Execute `ss -t -u -p -i` to query active sockets with TCP info metrics
        var ssResult = await ProcessExecutionHelper.ExecuteAsync("ss", new[] { "-t", "-u", "-p", "-i" }, timeoutMs: 2500);
        if (ssResult.Success && !string.IsNullOrWhiteSpace(ssResult.StandardOutput))
        {
            ParseSsOutput(ssResult.StandardOutput, currentProcessSockets);
        }

        var results = new List<ProcessNetworkUsage>();

        foreach (var kvp in currentProcessSockets)
        {
            int pid = kvp.Key;
            string rawName = kvp.Value.Name;
            long rawSent = kvp.Value.SentBytes;
            long rawRecv = kvp.Value.RecvBytes;

            // Resolve process identity details via /proc
            string processName = rawName;
            string execPath = string.Empty;
            string user = "unknown";
            string identityKey = $"{processName}_{pid}_0";

            if (_processResolver != null)
            {
                var identity = _processResolver.ResolveProcessIdentity(pid);
                if (identity != null)
                {
                    if (!string.IsNullOrEmpty(identity.ProcessName) && !identity.ProcessName.StartsWith("pid_"))
                    {
                        processName = identity.ProcessName;
                    }
                    execPath = identity.ExecutablePath;
                    user = identity.UserName;
                    identityKey = identity.CompositeKey;
                }
            }

            if (!_processStates.TryGetValue(identityKey, out var state))
            {
                state = new ProcessSocketState
                {
                    IdentityKey = identityKey,
                    ProcessName = processName,
                    Pid = pid,
                    LastRawSent = rawSent,
                    LastRawRecv = rawRecv,
                    CumulativeSent = rawSent,
                    CumulativeRecv = rawRecv,
                    CurrentUploadRate = 0,
                    CurrentDownloadRate = 0,
                    LastUpdate = now
                };
                _processStates[identityKey] = state;
            }
            else
            {
                double processElapsed = (now - state.LastUpdate).TotalSeconds;
                if (processElapsed <= 0) processElapsed = elapsedSeconds > 0 ? elapsedSeconds : 1.0;

                long sentDelta = Math.Max(0, rawSent - state.LastRawSent);
                long recvDelta = Math.Max(0, rawRecv - state.LastRawRecv);

                state.LastRawSent = rawSent;
                state.LastRawRecv = rawRecv;
                state.CumulativeSent += sentDelta;
                state.CumulativeRecv += recvDelta;

                state.CurrentUploadRate = sentDelta / processElapsed;
                state.CurrentDownloadRate = recvDelta / processElapsed;
                state.LastUpdate = now;
            }

            results.Add(new ProcessNetworkUsage
            {
                ProcessIdentifier = processName,
                ExecutablePath = execPath,
                Pid = pid,
                User = user,
                DownloadRateBytesPerSec = state.CurrentDownloadRate,
                UploadRateBytesPerSec = state.CurrentUploadRate,
                DownloadBytes = state.CumulativeRecv,
                UploadBytes = state.CumulativeSent,
                Timestamp = now,
                DataSource = "Linux Socket Monitor",
                ProcessIdentityKey = identityKey
            });
        }

        // Clean up stale processes not updated in > 60 seconds
        var staleKeys = _processStates.Where(kvp => (now - kvp.Value.LastUpdate).TotalSeconds > 60).Select(kvp => kvp.Key).ToList();
        foreach (var key in staleKeys)
        {
            _processStates.TryRemove(key, out _);
        }

        return results;
    }

    private static void ParseSsOutput(string output, Dictionary<int, (string Name, long SentBytes, long RecvBytes)> processSockets)
    {
        var lines = output.Split('\n');
        List<(string Name, int Pid)> currentPids = new();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.Contains("users:(("))
            {
                currentPids.Clear();
                var matches = Regex.Matches(line, @"users:\(\(\""([^\""]+)\"",pid=(\d+)");
                foreach (Match match in matches)
                {
                    if (match.Groups.Count >= 3 && int.TryParse(match.Groups[2].Value, out int pid))
                    {
                        string pName = NormalizeProcessName(match.Groups[1].Value);
                        currentPids.Add((pName, pid));
                    }
                }
            }

            if (currentPids.Count > 0)
            {
                long sent = 0;
                long recv = 0;

                var sentMatch = Regex.Match(line, @"bytes_sent:(\d+)");
                if (sentMatch.Success && long.TryParse(sentMatch.Groups[1].Value, out long sVal))
                {
                    sent = sVal;
                }

                var recvMatch = Regex.Match(line, @"bytes_received:(\d+)");
                if (recvMatch.Success && long.TryParse(recvMatch.Groups[1].Value, out long rVal))
                {
                    recv = rVal;
                }

                if (sent > 0 || recv > 0)
                {
                    foreach (var (pName, pid) in currentPids)
                    {
                        if (!processSockets.TryGetValue(pid, out var existing))
                        {
                            processSockets[pid] = (pName, sent, recv);
                        }
                        else
                        {
                            processSockets[pid] = (existing.Name, existing.SentBytes + sent, existing.RecvBytes + recv);
                        }
                    }
                }
            }
        }
    }

    private static string NormalizeProcessName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "unknown";
        int spaceIndex = name.IndexOf(' ');
        if (spaceIndex > 0)
        {
            name = name.Substring(0, spaceIndex);
        }
        string cleanName = System.IO.Path.GetFileName(name);
        return string.IsNullOrWhiteSpace(cleanName) ? name : cleanName;
    }

    private class ProcessSocketState
    {
        public string IdentityKey { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public int Pid { get; set; }
        public long LastRawSent { get; set; }
        public long LastRawRecv { get; set; }
        public long CumulativeSent { get; set; }
        public long CumulativeRecv { get; set; }
        public double CurrentDownloadRate { get; set; }
        public double CurrentUploadRate { get; set; }
        public DateTime LastUpdate { get; set; }
    }
}
