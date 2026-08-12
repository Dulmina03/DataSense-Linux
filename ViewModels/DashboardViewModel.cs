using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DataSense.Helpers;
using DataSense.Models;
using DataSense.Services;

namespace DataSense.ViewModels;

public partial class DashboardViewModel : ViewModelBase, IDisposable
{
    private readonly INetworkMonitorWorker _networkMonitorWorker;
    private bool _disposed;

    [ObservableProperty]
    private string _activeInterface = "Unknown";

    [ObservableProperty]
    private string _downloadSpeedText = "0.0 B/s";

    [ObservableProperty]
    private string _uploadSpeedText = "0.0 B/s";

    [ObservableProperty]
    private string _totalDownloadedText = "0.00 B";

    [ObservableProperty]
    private string _totalUploadedText = "0.00 B";

    [ObservableProperty]
    private string _statusText = "Standby";

    public override string Title => "Dashboard";

    public DashboardViewModel(INetworkMonitorWorker networkMonitorWorker)
    {
        _networkMonitorWorker = networkMonitorWorker ?? throw new ArgumentNullException(nameof(networkMonitorWorker));
        
        // Initialize with current values
        UpdateValues(
            _networkMonitorWorker.ActiveInterface,
            _networkMonitorWorker.DownloadSpeed,
            _networkMonitorWorker.UploadSpeed,
            _networkMonitorWorker.TotalBytesDownloaded,
            _networkMonitorWorker.TotalBytesUploaded
        );

        // Subscribe to updates
        _networkMonitorWorker.NetworkUsageUpdated += OnNetworkUsageUpdated;
    }

    private void OnNetworkUsageUpdated(NetworkUsage usage)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateValues(
                usage.InterfaceName,
                usage.DownloadSpeed,
                usage.UploadSpeed,
                usage.BytesReceived,
                usage.BytesSent
            );
        });
    }

    private void UpdateValues(string? iface, double downloadSpeed, double uploadSpeed, long bytesReceived, long bytesSent)
    {
        ActiveInterface = string.IsNullOrEmpty(iface) || iface == "None" ? "Disconnected" : iface;
        DownloadSpeedText = ByteFormatter.FormatSpeed(downloadSpeed);
        UploadSpeedText = ByteFormatter.FormatSpeed(uploadSpeed);
        TotalDownloadedText = ByteFormatter.FormatBytes(bytesReceived);
        TotalUploadedText = ByteFormatter.FormatBytes(bytesSent);
        StatusText = string.IsNullOrEmpty(iface) || iface == "None" ? "Offline" : "Monitoring";
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
                _networkMonitorWorker.NetworkUsageUpdated -= OnNetworkUsageUpdated;
            }
            _disposed = true;
        }
    }
}
