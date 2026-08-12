using System;
using CommunityToolkit.Mvvm.ComponentModel;
using DataSense.Models;
using DataSense.Services;

namespace DataSense.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly INetworkMonitorWorker _networkMonitorWorker;

    [ObservableProperty]
    private string _activeInterface = "Unknown";

    [ObservableProperty]
    private string _downloadSpeedText = "0.0 B/s";

    [ObservableProperty]
    private string _uploadSpeedText = "0.0 B/s";

    [ObservableProperty]
    private string _statusText = "Standby";

    public override string Title => "Dashboard";

    public DashboardViewModel(INetworkMonitorWorker networkMonitorWorker)
    {
        _networkMonitorWorker = networkMonitorWorker ?? throw new ArgumentNullException(nameof(networkMonitorWorker));
        
        // Initialize with current values
        UpdateValues(_networkMonitorWorker.ActiveInterface, _networkMonitorWorker.DownloadSpeed, _networkMonitorWorker.UploadSpeed);

        // Subscribe to updates
        _networkMonitorWorker.NetworkUsageUpdated += OnNetworkUsageUpdated;
    }

    private void OnNetworkUsageUpdated(NetworkUsage usage)
    {
        UpdateValues(usage.InterfaceName, usage.DownloadSpeed, usage.UploadSpeed);
    }

    private void UpdateValues(string? iface, double downloadSpeed, double uploadSpeed)
    {
        ActiveInterface = string.IsNullOrEmpty(iface) || iface == "None" ? "Disconnected" : iface;
        DownloadSpeedText = FormatSpeed(downloadSpeed);
        UploadSpeedText = FormatSpeed(uploadSpeed);
        StatusText = string.IsNullOrEmpty(iface) || iface == "None" ? "Offline" : "Monitoring";
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        string[] units = { "B/s", "KB/s", "MB/s", "GB/s", "TB/s" };
        int unitIndex = 0;
        double speed = bytesPerSecond;

        while (speed >= 1024 && unitIndex < units.Length - 1)
        {
            speed /= 1024;
            unitIndex++;
        }

        return $"{speed:F1} {units[unitIndex]}";
    }
}
