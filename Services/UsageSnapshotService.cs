using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

/// <summary>
/// Canonical implementation of <see cref="IUsageSnapshotService"/>.
/// Executes the single 1-second host measurement loop, computes reset-safe monotonic deltas,
/// associates network identity, and publishes canonical snapshots.
/// </summary>
public class UsageSnapshotService : IUsageSnapshotService
{
    private readonly INetworkUsageCollector _collector;
    private readonly INetworkIdentityService? _identityService;
    private readonly ISystemHealthRegistry? _healthRegistry;

    private readonly ConcurrentDictionary<string, InterfaceBaselineState> _baselines = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _stateLock = new();

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private bool _disposed;

    private IReadOnlyList<NetworkUsageSnapshot> _latestSnapshots = Array.Empty<NetworkUsageSnapshot>();
    private NetworkUsageSnapshot? _latestHostSnapshot;

    public event Action<IReadOnlyList<NetworkUsageSnapshot>>? SnapshotsGenerated;

    public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;
    public string? PrimaryActiveInterface { get; private set; }

    private class InterfaceBaselineState
    {
        public long LastRawRx { get; set; }
        public long LastRawTx { get; set; }
        public DateTime LastTimestampUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public bool HasBaseline { get; set; }
    }

    public UsageSnapshotService(
        INetworkUsageCollector collector,
        INetworkIdentityService? identityService = null,
        ISystemHealthRegistry? healthRegistry = null)
    {
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _identityService = identityService;
        _healthRegistry = healthRegistry;

        _healthRegistry?.RegisterSubsystem("UsageSnapshotService");
    }

