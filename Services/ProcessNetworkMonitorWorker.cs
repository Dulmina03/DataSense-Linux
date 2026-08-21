using System;
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
    private CancellationTokenSource? _cts;
    private Task? _workerTask;

    public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;

    // We store the last seen usage and the timestamp to calculate elapsed time
    private readonly Dictionary<string, ProcessState> _activeProcesses = new();
    
    // UI can subscribe to this for the live Process Network Traffic table
    public event Action<IEnumerable<ProcessNetworkUsage>>? LiveTrafficUpdated;

    public ProcessNetworkMonitorWorker(IProcessNetworkMonitor monitor, INetworkUsageRepository repository)
    {
        _monitor = monitor;
        _repository = repository;
    }

    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        _workerTask = Task.Run(() => RunAsync(_cts.Token), _cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _workerTask?.Wait();
        }
        catch { /* ignored */ }
        _cts?.Dispose();
        _cts = null;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        // Gracefully fail if nethogs is not available or no permissions
        if (!await _monitor.IsAvailableAsync() || !await _monitor.HasPermissionsAsync())
        {
            return;
        }

        DateTime lastFlush = DateTime.UtcNow;

        try
        {
            await foreach (var batch in _monitor.StartMonitoringAsync(ct))
            {
                var currentTimestamp = DateTime.UtcNow;
                var currentBatch = batch.ToList();

                // Fire event for live UI
                LiveTrafficUpdated?.Invoke(currentBatch);

                // Integrate rates into bytes
                foreach (var usage in currentBatch)
                {
                    string key = usage.ProcessIdentifier; // Group by application identity

                    if (!_activeProcesses.TryGetValue(key, out var state))
                    {
                        state = new ProcessState
                        {
                            LastUpdate = currentTimestamp
                        };
                        _activeProcesses[key] = state;
                    }

                    double elapsedSeconds = (currentTimestamp - state.LastUpdate).TotalSeconds;
                    
                    // Prevent giant jumps if the process was paused/slept
                    if (elapsedSeconds > 0 && elapsedSeconds < 10)
                    {
                        state.UnflushedDownloaded += (long)(usage.DownloadRateBytesPerSec * elapsedSeconds);
                        state.UnflushedUploaded += (long)(usage.UploadRateBytesPerSec * elapsedSeconds);
                    }

                    state.LastUpdate = currentTimestamp;
                }

                // Flush to SQLite every 5 seconds to avoid DB spam
                if ((currentTimestamp - lastFlush).TotalSeconds >= 5)
                {
                    await FlushToDatabaseAsync();
                    lastFlush = currentTimestamp;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal exit
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Process monitor error: {ex.Message}");
        }
        finally
        {
            // Flush any remaining data on exit
            await FlushToDatabaseAsync();
        }
    }

    private async Task FlushToDatabaseAsync()
    {
        var now = DateTime.UtcNow;
        var toFlush = _activeProcesses.ToList();
        var recordsToSave = new List<ProcessUsageRecord>();

        foreach (var kvp in toFlush)
        {
            var key = kvp.Key;
            var state = kvp.Value;

            if (state.UnflushedDownloaded > 0 || state.UnflushedUploaded > 0)
            {
                recordsToSave.Add(new ProcessUsageRecord
                {
                    ProcessName = key,
                    Timestamp = now,
                    BytesDownloaded = state.UnflushedDownloaded,
                    BytesUploaded = state.UnflushedUploaded
                });

                state.UnflushedDownloaded = 0;
                state.UnflushedUploaded = 0;
            }

            // Cleanup stale processes that haven't been seen in 60 seconds
            if ((now - state.LastUpdate).TotalSeconds > 60)
            {
                _activeProcesses.Remove(key);
            }
        }

        if (recordsToSave.Count > 0)
        {
            await _repository.SaveProcessUsageBatchAsync(recordsToSave);
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private class ProcessState
    {
        public DateTime LastUpdate { get; set; }
        public long UnflushedDownloaded { get; set; }
        public long UnflushedUploaded { get; set; }
    }
}
