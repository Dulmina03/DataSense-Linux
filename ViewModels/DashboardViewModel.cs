using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataSense.Database;
using DataSense.Helpers;
using DataSense.Models;
using DataSense.Services;

namespace DataSense.ViewModels;

public partial class DashboardViewModel : ViewModelBase, IDisposable
{
    private readonly INetworkMonitorWorker    _networkMonitorWorker;
    private readonly INetworkUsageRepository  _repository;
    private readonly INetworkConnectionService _connectionService;
    private bool _disposed;
    private int _tickCount = 4; // Start at 4 so first tick triggers details load immediately

    // ── Live monitoring properties ──────────────────────────────────────────

    [ObservableProperty] private string _activeInterface    = "Unknown";
    [ObservableProperty] private string _downloadSpeedText  = "0.0 B/s";
    [ObservableProperty] private string _uploadSpeedText    = "0.0 B/s";
    [ObservableProperty] private string _totalDownloadedText = "0.00 B";
    [ObservableProperty] private string _totalUploadedText   = "0.00 B";
    [ObservableProperty] private string _statusText          = "Standby";

    // ── Today summary properties ────────────────────────────────────────────

    [ObservableProperty] private string _todayDownloadedText = "—";
    [ObservableProperty] private string _todayUploadedText   = "—";
    [ObservableProperty] private string _todayTotalText      = "—";

    // ── This month summary properties ───────────────────────────────────────

    [ObservableProperty] private string _monthDownloadedText = "—";
    [ObservableProperty] private string _monthUploadedText   = "—";
    [ObservableProperty] private string _monthTotalText      = "—";

    // ── Download vs Upload ratio ────────────────────────────────────────────

    [ObservableProperty] private string     _downloadRatioText  = "—";
    [ObservableProperty] private string     _uploadRatioText    = "—";

    // GridLength properties so AXAML compiled bindings can drive ColumnDefinition.Width
    [ObservableProperty] private GridLength _downloadColumnWidth = new GridLength(1, GridUnitType.Star);
    [ObservableProperty] private GridLength _uploadColumnWidth   = new GridLength(1, GridUnitType.Star);

    // ── Connection details properties ───────────────────────────────────────

    [ObservableProperty] private string _connectionType = "—";
    [ObservableProperty] private string _connectionState = "—";
    [ObservableProperty] private string _connectionName = "—";
    [ObservableProperty] private string _ipv4Address = "—";
    [ObservableProperty] private string _ipv6Address = "—";
    [ObservableProperty] private string _gateway = "—";
    [ObservableProperty] private string _dnsServers = "—";
    [ObservableProperty] private string _macAddress = "—";
    [ObservableProperty] private string _wifiSsid = "—";
    [ObservableProperty] private int _wifiSignalStrength = -1;
    [ObservableProperty] private string _wifiSignalStrengthText = "—";
    [ObservableProperty] private string _linkSpeed = "—";
    [ObservableProperty] private bool _hasWifi = false;
    [ObservableProperty] private bool _isConnectionDetailsLoading = false;

    // ── Chart ───────────────────────────────────────────────────────────────

    public ObservableCollection<DailyChartBarViewModel> DailyChartItems { get; } = new();

    // ── Analytics load state ────────────────────────────────────────────────

    [ObservableProperty] private bool    _isAnalyticsLoading = true;
    [ObservableProperty] private string? _analyticsError;
    [ObservableProperty] private bool    _isChartEmpty       = true;

    // ── Chart layout constants ──────────────────────────────────────────────

    /// <summary>Fixed canvas height for the bar chart area in device-independent pixels.</summary>
    public const double ChartHeight = 160.0;

    /// <summary>Fixed canvas width used when pre-computing bar geometry.</summary>
    public const double ChartWidth  = 560.0;

    /// <summary>Gap in pixels between adjacent bars.</summary>
    private const double BarGap = 4.0;

    /// <summary>Number of days shown in the chart.</summary>
    private const int ChartDays = 14;

    // ── Title ───────────────────────────────────────────────────────────────

    public override string Title => "Dashboard";

    // ────────────────────────────────────────────────────────────────────────
    // Construction
    // ────────────────────────────────────────────────────────────────────────

