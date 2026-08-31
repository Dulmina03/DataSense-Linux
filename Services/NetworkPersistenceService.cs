using System;
using System.Threading.Tasks;
using DataSense.Database;
using DataSense.Models;

namespace DataSense.Services;

public class NetworkPersistenceService : INetworkPersistenceService, IDisposable
{
    private readonly INetworkMonitorWorker _networkMonitorWorker;
    private readonly INetworkUsageRepository _repository;
    private readonly TimeSpan _persistInterval = TimeSpan.FromSeconds(5);
    private DateTime _lastPersistedTimestamp = DateTime.MinValue;
    private bool _isStarted;
    private bool _disposed;

    public NetworkPersistenceService(INetworkMonitorWorker networkMonitorWorker, INetworkUsageRepository repository)
    {
        _networkMonitorWorker = networkMonitorWorker ?? throw new ArgumentNullException(nameof(networkMonitorWorker));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public void Start()
    {
        if (_isStarted) return;

        _networkMonitorWorker.NetworkUsageUpdated += OnNetworkUsageUpdated;
        _isStarted = true;
    }

    public void Stop()
    {
        if (!_isStarted) return;

        _networkMonitorWorker.NetworkUsageUpdated -= OnNetworkUsageUpdated;
        _isStarted = false;
    }

    private readonly object _deltaLock = new();
    private long _accumulatedDownloadDelta;
    private long _accumulatedUploadDelta;

    private void OnNetworkUsageUpdated(NetworkUsage usage)
    {
        if (usage == null || string.IsNullOrEmpty(usage.InterfaceName) || usage.InterfaceName == "None")
            return;

        long saveDlDelta;
        long saveUlDelta;
        bool shouldPersist = false;

        lock (_deltaLock)
        {
            _accumulatedDownloadDelta += usage.DownloadDelta;
            _accumulatedUploadDelta += usage.UploadDelta;

            DateTime now = DateTime.UtcNow;
            if (now - _lastPersistedTimestamp >= _persistInterval)
            {
                _lastPersistedTimestamp = now;
                saveDlDelta = _accumulatedDownloadDelta;
                saveUlDelta = _accumulatedUploadDelta;
                _accumulatedDownloadDelta = 0;
                _accumulatedUploadDelta = 0;
                shouldPersist = true;
            }
            else
            {
                saveDlDelta = 0;
                saveUlDelta = 0;
            }
        }

        if (shouldPersist)
        {
            var usageToSave = new NetworkUsage
            {
                InterfaceName = usage.InterfaceName,
                BytesReceived = usage.BytesReceived,
                BytesSent = usage.BytesSent,
                DownloadDelta = saveDlDelta,
                UploadDelta = saveUlDelta,
                DownloadSpeed = usage.DownloadSpeed,
                UploadSpeed = usage.UploadSpeed,
                Timestamp = usage.Timestamp
            };

            // Fire-and-forget async save on background thread without blocking telemetry loop or UI thread
            Task.Run(async () =>
            {
                try
                {
                    await _repository.SaveUsageAsync(usageToSave);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error persisting network usage: {ex}");
                }
            });
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                Stop();
            }
            _disposed = true;
        }
    }
}
