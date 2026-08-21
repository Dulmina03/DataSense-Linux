using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

public class NetworkMonitorWorker : INetworkMonitorWorker, IDisposable
{
    private readonly INetworkMonitorService _networkMonitorService;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private readonly object _stateLock = new();
    private bool _disposed;

    public string? ActiveInterface { get; private set; }
    public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;
    public double DownloadSpeed { get; private set; }
    public double UploadSpeed { get; private set; }
    public long TotalBytesDownloaded { get; private set; }
    public long TotalBytesUploaded { get; private set; }
    public long TotalDataUsage => TotalBytesDownloaded + TotalBytesUploaded;
    public NetworkUsage? LatestUsage { get; private set; }

    public event Action<NetworkUsage>? NetworkUsageUpdated;

    public NetworkMonitorWorker(INetworkMonitorService networkMonitorService)
    {
        _networkMonitorService = networkMonitorService ?? throw new ArgumentNullException(nameof(networkMonitorService));
    }

    public void Start()
    {
        lock (_stateLock)
        {
            if (_cts != null) return; // Already running

            _cts = new CancellationTokenSource();
            _runTask = Task.Run(() => RunAsync(_cts.Token));
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        Task? runTask;

        lock (_stateLock)
        {
            cts = _cts;
            runTask = _runTask;

            _cts = null;
            _runTask = null;
        }

        if (cts != null)
        {
            cts.Cancel();
            try
            {
                runTask?.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error stopping background worker: {ex}");
            }
            cts.Dispose();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                string? activeIface = await DetectActiveInterfaceAsync();
                if (!string.IsNullOrEmpty(activeIface))
                {
                    var usage = await _networkMonitorService.GetUsageAsync(activeIface);
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
                    else
                    {
                        ActiveInterface = activeIface;
                        DownloadSpeed = 0;
                        UploadSpeed = 0;
                        TotalBytesDownloaded = 0;
                        TotalBytesUploaded = 0;

                        var fallbackUsage = new NetworkUsage
                        {
                            InterfaceName = activeIface,
                            Timestamp = DateTime.UtcNow
                        };
                        LatestUsage = fallbackUsage;
                        NetworkUsageUpdated?.Invoke(fallbackUsage);
                    }
                }
                else
                {
                    ActiveInterface = null;
                    DownloadSpeed = 0;
                    UploadSpeed = 0;
                    TotalBytesDownloaded = 0;
                    TotalBytesUploaded = 0;

                    var fallbackUsage = new NetworkUsage
                    {
                        InterfaceName = "None",
                        Timestamp = DateTime.UtcNow
                    };
                    LatestUsage = fallbackUsage;
                    NetworkUsageUpdated?.Invoke(fallbackUsage);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in background monitor worker: {ex}");
            }

            try
            {
                await timer.WaitForNextTickAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<string?> DetectActiveInterfaceAsync()
    {
        // 1. Try to find active interface via default gateway in /proc/net/route
        string? routeInterface = GetDefaultRouteInterface();
        if (!string.IsNullOrEmpty(routeInterface))
        {
            return routeInterface;
        }

        // 2. Fallback: Query INetworkMonitorService for available interfaces
        var available = await _networkMonitorService.GetAvailableInterfacesAsync();
        var availableList = available.ToList();
        if (availableList.Any())
        {
            return availableList.First();
        }

        return null;
    }

    private string? GetDefaultRouteInterface()
    {
        const string routePath = "/proc/net/route";
        if (!File.Exists(routePath))
            return null;

        try
        {
            var lines = File.ReadAllLines(routePath);
            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length < 8) continue;

                var iface = parts[0];
                var destination = parts[1];
                var mask = parts[7];

                // Default route destination is 00000000 and mask is 00000000
                if (destination == "00000000" && mask == "00000000")
                {
                    return iface;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error reading default route: {ex}");
        }

        return null;
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
