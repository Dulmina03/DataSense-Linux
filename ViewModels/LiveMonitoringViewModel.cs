using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataSense.Database;
using DataSense.Helpers;
using DataSense.Models;
using DataSense.Services;

namespace DataSense.ViewModels;

public partial class LiveMonitoringViewModel : ViewModelBase, IDisposable
{
    private readonly ILiveMonitoringEngine _engine;
    private readonly ILinuxInterfaceStatsService _statsService;
    private readonly INetworkMonitorService _networkMonitor;
    private readonly ProcessNetworkMonitorWorker _processMonitorWorker;
    private readonly INetworkConnectionService _connectionService;
    private readonly INetworkUsageRepository _repository;
    private readonly IExportService _exportService;
    private readonly DispatcherTimer _refreshTimer;
    private bool _disposed;

    public override string Title => "Live Traffic Monitor";

    // ── Overview ─────────────────────────────────────────────────────────────
    [ObservableProperty] private string _currentNetworkName = "—";
    [ObservableProperty] private string _connectionType = "—";
    [ObservableProperty] private string _interfaceName = "—";
    [ObservableProperty] private string _connectionStatus = "Connecting";
    [ObservableProperty] private string _liveDownloadSpeed = "0 B/s";
    [ObservableProperty] private string _liveUploadSpeed = "0 B/s";
    [ObservableProperty] private string _combinedSpeed = "0 B/s";
    [ObservableProperty] private string _sessionDurationText = "00:00:00";
    [ObservableProperty] private string _sessionDownloadedText = "0 B";
    [ObservableProperty] private string _sessionUploadedText = "0 B";
    [ObservableProperty] private string _sessionTotalText = "0 B";

    // ── Chart Window Controls ────────────────────────────────────────────────
    [ObservableProperty] private GraphWindowTime _selectedWindow = GraphWindowTime.SixtySeconds;
    [ObservableProperty] private double _chartWidth = 580.0;
    public const double ChartHeight = 150.0;
    public ObservableCollection<LiveTrafficSample> ChartSamples { get; } = new();

    // ── Process Breakdown Controls ───────────────────────────────────────────
    [ObservableProperty] private ProcessSortMode _selectedSortMode = ProcessSortMode.HighestTotal;
    [ObservableProperty] private ProcessRankCount _selectedRankCount = ProcessRankCount.Top5;
    public ObservableCollection<LiveProcessRankItem> RankedProcesses { get; } = new();
    [ObservableProperty] private LiveProcessRankItem? _selectedProcess;

    // ── Process Detail Card ──────────────────────────────────────────────────
    [ObservableProperty] private string _detailProcessName = "Select a process";
    [ObservableProperty] private string _detailPidText = "—";
    [ObservableProperty] private string _detailExePath = "—";
    [ObservableProperty] private string _detailUser = "—";
    [ObservableProperty] private string _detailDataSource = "—";
    [ObservableProperty] private string _detailDownloadSpeed = "—";
    [ObservableProperty] private string _detailUploadSpeed = "—";
    [ObservableProperty] private string _detailTodayUsage = "—";
    [ObservableProperty] private string _detail7DayUsage = "—";
    [ObservableProperty] private string _detail30DayUsage = "—";
    [ObservableProperty] private string _detailPercentageText = "—";

    // ── Interface Quality & Comparison ──────────────────────────────────────
    [ObservableProperty] private NetworkInterfaceStats? _activeInterfaceStats;
    [ObservableProperty] private string _ipv4Address = "Unavailable";
    [ObservableProperty] private string _ipv6Address = "Unavailable";
    [ObservableProperty] private string _macAddress = "Unavailable";
    [ObservableProperty] private string _packetErrorRateText = "0.00%";
    [ObservableProperty] private string _packetDropRateText = "0.00%";
    public ObservableCollection<NetworkInterfaceStats> AllInterfaces { get; } = new();

    // ── Smart Insights & Data Source ─────────────────────────────────────────
    public ObservableCollection<string> SmartInsights { get; } = new();

    // ── Status Bar & Controls ────────────────────────────────────────────────
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private string _monitoringStatusText = "Monitoring Active";
    [ObservableProperty] private string _lastUpdateText = "—";
    [ObservableProperty] private int _trackedProcessCount = 0;
    [ObservableProperty] private string _snapshotStatusMessage = string.Empty;