    public void Start()
    {
        lock (_stateLock)
        {
            if (_cts != null) return;

            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => RunMeasurementLoopAsync(_cts.Token));
            _healthRegistry?.ReportHealth("UsageSnapshotService", SubsystemState.Healthy, "Canonical measurement loop active");
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        Task? loopTask;

        lock (_stateLock)
        {
            cts = _cts;
            loopTask = _loopTask;
            _cts = null;
            _loopTask = null;
        }

        if (cts != null)
        {
            cts.Cancel();
            try
            {
                loopTask?.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UsageSnapshotService] Error stopping measurement loop: {ex.Message}");
            }
            cts.Dispose();
            _healthRegistry?.ReportHealth("UsageSnapshotService", SubsystemState.Healthy, "Canonical measurement loop stopped");
        }
    }

    public IReadOnlyList<NetworkUsageSnapshot> GetLatestSnapshots() => _latestSnapshots;
    public NetworkUsageSnapshot? GetLatestHostSnapshot() => _latestHostSnapshot;

    private async Task RunMeasurementLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await MeasureCycleAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UsageSnapshotService] Measurement cycle error: {ex}");
                _healthRegistry?.ReportHealth("UsageSnapshotService", SubsystemState.Degraded, $"Measurement cycle error: {ex.Message}", ex);
            }

            try
            {
                await timer.WaitForNextTickAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task<IReadOnlyList<NetworkUsageSnapshot>> MeasureCycleAsync()
    {
        var rawCountersList = await _collector.CollectAllInterfacesAsync();
        var nowUtc = DateTime.UtcNow;
        var snapshots = new List<NetworkUsageSnapshot>(rawCountersList.Count);

        var seenInterfaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var counter in rawCountersList)
        {
            string iface = counter.InterfaceName;
            seenInterfaces.Add(iface);

            long deltaRx = 0;
            long deltaTx = 0;
            double speedRx = 0;
            double speedTx = 0;
            double elapsedSeconds = 1.0;
            bool isReset = false;
            bool isInitial = false;

            if (!_baselines.TryGetValue(iface, out var baseline))
            {
                // First observation of this interface: establish baseline
                baseline = new InterfaceBaselineState
                {
                    LastRawRx = counter.RawBytesReceived,
                    LastRawTx = counter.RawBytesSent,
                    LastTimestampUtc = nowUtc,
                    LastSeenUtc = nowUtc,
                    HasBaseline = true
                };
                _baselines[iface] = baseline;
                isInitial = true;
                deltaRx = 0;
                deltaTx = 0;
                speedRx = 0;
                speedTx = 0;
            }
            else
            {
                elapsedSeconds = (nowUtc - baseline.LastTimestampUtc).TotalSeconds;
                baseline.LastSeenUtc = nowUtc;

                // Validate realistic sample interval (0 < elapsed <= 15.0s)
                if (elapsedSeconds > 0 && elapsedSeconds <= 15.0 && baseline.HasBaseline)
                {
                    // 1. Calculate Rx delta & handle counter reset
                    if (counter.RawBytesReceived >= baseline.LastRawRx)
                    {
                        deltaRx = counter.RawBytesReceived - baseline.LastRawRx;
                    }
                    else
                    {
                        // Counter reset detected: delta MUST BE 0, NOT current cumulative counter!
                        deltaRx = 0;
                        isReset = true;
                        Debug.WriteLine($"[UsageSnapshotService] Counter reset on {iface}: Rx {baseline.LastRawRx} -> {counter.RawBytesReceived}");
                    }

                    // 2. Calculate Tx delta & handle counter reset
                    if (counter.RawBytesSent >= baseline.LastRawTx)
                    {
                        deltaTx = counter.RawBytesSent - baseline.LastRawTx;
                    }
                    else
                    {
                        // Counter reset detected: delta MUST BE 0
                        deltaTx = 0;
                        isReset = true;
                        Debug.WriteLine($"[UsageSnapshotService] Counter reset on {iface}: Tx {baseline.LastRawTx} -> {counter.RawBytesSent}");
                    }

                    // 3. Compute speed in bytes per second
                    speedRx = deltaRx / elapsedSeconds;
                    speedTx = deltaTx / elapsedSeconds;
                }
                else
                {
                    // Too long gap (e.g. sleep/suspend or timer glitch) -> reset baseline smoothly
                    deltaRx = 0;
                    deltaTx = 0;
                    speedRx = 0;
                    speedTx = 0;
                }

                // Update baseline state
                baseline.LastRawRx = counter.RawBytesReceived;
                baseline.LastRawTx = counter.RawBytesSent;
                baseline.LastTimestampUtc = nowUtc;
            }

            // Protect against invalid values
            deltaRx = Math.Max(0, deltaRx);
            deltaTx = Math.Max(0, deltaTx);
            speedRx = Math.Max(0, double.IsFinite(speedRx) ? speedRx : 0);
            speedTx = Math.Max(0, double.IsFinite(speedTx) ? speedTx : 0);

            // Resolve Network Identity
            string networkKey;
            string displayName;

            if (_identityService != null)
            {
                try
                {
                    var identity = await _identityService.GetCurrentIdentityAsync(iface);
                    networkKey = identity.CanonicalKey;
                    displayName = identity.DisplayName;
                }
                catch
                {
                    networkKey = $"{counter.ConnectionType.ToLowerInvariant()}:{iface}";
                    displayName = iface;
                }
            }
            else
            {
                networkKey = $"{counter.ConnectionType.ToLowerInvariant()}:{iface}";
                displayName = iface;
            }

            var snapshot = new NetworkUsageSnapshot
            {
                TimestampUtc = nowUtc,
                InterfaceName = iface,
                NetworkKey = networkKey,
                NetworkDisplayName = displayName,
                ConnectionType = counter.ConnectionType,
                RawBytesReceived = counter.RawBytesReceived,
                RawBytesSent = counter.RawBytesSent,
                DeltaBytesReceived = deltaRx,
                DeltaBytesSent = deltaTx,
                DownloadSpeedBps = speedRx,
                UploadSpeedBps = speedTx,
                ElapsedSeconds = elapsedSeconds,
                IsCounterReset = isReset,
                IsInitialBaseline = isInitial
            };

            snapshots.Add(snapshot);
        }

        // Clean up baselines for interfaces disconnected/removed > 120s
        var staleKeys = _baselines
            .Where(kvp => (nowUtc - kvp.Value.LastSeenUtc).TotalSeconds > 120)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var staleKey in staleKeys)
        {
            _baselines.TryRemove(staleKey, out _);
        }

        // Determine primary active interface (prefer Wi-Fi/Ethernet with operational traffic)
        var primarySnapshot = snapshots
            .OrderByDescending(s => s.TotalSpeedBps > 0)
            .ThenByDescending(s => s.ConnectionType == "WiFi" || s.ConnectionType == "Ethernet")
            .FirstOrDefault();

        PrimaryActiveInterface = primarySnapshot?.InterfaceName ?? (snapshots.Count > 0 ? snapshots[0].InterfaceName : "Disconnected");

        // Build consolidated host snapshot
        long totalRawRx = snapshots.Sum(s => s.RawBytesReceived);
        long totalRawTx = snapshots.Sum(s => s.RawBytesSent);
        long totalDeltaRx = snapshots.Sum(s => s.DeltaBytesReceived);
        long totalDeltaTx = snapshots.Sum(s => s.DeltaBytesSent);
        double totalSpeedRx = snapshots.Sum(s => s.DownloadSpeedBps);
        double totalSpeedTx = snapshots.Sum(s => s.UploadSpeedBps);

        _latestHostSnapshot = new NetworkUsageSnapshot
        {
            TimestampUtc = nowUtc,
            InterfaceName = primarySnapshot?.InterfaceName ?? "host",
            NetworkKey = primarySnapshot?.NetworkKey ?? "host:combined",
            NetworkDisplayName = primarySnapshot?.NetworkDisplayName ?? "Host Network",
            ConnectionType = primarySnapshot?.ConnectionType ?? "Combined",
            RawBytesReceived = totalRawRx,
            RawBytesSent = totalRawTx,
            DeltaBytesReceived = totalDeltaRx,
            DeltaBytesSent = totalDeltaTx,
            DownloadSpeedBps = totalSpeedRx,
            UploadSpeedBps = totalSpeedTx,
            ElapsedSeconds = 1.0,
            IsCounterReset = snapshots.Any(s => s.IsCounterReset),
            IsInitialBaseline = snapshots.All(s => s.IsInitialBaseline)
        };

        _latestSnapshots = snapshots.AsReadOnly();

        // Emit canonical event stream
        SnapshotsGenerated?.Invoke(_latestSnapshots);

        return _latestSnapshots;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Stop();
            _disposed = true;
        }
    }
}
