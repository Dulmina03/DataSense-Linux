using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataSense.Database;
using DataSense.Models;

namespace DataSense.Services;

public class ProcessNetworkMonitorWorker : IDisposable
{
    private readonly IProcessNetworkMonitor _monitor;
    private readonly INetworkUsageRepository _repository;
    private readonly ISystemHealthRegistry? _healthRegistry;
    private readonly IEventService? _eventService;
    private readonly ILinuxProcessResolver? _processResolver;
    private readonly IApplicationAnalyticsService? _analyticsService;
    private CancellationTokenSource? _cts;
    private Task? _workerTask;
    private bool _isPaused;
    private bool _disposed;

    public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;
    public bool IsPaused => _isPaused;
    public DateTime? LastSuccessfulSample { get; private set; }
    public int TrackedProcessCount => _activeProcesses.Count;
    public string MonitoringStatus { get; private set; } = "Not started";
    public string? LastError { get; private set; }
    public int RestartAttempts { get; private set; }
    public IProcessNetworkMonitor Monitor => _monitor;

    // Process state tracking with PID-reuse safety
    private readonly ConcurrentDictionary<string, ProcessState> _activeProcesses = new();

    // Flush interval management
    private const int FlushIntervalSeconds = 10;
    private const int MaxElapsedSecondsPerSample = 10;
    private const int StaleProcessTimeoutSeconds = 120;

    // UI can subscribe to this for the live Process Network Traffic table
    public event Action<IEnumerable<ProcessNetworkUsage>>? LiveTrafficUpdated;

    public ProcessNetworkMonitorWorker(
        IProcessNetworkMonitor monitor,
        INetworkUsageRepository repository,
        ISystemHealthRegistry? healthRegistry = null,
        IEventService? eventService = null,
        ILinuxProcessResolver? processResolver = null,
        IApplicationAnalyticsService? analyticsService = null)
    {
        _monitor = monitor;
        _repository = repository;
        _healthRegistry = healthRegistry;
        _eventService = eventService;
        _processResolver = processResolver;
        _analyticsService = analyticsService;

        _healthRegistry?.RegisterSubsystem("ProcessMonitor");
    }

