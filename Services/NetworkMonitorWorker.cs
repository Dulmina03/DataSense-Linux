using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

/// <summary>
/// Bridges the canonical <see cref="IUsageSnapshotService"/> to existing <see cref="INetworkMonitorWorker"/> consumers.
/// Guarantees that there is only ONE measurement loop active in the application.
/// </summary>
public class NetworkMonitorWorker : INetworkMonitorWorker, IDisposable
{
    private readonly IUsageSnapshotService? _snapshotService;
    private readonly INetworkMonitorService? _legacyMonitorService;
    private readonly object _stateLock = new();
    private bool _isStarted;
    private bool _disposed;

    public string? ActiveInterface { get; private set; } = "Disconnected";
    public bool IsRunning => _snapshotService?.IsRunning ?? _isStarted;
    public double DownloadSpeed { get; private set; }
    public double UploadSpeed { get; private set; }
    public long TotalBytesDownloaded { get; private set; }
    public long TotalBytesUploaded { get; private set; }
    public long TotalDataUsage => TotalBytesDownloaded + TotalBytesUploaded;
    public NetworkUsage? LatestUsage { get; private set; }

    public event Action<NetworkUsage>? NetworkUsageUpdated;

    /// <summary>
    /// Canonical constructor accepting the single <see cref="IUsageSnapshotService"/>.
    /// </summary>
    public NetworkMonitorWorker(IUsageSnapshotService snapshotService)
    {
        _snapshotService = snapshotService ?? throw new ArgumentNullException(nameof(snapshotService));
        _snapshotService.SnapshotsGenerated += OnCanonicalSnapshotsGenerated;
    }

    /// <summary>
    /// Legacy constructor preserved for backwards compatibility with tests.
    /// </summary>
    public NetworkMonitorWorker(INetworkMonitorService networkMonitorService)
    {
        _legacyMonitorService = networkMonitorService ?? throw new ArgumentNullException(nameof(networkMonitorService));
    }

    public void Start()
    {
        lock (_stateLock)
        {
            if (_isStarted) return;
            _isStarted = true;

            if (_snapshotService != null)
            {
                _snapshotService.Start();
            }
            else if (_legacyMonitorService != null)
            {
                StartLegacyPolling();
            }
        }
    }

    public void Stop()
    {
        lock (_stateLock)
        {
            if (!_isStarted) return;
            _isStarted = false;

            if (_snapshotService != null)
            {
                _snapshotService.Stop();
            }
            else
            {
                StopLegacyPolling();
            }
        }
    }

    private void OnCanonicalSnapshotsGenerated(IReadOnlyList<NetworkUsageSnapshot> snapshots)
    {
        var host = _snapshotService?.GetLatestHostSnapshot();
        string activeIface = _snapshotService?.PrimaryActiveInterface ?? "Disconnected";

        if (host != null && snapshots.Count > 0)
        {
            ActiveInterface = activeIface;
            DownloadSpeed = host.DownloadSpeedBps;
            UploadSpeed = host.UploadSpeedBps;
            TotalBytesDownloaded = host.RawBytesReceived;
            TotalBytesUploaded = host.RawBytesSent;

            var usage = new NetworkUsage
            {
                InterfaceName = activeIface,
                BytesReceived = host.RawBytesReceived,
                BytesSent = host.RawBytesSent,
                DownloadDelta = host.DeltaBytesReceived,
                UploadDelta = host.DeltaBytesSent,
                DownloadSpeed = host.DownloadSpeedBps,
                UploadSpeed = host.UploadSpeedBps,
                Timestamp = host.TimestampUtc
            };

            LatestUsage = usage;
            NetworkUsageUpdated?.Invoke(usage);
        }
        else
        {
            ActiveInterface = "Disconnected";
            DownloadSpeed = 0;
            UploadSpeed = 0;

            var fallbackUsage = new NetworkUsage
            {
                InterfaceName = "Disconnected",
                Timestamp = DateTime.UtcNow
            };

            LatestUsage = fallbackUsage;
            NetworkUsageUpdated?.Invoke(fallbackUsage);
        }
    }

    #region Legacy Polling Fallback (For isolated legacy tests without snapshot service)

    private CancellationTokenSource? _legacyCts;
    private Task? _legacyTask;
    private string? _previousInterface;

    private void StartLegacyPolling()
    {
        _legacyCts = new CancellationTokenSource();
        _legacyTask = Task.Run(() => RunLegacyAsync(_legacyCts.Token));
    }

    private void StopLegacyPolling()
    {
        _legacyCts?.Cancel();
        try { _legacyTask?.GetAwaiter().GetResult(); } catch { }
        _legacyCts?.Dispose();
        _legacyCts = null;
        _legacyTask = null;
    }

    private async Task RunLegacyAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var interfaces = await _legacyMonitorService!.GetAvailableInterfacesAsync();
                var activeIface = interfaces.FirstOrDefault();
                if (!string.IsNullOrEmpty(activeIface) && activeIface != "None")
                {
                    if (_previousInterface != null && _previousInterface != activeIface)
                    {
                        _legacyMonitorService.ResetMeasurement(activeIface);
                    }
                    _previousInterface = activeIface;

                    var usage = await _legacyMonitorService.GetUsageAsync(activeIface);
                    if (usage != null)
                    {
                        ActiveInterface = usage.InterfaceName;
                        DownloadSpeed = usage.DownloadSpeed;
                        UploadSpeed = usage.UploadSpeed;
                        TotalBytesDownloaded = usage.BytesReceived;
                        TotalBytesUploaded = usage.BytesSent;
                        LatestUsage = usage;
                        NetworkUsageUpdated?.Invoke(usage);
                    }
                }
            }
            catch { }

            try { await timer.WaitForNextTickAsync(ct); } catch { break; }
        }
    }

    #endregion

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_snapshotService != null)
            {
                _snapshotService.SnapshotsGenerated -= OnCanonicalSnapshotsGenerated;
            }
            Stop();
            _disposed = true;
        }
    }
}

