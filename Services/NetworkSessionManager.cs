using System;
using System.Threading;
using System.Threading.Tasks;
using DataSense.Database;
using DataSense.Helpers;
using DataSense.Models;

namespace DataSense.Services;

public class NetworkSessionManager : IDisposable
{
    private readonly INetworkMonitorWorker _monitorWorker;
    private readonly INetworkConnectionService _connectionService;
    private readonly INetworkUsageRepository _repository;
    private readonly INetworkIdentityService _identityService;
    
    private NetworkSession? _currentSession;
    public NetworkSession? CurrentSession => _currentSession;
    private string? _currentInterface;
    private string? _currentNetworkName;
    private long _sessionStartDownloaded;
    private long _sessionStartUploaded;
    private DateTime? _lastDbUpdate;
    private DateTime _lastNetworkCheck = DateTime.MinValue;

    private readonly SemaphoreSlim _lock = new(1, 1);

    public NetworkSessionManager(
        INetworkMonitorWorker monitorWorker,
        INetworkConnectionService connectionService,
        INetworkUsageRepository repository,
        INetworkIdentityService? identityService = null)
    {
        _monitorWorker = monitorWorker;
        _connectionService = connectionService;
        _repository = repository;
        _identityService = identityService ?? new NetworkIdentityService(connectionService);
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

    public static string ResolveNetworkName(NetworkConnectionDetails details, string interfaceName)
    {
        // Priority 1 & 2: Active Wi-Fi SSID
        if (NetworkIdentityValidator.IsValidNetworkName(details.WifiSsid))
        {
            return details.WifiSsid.Trim();
        }

        // Priority 3: NetworkManager Connection Profile Name
        if (NetworkIdentityValidator.IsValidNetworkName(details.ConnectionName) &&
            !details.ConnectionName.StartsWith("Wired connection", StringComparison.OrdinalIgnoreCase) &&
            !details.ConnectionName.StartsWith("Wired Connection", StringComparison.OrdinalIgnoreCase))
        {
            return details.ConnectionName.Trim();
        }

        // Ethernet detection
        if (details.ConnectionType.Equals("ethernet", StringComparison.OrdinalIgnoreCase))
        {
            return "Ethernet";
        }

        // Priority 4: Interface fallback
        if (!string.IsNullOrWhiteSpace(interfaceName) && interfaceName != "None" && interfaceName != "Disconnected")
        {
            return $"Interface: {interfaceName.Trim()}";
        }

        return "Unknown Network";
    }

    private async void OnNetworkUsageUpdated(NetworkUsage usage)
    {
        await _lock.WaitAsync();
        try
        {
            var iface = usage.InterfaceName;
            bool isDisconnected = string.IsNullOrEmpty(iface) || iface == "None" || iface == "Disconnected";
            var now = DateTime.UtcNow;

            // Check for network switch (e.g., interface change or SSID change every 5 seconds)
            bool shouldCheckNetworkIdentity = _currentInterface != iface || (now - _lastNetworkCheck).TotalSeconds > 5;
            string? detectedNetworkName = _currentNetworkName;
            NetworkIdentity? identity = null;

            if (shouldCheckNetworkIdentity && !isDisconnected)
            {
                try
                {
                    identity = await _identityService.GetCurrentIdentityAsync(iface!);
                    var resolved = identity.DisplayName;

                    // If resolved identity is valid, update detected name
                    if (_identityService.IsValidNetworkName(resolved) || resolved == "Ethernet")
                    {
                        detectedNetworkName = resolved;
                    }
                    // If resolution temporarily failed or returned interface fallback, but we already had a valid network on this interface, preserve it
                    else if (_currentInterface == iface && _identityService.IsValidNetworkName(_currentNetworkName))
                    {
                        detectedNetworkName = _currentNetworkName;
                    }
                    else
                    {
                        detectedNetworkName = resolved;
                    }

                    _lastNetworkCheck = now;
                }
                catch { }
            }

            bool networkChanged = _currentInterface != iface ||
                                 (!string.IsNullOrEmpty(_currentNetworkName) &&
                                  !string.IsNullOrEmpty(detectedNetworkName) &&
                                  !string.Equals(_currentNetworkName, detectedNetworkName, StringComparison.OrdinalIgnoreCase));

            // If interface or network SSID changed
            if (networkChanged || (_currentSession == null && !isDisconnected))
            {
                // Finalize previous session
                await FinalizeCurrentSessionAsync();

                _currentInterface = iface;
                _currentNetworkName = detectedNetworkName;

                if (!isDisconnected)
                {
                    if (string.IsNullOrEmpty(detectedNetworkName))
                    {
                        try
                        {
                            identity = await _identityService.GetCurrentIdentityAsync(iface!);
                            detectedNetworkName = identity.DisplayName;
                            _currentNetworkName = detectedNetworkName;
                        }
                        catch { }
                    }

                    string networkName = _identityService.NormalizeNetworkName(detectedNetworkName, iface);
                    string connType = identity?.Type.ToString() ?? (iface != null && (iface.StartsWith("wl") || iface.StartsWith("wlan")) ? "WiFi" : "Ethernet");

                    _currentSession = new NetworkSession
                    {
                        InterfaceName = iface ?? "Unknown",
                        ConnectionType = connType,
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