    public void Start()
    {
        if (_cts != null) return;
        _isPaused = false;
        _cts = new CancellationTokenSource();
        _workerTask = Task.Run(() => RunAsync(_cts.Token), _cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _workerTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch { /* ignored */ }
        _cts?.Dispose();
        _cts = null;
        MonitoringStatus = "Stopped";
    }

    public void Pause()
    {
        _isPaused = true;
        MonitoringStatus = "Paused";
        _healthRegistry?.ReportHealth("ProcessMonitor", SubsystemState.Healthy, "Monitoring paused by user");
    }

    public void Resume()
    {
        _isPaused = false;
        MonitoringStatus = "Running";
        _healthRegistry?.ReportHealth("ProcessMonitor", SubsystemState.Healthy, "Monitoring resumed");
        _ = _analyticsService?.InvalidateCacheAsync();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        // Check availability
        bool isAvailable = false;
        try
        {
            isAvailable = await _monitor.IsAvailableAsync();
        }
        catch
        {
            isAvailable = false;
        }

        if (!isAvailable)
        {
            MonitoringStatus = "Unavailable";
            LastError = "nethogs executable not found on the system";
            _healthRegistry?.ReportHealth("ProcessMonitor", SubsystemState.Unavailable, "nethogs not installed");
            PublishProcessMonitorEvent(DataSenseEventType.ProcessMonitorUnavailable,
                "Process Monitor Unavailable",
                "nethogs is not installed. Install it to enable per-process network monitoring.",
                EventSeverity.Warning);
            return;
        }

        // Check permissions
        bool hasPermissions = false;
        try
        {
            hasPermissions = await _monitor.HasPermissionsAsync();
        }
        catch
        {
            hasPermissions = false;
        }

        if (!hasPermissions)
        {
            MonitoringStatus = "Permission denied";
            LastError = "nethogs lacks required capabilities (CAP_NET_RAW)";
            _healthRegistry?.ReportHealth("ProcessMonitor", SubsystemState.Degraded,
                "nethogs permissions insufficient. Run: sudo setcap cap_net_raw,cap_net_admin=eip $(which nethogs)");
            PublishProcessMonitorEvent(DataSenseEventType.ProcessMonitorPermissionDenied,
                "Process Monitor — Permission Denied",
                "nethogs requires CAP_NET_RAW capability. Grant it via: sudo setcap cap_net_raw,cap_net_admin=eip $(which nethogs)",
                EventSeverity.Warning);
            return;
        }

        MonitoringStatus = "Running";
        _healthRegistry?.ReportHealth("ProcessMonitor", SubsystemState.Healthy, "Monitoring active");
        DateTime lastFlush = DateTime.UtcNow;
        RestartAttempts = 0;

        while (!ct.IsCancellationRequested && RestartAttempts < 5)
        {
            try
            {
                await foreach (var batch in _monitor.StartMonitoringAsync(ct))
                {
                    if (ct.IsCancellationRequested) break;

                    // Respect pause state
                    if (_isPaused)
                    {
                        continue;
                    }

                    RestartAttempts = 0; // Reset on successful batch
                    var currentTimestamp = DateTime.UtcNow;
                    var currentBatch = batch.ToList();

                    // Fire event for live UI
                    LiveTrafficUpdated?.Invoke(currentBatch);
                    LastSuccessfulSample = currentTimestamp;

                    // Integrate rates into bytes per sample interval
                    IntegrateProcessUsage(currentBatch, currentTimestamp);

                    // Flush to SQLite periodically
                    if ((currentTimestamp - lastFlush).TotalSeconds >= FlushIntervalSeconds)
                    {
                        await FlushToDatabaseAsync();
                        lastFlush = currentTimestamp;
                    }
                }

                // nethogs exited normally — attempt restart
                if (!ct.IsCancellationRequested)
                {
                    RestartAttempts++;
                    MonitoringStatus = $"Restarting ({RestartAttempts}/5)";
                    _healthRegistry?.ReportHealth("ProcessMonitor", SubsystemState.Degraded, $"nethogs exited, restart attempt {RestartAttempts}/5");

                    if (RestartAttempts == 1)
                    {
                        PublishProcessMonitorEvent(DataSenseEventType.ProcessMonitorBackendRestarted,
                            "Process Monitor Backend Restarted",
                            "nethogs process exited unexpectedly and is being restarted.",
                            EventSeverity.Warning);
                    }

                    await Task.Delay(TimeSpan.FromSeconds(RestartAttempts * 2), ct);
                }
            }
            catch (OperationCanceledException)
            {
                break; // Normal shutdown
            }
            catch (Exception ex)
            {
                RestartAttempts++;
                LastError = ex.Message;
                MonitoringStatus = $"Error (restart {RestartAttempts}/5)";
                _healthRegistry?.ReportHealth("ProcessMonitor", SubsystemState.Error, $"Error: {ex.Message}", ex);

                if (!ct.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(RestartAttempts * 2), ct);
                }
            }
        }

        // Final flush on exit
        try { await FlushToDatabaseAsync(); } catch { }

        if (RestartAttempts >= 5)
        {
            MonitoringStatus = "Failed — max restarts exceeded";
            _healthRegistry?.ReportHealth("ProcessMonitor", SubsystemState.Error, "Max restart attempts exceeded");
        }
    }