    public LiveMonitoringViewModel(
        ILiveMonitoringEngine engine,
        ILinuxInterfaceStatsService statsService,
        INetworkMonitorService networkMonitor,
        ProcessNetworkMonitorWorker processMonitorWorker,
        INetworkConnectionService connectionService,
        INetworkUsageRepository repository,
        IExportService exportService)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _statsService = statsService ?? throw new ArgumentNullException(nameof(statsService));
        _networkMonitor = networkMonitor ?? throw new ArgumentNullException(nameof(networkMonitor));
        _processMonitorWorker = processMonitorWorker ?? throw new ArgumentNullException(nameof(processMonitorWorker));
        _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));

        _processMonitorWorker.LiveTrafficUpdated += OnLiveProcessTrafficUpdated;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += OnRefreshTimerTick;
        _refreshTimer.Start();

        _ = RefreshConnectionDetailsAsync();
    }

    private void OnLiveProcessTrafficUpdated(IEnumerable<ProcessNetworkUsage> currentBatch)
    {
        _engine.UpdateLiveProcesses(currentBatch);
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        if (IsPaused) return;

        try
        {
            // 1. Read interface speeds & stats
            var interfaces = (await _statsService.GetAllInterfaceStatsAsync()).ToList();

            Dispatcher.UIThread.Post(() =>
            {
                AllInterfaces.Clear();
                foreach (var iface in interfaces) AllInterfaces.Add(iface);

                var active = interfaces.FirstOrDefault(i => i.IsUp) ?? interfaces.FirstOrDefault();
                ActiveInterfaceStats = active;

                if (active != null)
                {
                    InterfaceName = active.InterfaceName;
                    ConnectionType = active.ConnectionType;
                    Ipv4Address = active.IPv4Address ?? "Unavailable";
                    Ipv6Address = active.IPv6Address ?? "Unavailable";
                    MacAddress = active.MacAddress ?? "Unavailable";
                    PacketErrorRateText = $"{active.PacketErrorRatePercentage:F2}%";
                    PacketDropRateText = $"{active.PacketDropRatePercentage:F2}%";

                    LiveDownloadSpeed = ByteFormatter.FormatSpeed(active.DownloadRateBytesPerSec);
                    LiveUploadSpeed = ByteFormatter.FormatSpeed(active.UploadRateBytesPerSec);
                    CombinedSpeed = ByteFormatter.FormatSpeed(active.DownloadRateBytesPerSec + active.UploadRateBytesPerSec);

                    // Push sample to live graph engine
                    _engine.AddSample(active.DownloadRateBytesPerSec, active.UploadRateBytesPerSec);
                }
                else
                {
                    ConnectionStatus = "Disconnected";
                }

                // 2. Refresh Live Chart Samples
                var samples = _engine.GetRollingSamples(SelectedWindow);
                ChartSamples.Clear();
                foreach (var s in samples) ChartSamples.Add(s);

                // 3. Refresh Ranked Processes
                var ranked = _engine.GetRankedProcesses(SelectedSortMode, SelectedRankCount);
                RankedProcesses.Clear();
                foreach (var r in ranked) RankedProcesses.Add(r);

                // Default selection if none
                if (SelectedProcess == null && RankedProcesses.Count > 0)
                {
                    SelectedProcess = RankedProcesses[0];
                }
                else if (SelectedProcess != null)
                {
                    // Refresh selected process live speeds
                    var updated = ranked.FirstOrDefault(p => p.Pid == SelectedProcess.Pid && p.ProcessName == SelectedProcess.ProcessName);
                    if (updated != null)
                    {
                        DetailDownloadSpeed = updated.DownloadRateText;
                        DetailUploadSpeed = updated.UploadRateText;
                        DetailPercentageText = $"{updated.PercentageOfTotalTraffic:F1}%";
                    }
                }

                // 4. Refresh Smart Insights
                var insights = _engine.GenerateSmartInsights(interfaces);
                SmartInsights.Clear();
                foreach (var ins in insights) SmartInsights.Add(ins);

                // 5. Refresh Status Bar
                LastUpdateText = DateTime.UtcNow.ToString("HH:mm:ss");
                TrackedProcessCount = _processMonitorWorker.TrackedProcessCount;
                MonitoringStatusText = _processMonitorWorker.MonitoringStatus == "Running" ? "Monitoring Active" : _processMonitorWorker.MonitoringStatus;
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Live refresh failed: {ex.Message}");
        }
    }

    private async Task RefreshConnectionDetailsAsync()
    {
        try
        {
            var activeIface = (await _networkMonitor.GetAvailableInterfacesAsync()).FirstOrDefault() ?? "eth0";
            var conn = await _connectionService.GetConnectionDetailsAsync(activeIface);
            if (conn != null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    string name = !string.IsNullOrEmpty(conn.WifiSsid) && conn.WifiSsid != "—" ? conn.WifiSsid : conn.ConnectionName;
                    CurrentNetworkName = !string.IsNullOrEmpty(name) && name != "None" ? name : activeIface;
                    ConnectionType = conn.ConnectionType;
                    InterfaceName = conn.InterfaceName;
                    ConnectionStatus = conn.ConnectionState.Equals("connected", StringComparison.OrdinalIgnoreCase) || conn.ConnectionState.Equals("activated", StringComparison.OrdinalIgnoreCase) ? "Collecting" : "Connecting";
                });
            }
        }
        catch
        {
            Dispatcher.UIThread.Post(() => ConnectionStatus = "Collecting");
        }
    }

    [RelayCommand]
    private void SelectProcess(LiveProcessRankItem process)
    {
        SelectedProcess = process;
    }

    partial void OnSelectedProcessChanged(LiveProcessRankItem? value)
    {
        if (value == null) return;

        DetailProcessName = value.ProcessName;
        DetailPidText = value.Pid > 0 ? value.Pid.ToString() : "—";
        DetailExePath = !string.IsNullOrEmpty(value.ExecutablePath) ? value.ExecutablePath : "—";
        DetailUser = !string.IsNullOrEmpty(value.UserName) ? value.UserName : "—";
        DetailDataSource = value.DataSource;
        DetailDownloadSpeed = value.DownloadRateText;
        DetailUploadSpeed = value.UploadRateText;
        DetailPercentageText = $"{value.PercentageOfTotalTraffic:F1}%";

        _ = LoadProcessHistoryDetailsAsync(value.ProcessName);
    }

    private async Task LoadProcessHistoryDetailsAsync(string processName)
    {
        try
        {
            var today = await _repository.GetTopProcessesAsync(DateTime.UtcNow.Date, DateTime.UtcNow, 100);
            var last7Days = await _repository.GetTopProcessesAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, 100);
            var last30Days = await _repository.GetTopProcessesAsync(DateTime.UtcNow.AddDays(-30), DateTime.UtcNow, 100);

            var todayMatch = today.FirstOrDefault(p => p.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));
            var match7 = last7Days.FirstOrDefault(p => p.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));
            var match30 = last30Days.FirstOrDefault(p => p.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));

            Dispatcher.UIThread.Post(() =>
            {
                DetailTodayUsage = todayMatch != null ? ByteFormatter.FormatBytes(todayMatch.TotalBytes) : "0 B";
                Detail7DayUsage = match7 != null ? ByteFormatter.FormatBytes(match7.TotalBytes) : "0 B";
                Detail30DayUsage = match30 != null ? ByteFormatter.FormatBytes(match30.TotalBytes) : "0 B";
            });
        }
        catch { }
    }

    [RelayCommand]
    private void TogglePauseResume()
    {
        if (IsPaused)
        {
            IsPaused = false;
            _engine.Resume();
            _processMonitorWorker.Resume();
            MonitoringStatusText = "Monitoring Active";
        }
        else
        {
            IsPaused = true;
            _engine.Pause();
            _processMonitorWorker.Pause();
            MonitoringStatusText = "Paused";
        }
    }

    [RelayCommand]
    private void SelectWindow(string secondsStr)
    {
        if (int.TryParse(secondsStr, out int sec))
        {
            SelectedWindow = sec switch
            {
                30 => GraphWindowTime.ThirtySeconds,
                300 => GraphWindowTime.FiveMinutes,
                _ => GraphWindowTime.SixtySeconds
            };
        }
    }

    [RelayCommand]
    private void SelectSortMode(string sortModeStr)
    {
        if (Enum.TryParse<ProcessSortMode>(sortModeStr, out var mode))
        {
            SelectedSortMode = mode;
        }
    }

    [RelayCommand]
    private void SelectRankCount(string countStr)
    {
        if (int.TryParse(countStr, out int c))
        {
            SelectedRankCount = c switch
            {
                5 => ProcessRankCount.Top5,
                10 => ProcessRankCount.Top10,
                _ => ProcessRankCount.AllActive
            };
        }
    }

    [RelayCommand]
    private async Task ExportSnapshotAsync()
    {
        try
        {
            string docsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DataSense");
            string fileName = $"live_snapshot_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
            string targetPath = Path.Combine(docsPath, fileName);

            var result = await _exportService.ExportCurrentSnapshotAsync(targetPath, ActiveInterfaceStats, RankedProcesses);

            SnapshotStatusMessage = result.Success ? $"✅ Saved snapshot to {Path.GetFileName(result.FilePath)}" : $"⚠️ Export failed: {result.ErrorMessage}";
        }
        catch (Exception ex)
        {
            SnapshotStatusMessage = $"⚠️ Export failed: {ex.Message}";
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
                _refreshTimer.Stop();
                _processMonitorWorker.LiveTrafficUpdated -= OnLiveProcessTrafficUpdated;
            }
            _disposed = true;
        }
    }
}