    public DashboardViewModel(
        INetworkMonitorWorker   networkMonitorWorker,
        INetworkUsageRepository repository,
        INetworkConnectionService connectionService)
    {
        _networkMonitorWorker = networkMonitorWorker ?? throw new ArgumentNullException(nameof(networkMonitorWorker));
        _repository           = repository           ?? throw new ArgumentNullException(nameof(repository));
        _connectionService    = connectionService    ?? throw new ArgumentNullException(nameof(connectionService));

        // Populate live card with current worker state immediately
        UpdateLiveValues(
            _networkMonitorWorker.ActiveInterface,
            _networkMonitorWorker.DownloadSpeed,
            _networkMonitorWorker.UploadSpeed,
            _networkMonitorWorker.TotalBytesDownloaded,
            _networkMonitorWorker.TotalBytesUploaded);

        // Subscribe to live updates
        _networkMonitorWorker.NetworkUsageUpdated += OnNetworkUsageUpdated;

        // Kick off async analytics (fire-and-forget; errors surfaced via AnalyticsError)
        _ = LoadAnalyticsAsync();
        
        // Initial load of connection details
        _ = LoadConnectionDetailsAsync(_networkMonitorWorker.ActiveInterface);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Commands
    // ────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task RefreshAnalytics() => await LoadAnalyticsAsync();

    // ────────────────────────────────────────────────────────────────────────
    // Live monitoring
    // ────────────────────────────────────────────────────────────────────────

    private void OnNetworkUsageUpdated(NetworkUsage usage)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateLiveValues(
                usage.InterfaceName,
                usage.DownloadSpeed,
                usage.UploadSpeed,
                usage.BytesReceived,
                usage.BytesSent);
        });

        // Query connection details every 5 seconds (5 ticks)
        _tickCount++;
        if (_tickCount >= 5)
        {
            _tickCount = 0;
            _ = LoadConnectionDetailsAsync(usage.InterfaceName);
        }
    }

    private void UpdateLiveValues(
        string? iface,
        double  downloadSpeed,
        double  uploadSpeed,
        long    bytesReceived,
        long    bytesSent)
    {
        ActiveInterface     = string.IsNullOrEmpty(iface) || iface == "None" ? "Disconnected" : iface;
        DownloadSpeedText   = ByteFormatter.FormatSpeed(downloadSpeed);
        UploadSpeedText     = ByteFormatter.FormatSpeed(uploadSpeed);
        TotalDownloadedText = ByteFormatter.FormatBytes(bytesReceived);
        TotalUploadedText   = ByteFormatter.FormatBytes(bytesSent);
        StatusText          = string.IsNullOrEmpty(iface) || iface == "None" ? "Offline" : "Monitoring";
    }

    // ────────────────────────────────────────────────────────────────────────
    // Analytics loading
    // ────────────────────────────────────────────────────────────────────────

    private async Task LoadAnalyticsAsync()
    {
        // Show loading state on UI thread before kicking off the background work
        Dispatcher.UIThread.Post(() =>
        {
            IsAnalyticsLoading = true;
            AnalyticsError     = null;
        });

        try
        {
            // ── All DB calls happen on the calling/background thread ──────

            // 1. Today summary
            var (todayDl, todayUl) = await _repository.GetTodaySummaryAsync();
            long todayTotal        = todayDl + todayUl;

            // 2. Month summary
            var (monthDl, monthUl) = await _repository.GetMonthSummaryAsync();
            long monthTotal        = monthDl + monthUl;

            // 3. Ratio (guard: avoid division by zero)
            double dlRatio = monthTotal > 0 ? (double)monthDl / monthTotal : 0.5;
            double ulRatio = monthTotal > 0 ? (double)monthUl / monthTotal : 0.5;

            // 4. Daily chart data — fetch last 30 days, display last 14
            var utcNow = DateTime.UtcNow;
            var chartStart = utcNow.Date.AddDays(-(ChartDays - 1));
            var chartEnd   = utcNow.Date.AddDays(1).AddTicks(-1);
            var dailyRaw   = (await _repository.GetDailyUsageAsync(chartStart, chartEnd)).ToList();

            // GetDailyUsageAsync returns ORDER BY Day DESC — reverse to chronological
            dailyRaw.Reverse();

            // Take the last ChartDays entries (may be fewer if DB is newer)
            var chartData = dailyRaw.TakeLast(ChartDays).ToList();
            var chartItems = BuildChartItems(chartData);

            // 5. Post all UI updates atomically on the UI thread
            Dispatcher.UIThread.Post(() =>
            {
                // Today
                TodayDownloadedText = ByteFormatter.FormatBytes(todayDl);
                TodayUploadedText   = ByteFormatter.FormatBytes(todayUl);
                TodayTotalText      = ByteFormatter.FormatBytes(todayTotal);

                // Month
                MonthDownloadedText = ByteFormatter.FormatBytes(monthDl);
                MonthUploadedText   = ByteFormatter.FormatBytes(monthUl);
                MonthTotalText      = ByteFormatter.FormatBytes(monthTotal);

                // Ratio bar proportional star widths for ColumnDefinition.Width
                DownloadRatioText    = monthTotal > 0 ? $"{dlRatio * 100:F0}%" : "—";
                UploadRatioText      = monthTotal > 0 ? $"{ulRatio * 100:F0}%" : "—";
                DownloadColumnWidth  = new GridLength(Math.Max(dlRatio, 0.01), GridUnitType.Star);
                UploadColumnWidth    = new GridLength(Math.Max(ulRatio, 0.01), GridUnitType.Star);

                // Chart
                DailyChartItems.Clear();
                foreach (var item in chartItems)
                    DailyChartItems.Add(item);

                IsChartEmpty       = !chartItems.Any(b => b.HasData);
                IsAnalyticsLoading = false;
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                AnalyticsError     = $"Analytics unavailable: {ex.Message}";
                IsAnalyticsLoading = false;
            });
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Chart geometry
    // ────────────────────────────────────────────────────────────────────────

    private static List<DailyChartBarViewModel> BuildChartItems(List<DailyUsageRecord> daily)
    {
        if (daily.Count == 0) return new List<DailyChartBarViewModel>();

        long maxTotal = daily.Max(d => d.TotalBytes);
        if (maxTotal <= 0) maxTotal = 1; // guard: prevent division by zero

        int    count    = daily.Count;
        double barWidth = (ChartWidth - (count - 1) * BarGap) / Math.Max(count, 1);

        var items = new List<DailyChartBarViewModel>(count);

        for (int i = 0; i < count; i++)
        {
            var d = daily[i];

            double totalBarHeight = (double)d.TotalBytes / maxTotal * ChartHeight;

            // Stacked: upload on top, download on bottom
            double dlFrac   = d.TotalBytes > 0 ? (double)d.BytesDownloaded / d.TotalBytes : 0.5;
            double ulFrac   = 1.0 - dlFrac;

            double dlBarHeight = totalBarHeight * dlFrac;
            double ulBarHeight = totalBarHeight * ulFrac;

            // Bottom-aligned within chart area
            double dlBarY = ChartHeight - dlBarHeight;
            double ulBarY = dlBarY - ulBarHeight;

            double barX = i * (barWidth + BarGap);

            items.Add(new DailyChartBarViewModel
            {
                DayLabel        = d.Day.ToString("MMM d"),
                BytesDownloaded = d.BytesDownloaded,
                BytesUploaded   = d.BytesUploaded,
                TotalBytes      = d.TotalBytes,
                DownloadedText  = ByteFormatter.FormatBytes(d.BytesDownloaded),
                UploadedText    = ByteFormatter.FormatBytes(d.BytesUploaded),
                TotalText       = ByteFormatter.FormatBytes(d.TotalBytes),
                BarX            = barX,
                BarWidth        = Math.Max(barWidth, 1),
                DownloadBarHeight = Math.Max(dlBarHeight, 0),
                UploadBarHeight   = Math.Max(ulBarHeight, 0),
                DownloadBarY      = dlBarY,
                UploadBarY        = ulBarY,
                LabelY          = ChartHeight + 4,
            });
        }

        return items;
    }

    private async Task LoadConnectionDetailsAsync(string? interfaceName)
    {
        if (string.IsNullOrEmpty(interfaceName) || interfaceName == "None" || interfaceName == "Disconnected")
        {
            Dispatcher.UIThread.Post(() =>
            {
                ConnectionType = "—";
                ConnectionState = "Disconnected";
                ConnectionName = "—";
                Ipv4Address = "—";
                Ipv6Address = "—";
                Gateway = "—";
                DnsServers = "—";
                MacAddress = "—";
                WifiSsid = "—";
                WifiSignalStrength = -1;
                WifiSignalStrengthText = "—";
                LinkSpeed = "—";
                HasWifi = false;
            });
            return;
        }

        Dispatcher.UIThread.Post(() => IsConnectionDetailsLoading = true);

        try
        {
            var details = await _connectionService.GetConnectionDetailsAsync(interfaceName);
            
            Dispatcher.UIThread.Post(() =>
            {
                ConnectionType = details.ConnectionType;
                ConnectionState = details.ConnectionState;
                ConnectionName = details.ConnectionName;
                Ipv4Address = details.Ipv4Address;
                Ipv6Address = details.Ipv6Address;
                Gateway = details.Gateway;
                DnsServers = details.DnsServers;
                MacAddress = details.MacAddress;
                WifiSsid = details.WifiSsid;
                WifiSignalStrength = details.WifiSignalStrength;
                WifiSignalStrengthText = details.WifiSignalStrength >= 0 ? $"{details.WifiSignalStrength}%" : "—";
                LinkSpeed = details.LinkSpeed;
                HasWifi = details.ConnectionType.Equals("wifi", StringComparison.OrdinalIgnoreCase);
                IsConnectionDetailsLoading = false;
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load connection details: {ex.Message}");
            Dispatcher.UIThread.Post(() => IsConnectionDetailsLoading = false);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Dispose
    // ────────────────────────────────────────────────────────────────────────

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