    private void IntegrateProcessUsage(List<ProcessNetworkUsage> currentBatch, DateTime currentTimestamp)
    {
        var seenKeys = new HashSet<string>();

        foreach (var usage in currentBatch)
        {
            // Validate sample
            if (usage.Pid < 0 || usage.DownloadRateBytesPerSec < 0 || usage.UploadRateBytesPerSec < 0 ||
                usage.DownloadBytes < 0 || usage.UploadBytes < 0 ||
                string.IsNullOrEmpty(usage.ProcessIdentifier) || usage.Timestamp == default || string.IsNullOrEmpty(usage.DataSource))
            {
                System.Diagnostics.Debug.WriteLine($"Rejected invalid process telemetry sample: PID={usage.Pid}, DL={usage.DownloadRateBytesPerSec}, UL={usage.UploadRateBytesPerSec}, Name={usage.ProcessIdentifier}, TS={usage.Timestamp}, Source={usage.DataSource}");
                continue;
            }

            // Use composite identity key to handle PID reuse
            string key = !string.IsNullOrEmpty(usage.ProcessIdentityKey)
                ? usage.ProcessIdentityKey
                : $"{usage.ProcessIdentifier}_{usage.Pid}_0";

            seenKeys.Add(key);

            if (!_activeProcesses.TryGetValue(key, out var state))
            {
                state = new ProcessState
                {
                    ProcessName = usage.ProcessIdentifier,
                    Pid = usage.Pid,
                    StartTimeTicks = GetStartTimeTicksFromKey(key),
                    StartTime = GetStartTimeFromTicks(GetStartTimeTicksFromKey(key)),
                    ExecutablePath = usage.ExecutablePath,
                    UserName = usage.User,
                    LastUpdate = currentTimestamp,
                    FirstSeen = currentTimestamp,
                    LastSeen = currentTimestamp,
                    Status = "New"
                };

                // Detect PID reuse
                foreach (var oldKvp in _activeProcesses)
                {
                    if (oldKvp.Value.Pid == usage.Pid && oldKvp.Key != key && oldKvp.Value.Status != "Recycled" && oldKvp.Value.Status != "Exited")
                    {
                        oldKvp.Value.Status = "Recycled";
                        oldKvp.Value.LastSeen = currentTimestamp;
                        oldKvp.Value.LastUpdate = currentTimestamp;
                        System.Diagnostics.Debug.WriteLine($"PID reuse detected: PID {usage.Pid} reassigned from {oldKvp.Value.ProcessName} to {usage.ProcessIdentifier}");
                    }
                }

                _activeProcesses[key] = state;
            }
            else
            {
                if (state.Status == "New")
                {
                    state.Status = "Existing";
                }
                else if (state.Status == "Exited" || state.Status == "Recycled")
                {
                    state.Status = "Existing";
                }
                state.LastSeen = currentTimestamp;
            }

            double elapsedSeconds = (currentTimestamp - state.LastUpdate).TotalSeconds;

            // Protect against zero/negative elapsed intervals and duplicate samples
            if (elapsedSeconds > 0 && currentTimestamp != state.LastUpdate)
            {
                long dlDelta = 0;
                long ulDelta = 0;

                if (usage.DownloadBytes > 0 || usage.UploadBytes > 0)
                {
                    if (!state.HasLastCumulative)
                    {
                        state.LastCumulativeDownload = usage.DownloadBytes;
                        state.LastCumulativeUpload = usage.UploadBytes;
                        state.HasLastCumulative = true;
                    }
                    else
                    {
                        if (usage.DownloadBytes < state.LastCumulativeDownload || usage.UploadBytes < state.LastCumulativeUpload)
                        {
                            // Counter reset
                            System.Diagnostics.Debug.WriteLine($"Counter reset detected for process {usage.ProcessIdentifier}: DL {state.LastCumulativeDownload}->{usage.DownloadBytes}, UL {state.LastCumulativeUpload}->{usage.UploadBytes}");
                            state.LastCumulativeDownload = usage.DownloadBytes;
                            state.LastCumulativeUpload = usage.UploadBytes;
                        }
                        else
                        {
                            dlDelta = usage.DownloadBytes - state.LastCumulativeDownload;
                            ulDelta = usage.UploadBytes - state.LastCumulativeUpload;
                            state.LastCumulativeDownload = usage.DownloadBytes;
                            state.LastCumulativeUpload = usage.UploadBytes;
                        }
                    }
                }
                else
                {
                    // Fallback to instantaneous rates
                    dlDelta = (long)(usage.DownloadRateBytesPerSec * elapsedSeconds);
                    ulDelta = (long)(usage.UploadRateBytesPerSec * elapsedSeconds);
                }

                // Protect against negative deltas
                dlDelta = Math.Max(dlDelta, 0);
                ulDelta = Math.Max(ulDelta, 0);

                // Prevent giant jumps if the sample was delayed
                if (elapsedSeconds < MaxElapsedSecondsPerSample)
                {
                    state.UnflushedDownloaded += dlDelta;
                    state.UnflushedUploaded += ulDelta;
                }
            }

            state.LastUpdate = currentTimestamp;

            // Update metadata
            if (!string.IsNullOrEmpty(usage.ExecutablePath))
                state.ExecutablePath = usage.ExecutablePath;
            if (!string.IsNullOrEmpty(usage.User) && usage.User != "unknown")
                state.UserName = usage.User;
        }

        // Detect processes that exited (not in the current batch)
        foreach (var kvp in _activeProcesses)
        {
            if (!seenKeys.Contains(kvp.Key) && kvp.Value.Status != "Exited" && kvp.Value.Status != "Recycled")
            {
                bool isRunning = false;
                if (kvp.Value.Pid > 0)
                {
                    if (_processResolver != null)
                    {
                        var resolved = _processResolver.ResolveProcessIdentity(kvp.Value.Pid);
                        if (resolved != null && resolved.StartTimeTicks == kvp.Value.StartTimeTicks)
                        {
                            isRunning = true;
                        }
                    }
                }

                if (!isRunning)
                {
                    kvp.Value.Status = "Exited";
                    kvp.Value.LastSeen = currentTimestamp;
                    System.Diagnostics.Debug.WriteLine($"Process exit detected: {kvp.Value.ProcessName} (PID={kvp.Value.Pid})");
                }
            }
        }

        // Cleanup stale processes
        var staleKeys = _activeProcesses
            .Where(kvp => (currentTimestamp - kvp.Value.LastSeen).TotalSeconds > StaleProcessTimeoutSeconds)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var staleKey in staleKeys)
        {
            _activeProcesses.TryRemove(staleKey, out _);
        }
    }

    public IEnumerable<ProcessLifecycleInfo> GetTrackedProcesses()
    {
        return _activeProcesses.Values.Select(state => new ProcessLifecycleInfo
        {
            ProcessName = state.ProcessName,
            Pid = state.Pid,
            StartTimeTicks = state.StartTimeTicks,
            StartTime = state.StartTime,
            ExecutablePath = state.ExecutablePath,
            UserName = state.UserName,
            FirstSeen = state.FirstSeen,
            LastSeen = state.LastSeen,
            Status = state.Status,
            IdentityKey = $"{state.ProcessName}_{state.Pid}_{state.StartTimeTicks}"
        }).ToList();
    }

    private static long GetStartTimeTicksFromKey(string key)
    {
        var parts = key.Split('_');
        if (parts.Length >= 3 && long.TryParse(parts[2], out long ticks))
        {
            return ticks;
        }
        return 0;
    }

    private static DateTime GetStartTimeFromTicks(long ticks)
    {
        if (ticks <= 0) return DateTime.MinValue;
        try
        {
            return new DateTime(ticks, DateTimeKind.Utc);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private async Task FlushToDatabaseAsync()
    {
        var now = DateTime.UtcNow;
        var recordsToSave = new List<ProcessUsageRecord>();

        foreach (var kvp in _activeProcesses)
        {
            var state = kvp.Value;

            if (state.UnflushedDownloaded > 0 || state.UnflushedUploaded > 0)
            {
                recordsToSave.Add(new ProcessUsageRecord
                {
                    ProcessName = state.ProcessName,
                    Timestamp = now,
                    BytesDownloaded = state.UnflushedDownloaded,
                    BytesUploaded = state.UnflushedUploaded,
                    ExecutablePath = state.ExecutablePath,
                    UserName = state.UserName,
                    DataSource = "Nethogs",
                    Pid = state.Pid,
                    StartTimeTicks = state.StartTimeTicks
                });

                state.UnflushedDownloaded = 0;
                state.UnflushedUploaded = 0;
            }
        }

        if (recordsToSave.Count > 0)
        {
            try
            {
                await _repository.SaveProcessUsageBatchAsync(recordsToSave);
                if (_analyticsService != null)
                {
                    await _analyticsService.InvalidateCacheAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Process usage flush failed: {ex.Message}");
            }
        }
    }

    private void PublishProcessMonitorEvent(DataSenseEventType type, string title, string description, EventSeverity severity)
    {
        _eventService?.PublishEvent(new DataSenseEvent
        {
            EventType = type,
            Title = title,
            Description = description,
            Severity = severity,
            Source = "ProcessMonitor",
            Fingerprint = $"ProcessMonitor_{type}"
        });
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Stop();
            _disposed = true;
        }
    }

    private class ProcessState
    {
        public string ProcessName { get; set; } = string.Empty;
        public int Pid { get; set; }
        public long StartTimeTicks { get; set; }
        public DateTime StartTime { get; set; }
        public string ExecutablePath { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public DateTime LastUpdate { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public string Status { get; set; } = "New";
        public long UnflushedDownloaded { get; set; }
        public long UnflushedUploaded { get; set; }

        public long LastCumulativeDownload { get; set; }
        public long LastCumulativeUpload { get; set; }
        public bool HasLastCumulative { get; set; }
    }
}

public class ProcessLifecycleInfo
{
    public string ProcessName { get; set; } = string.Empty;
    public int Pid { get; set; }
    public long StartTimeTicks { get; set; }
    public DateTime StartTime { get; set; }
    public string ExecutablePath { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public string Status { get; set; } = "New"; // "New", "Existing", "Exited", "Recycled"
    public string IdentityKey { get; set; } = string.Empty;
}
