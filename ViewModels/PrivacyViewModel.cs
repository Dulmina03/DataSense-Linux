using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataSense.Database;
using DataSense.Services;

namespace DataSense.ViewModels;

public partial class PrivacyViewModel : ViewModelBase
{
    private readonly INetworkUsageRepository _repository;
    private readonly ILinuxStorageService? _storageService;
    private readonly ILinuxPlatformService? _platformService;

    public override string Title => "Privacy & Data Lifecycle";

    [ObservableProperty] private string _dbPath = string.Empty;
    [ObservableProperty] private string _logPath = string.Empty;
    [ObservableProperty] private string _dbSizeText = "Calculating...";
    [ObservableProperty] private int    _retentionDays = 90;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool   _isPurging = false;
    [ObservableProperty] private string _storageArchitecture = "Local-First XDG Compliant";

    // ── Phase 11.26 & 11.27 Telemetry & Live Monitor Disclosures ──
    [ObservableProperty] private string _processTelemetryDisclosure = "DataSense captures per-process network bandwidth statistics (process name, executable path, user owner, upload/download rate) strictly on your local machine using Linux nethogs. No process names, traffic data, or identity information are ever sent to any remote server or external cloud.";
    [ObservableProperty] private string _processCapabilityNotice = "Per-process telemetry requires the nethogs utility with CAP_NET_RAW capability. DataSense itself runs entirely unprivileged without root access.";
    [ObservableProperty] private string _liveMonitoringDisclosure = "Live Monitoring maintains a short-lived, in-memory rolling chart window (max 300 samples). High-frequency per-second graph data is never persisted to SQLite or written to disk, ensuring optimal performance and zero unnecessary storage overhead.";
    [ObservableProperty] private string _sessionTimelineDisclosure = "Network sessions are tracked chronologically with local process attribution. All session timeline history, intelligent switch event logs, and comparative analytics are stored entirely locally in your SQLite database. DataSense never transmits your browsing history or session behavior to external services.";

    public PrivacyViewModel(
        INetworkUsageRepository repository,
        ILinuxStorageService? storageService = null,
        ILinuxPlatformService? platformService = null)
    {
        _repository     = repository     ?? throw new ArgumentNullException(nameof(repository));
        _storageService = storageService;
        _platformService = platformService;

        if (_storageService != null)
        {
            DbPath = _storageService.DatabasePath;
            LogPath = Path.Combine(_storageService.LogDirectory, "datasense.log");
        }
        else
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            DbPath = Path.Combine(appDataPath, "DataSense", "datasense.db");
            LogPath = Path.Combine(appDataPath, "DataSense", "logs", "datasense.log");
        }

        _ = CalculateDbSizeAsync();
    }

    [RelayCommand]
    private async Task CalculateDbSizeAsync()
    {
        try
        {
            if (File.Exists(DbPath))
            {
                var fileInfo = new FileInfo(DbPath);
                double sizeMb = fileInfo.Length / (1024.0 * 1024.0);
                DbSizeText = $"{sizeMb:F2} MB";
            }
            else
            {
                DbSizeText = "0 MB";
            }
        }
        catch (Exception ex)
        {
            DbSizeText = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task PurgeOldDataAsync()
    {
        IsPurging = true;
        StatusMessage = "Purging old records...";

        try
        {
            await _repository.PurgeOldRecordsAsync(TimeSpan.FromDays(RetentionDays));
            StatusMessage = $"✅ Successfully purged records older than {RetentionDays} days.";
            await CalculateDbSizeAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"⚠️ Purge failed: {ex.Message}";
        }
        finally
        {
            IsPurging = false;
        }
    }
}
