using System;
using System.Threading;
using System.Threading.Tasks;
using DataSense.Database;
using DataSense.Models;

namespace DataSense.Services;

public class NetworkSessionManager : IDisposable
{
    private readonly INetworkMonitorWorker _monitorWorker;
    private readonly INetworkConnectionService _connectionService;
    private readonly INetworkUsageRepository _repository;
    
    private NetworkSession? _currentSession;
    public NetworkSession? CurrentSession => _currentSession;
    private string? _currentInterface;
    private long _sessionStartDownloaded;
    private long _sessionStartUploaded;
    
    private readonly SemaphoreSlim _lock = new(1, 1);

    public NetworkSessionManager(
        INetworkMonitorWorker monitorWorker,
        INetworkConnectionService connectionService,
        INetworkUsageRepository repository)
    {
        _monitorWorker = monitorWorker;
        _connectionService = connectionService;
        _repository = repository;
    }

    public void Start()
    {
        _monitorWorker.NetworkUsageUpdated += OnNetworkUsageUpdated;
    }

    public void Stop()
    {
        _monitorWorker.NetworkUsageUpdated -= OnNetworkUsageUpdated;
        _ = FinalizeCurrentSessionAsync();
    }

    private async void OnNetworkUsageUpdated(NetworkUsage usage)
    {
        await _lock.WaitAsync();
        try
        {
            var iface = usage.InterfaceName;
            bool isDisconnected = string.IsNullOrEmpty(iface) || iface == "None" || iface == "Disconnected";

            // If interface changed
            if (_currentInterface != iface)
            {
                // Finalize previous session
                await FinalizeCurrentSessionAsync();
                
                _currentInterface = iface;

                if (!isDisconnected)
                {
                    // Start new session
                    var details = await _connectionService.GetConnectionDetailsAsync(iface!);
                    
                    string networkName = "Unknown Network";
                    if (details.ConnectionType.Equals("wifi", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(details.WifiSsid))
                    {
                        networkName = details.WifiSsid;
                    }
                    else if (details.ConnectionType.Equals("ethernet", StringComparison.OrdinalIgnoreCase))
                    {
                        networkName = "Ethernet";
                    }
                    else if (!string.IsNullOrEmpty(details.ConnectionName) && details.ConnectionName != "—")
                    {
                        networkName = details.ConnectionName;
                    }

                    _currentSession = new NetworkSession
                    {
                        InterfaceName = iface!,
                        ConnectionType = details.ConnectionType,
                        NetworkName = networkName,
                        StartTime = DateTime.UtcNow,
                        BytesDownloaded = 0,
                        BytesUploaded = 0
                    };
                    
                    _sessionStartDownloaded = usage.BytesReceived;
                    _sessionStartUploaded = usage.BytesSent;

                    await _repository.SaveSessionAsync(_currentSession);
                }
            }
            else if (_currentSession != null)
            {
                // Update ongoing session bytes
                long currentDownloadedDelta = usage.BytesReceived - _sessionStartDownloaded;
                long currentUploadedDelta = usage.BytesSent - _sessionStartUploaded;
                
                // Handle counter resets (if delta is negative, just add to it, assuming a reset. Wait, if counter resets, usage.BytesReceived is smaller than start.
                // For simplicity, if it's smaller, we just reset the start tracker)
                if (currentDownloadedDelta < 0) 
                {
                    _sessionStartDownloaded = usage.BytesReceived;
                    currentDownloadedDelta = 0;
                }
                if (currentUploadedDelta < 0)
                {
                    _sessionStartUploaded = usage.BytesSent;
                    currentUploadedDelta = 0;
                }


                // Only update DB periodically if significant change or every minute?
                // Let's just update the in-memory object and we'll save it to DB every 10 seconds or on end.
                // We can use a modulo of ticks or time. 
                // Since this fires every second, we'll just update it in memory, and we update DB if we hit a 10s threshold.
                var now = DateTime.UtcNow;
                if ((now - (_lastDbUpdate ?? DateTime.MinValue)).TotalSeconds > 10)
                {
                    _currentSession.BytesDownloaded += currentDownloadedDelta;
                    _currentSession.BytesUploaded += currentUploadedDelta;
                    _sessionStartDownloaded = usage.BytesReceived;
                    _sessionStartUploaded = usage.BytesSent;
                    
                    await _repository.UpdateSessionAsync(_currentSession);
                    _lastDbUpdate = now;
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }
    
    private DateTime? _lastDbUpdate;

    private async Task FinalizeCurrentSessionAsync()
    {
        if (_currentSession != null)
        {
            _currentSession.EndTime = DateTime.UtcNow;
            
            if (_monitorWorker.LatestUsage != null)
            {
                long currentDownloadedDelta = _monitorWorker.LatestUsage.BytesReceived - _sessionStartDownloaded;
                long currentUploadedDelta = _monitorWorker.LatestUsage.BytesSent - _sessionStartUploaded;
                
                if (currentDownloadedDelta > 0) _currentSession.BytesDownloaded += currentDownloadedDelta;
                if (currentUploadedDelta > 0) _currentSession.BytesUploaded += currentUploadedDelta;
            }

            await _repository.UpdateSessionAsync(_currentSession);
            _currentSession = null;
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}
