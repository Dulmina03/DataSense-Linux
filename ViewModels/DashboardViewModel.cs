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
    private readonly INetworkMonitorWorker     _networkMonitorWorker;
    private readonly INetworkUsageRepository   _repository;
    private readonly INetworkConnectionService _connectionService;
    private readonly IAnalyticsService         _analyticsService;
    private readonly ProcessNetworkMonitorWorker _processMonitorWorker;
    private bool     _disposed;
    private int      _tickCount  = 4; // Start at 4 so first tick triggers details load immediately
    private DateTime _lastAnalyticsDate = DateTime.MinValue; // Track UTC date for midnight auto-refresh

    // ── Live monitoring properties ──────────────────────────────────────────

    [ObservableProperty] private string _activeInterface    = "Unknown";
    [ObservableProperty] private string _downloadSpeedText  = "0.0 B/s";
    [ObservableProperty] private string _uploadSpeedText    = "0.0 B/s";
    [ObservableProperty] private string _totalDownloadedText = "0 B";
    [ObservableProperty] private string _totalUploadedText   = "0 B";
    [ObservableProperty] private string _statusText          = "Standby";
    [ObservableProperty] private string _statusDotColor      = "#555566"; // grey until connected

    // ── Today summary properties ────────────────────────────────────────────

    [ObservableProperty] private string _todayDownloadedText   = "—";
    [ObservableProperty] private string _todayUploadedText     = "—";
    [ObservableProperty] private string _todayTotalText        = "—";
    [ObservableProperty] private string _todayVsYesterdayText  = "—";   // e.g. "+12%" / "-5%"
    [ObservableProperty] private string _todayDeltaColor       = "#888899"; // green / red / neutral
    [ObservableProperty] private bool   _hasTodayDelta         = false;

    // ── Yesterday summary properties ────────────────────────────────────────

    [ObservableProperty] private string _yesterdayTotalText = "—";

    // ── This month summary properties ───────────────────────────────────────

    [ObservableProperty] private string _monthDownloadedText = "—";
    [ObservableProperty] private string _monthUploadedText   = "—";
    [ObservableProperty] private string _monthTotalText      = "—";

    // ── Insights row ────────────────────────────────────────────────────────

    [ObservableProperty] private string _avgDailyText = "—";
    [ObservableProperty] private string _peakDayText  = "—"; // e.g. "Aug 14 · 3.2 GB"

    // ── Download vs Upload ratio ────────────────────────────────────────────

    [ObservableProperty] private string     _downloadRatioText    = "—";
    [ObservableProperty] private string     _uploadRatioText      = "—";
    [ObservableProperty] private string     _downloadActualText   = "—"; // byte value in legend
    [ObservableProperty] private string     _uploadActualText     = "—"; // byte value in legend
    [ObservableProperty] private bool       _hasMonthData         = false; // guard ratio bar

    // GridLength properties so AXAML compiled bindings can drive ColumnDefinition.Width
    [ObservableProperty] private GridLength _downloadColumnWidth = new GridLength(1, GridUnitType.Star);
    [ObservableProperty] private GridLength _uploadColumnWidth   = new GridLength(1, GridUnitType.Star);

    // ── Connection details properties ───────────────────────────────────────

    [ObservableProperty] private string _connectionType  = "—";
    [ObservableProperty] private string _connectionState = "—";
    [ObservableProperty] private string _connectionName  = "—";
    [ObservableProperty] private string _ipv4Address     = "—";
    [ObservableProperty] private string _ipv6Address     = "—";
    [ObservableProperty] private string _gateway         = "—";
    [ObservableProperty] private string _dnsServers      = "—";
    [ObservableProperty] private string _macAddress      = "—";
    [ObservableProperty] private string _wifiSsid        = "—";
    [ObservableProperty] private int    _wifiSignalStrength     = -1;
    [ObservableProperty] private string _wifiSignalStrengthText = "—";
    [ObservableProperty] private string _linkSpeed              = "—";
    [ObservableProperty] private bool   _hasWifi                = false;
    [ObservableProperty] private bool   _isConnectionDetailsLoading = false;

    [ObservableProperty] private string _networkTypeText = "—";
    [ObservableProperty] private string _networkIdentityText = "—";

    // ── Chart ───────────────────────────────────────────────────────────────

    public ObservableCollection<DailyChartBarViewModel> DailyChartItems { get; } = new();

    /// <summary>
    /// Pixel width of the chart canvas.  Updated by the view's SizeChanged handler.
    /// DashboardViewModel.BuildChartItems() uses this value when computing bar geometry.
    /// </summary>
    [ObservableProperty] private double _chartWidth = 560.0;

    // ── Analytics load state ────────────────────────────────────────────────

    [ObservableProperty] private bool    _isAnalyticsLoading = true;
    [ObservableProperty] private string? _analyticsError;
    [ObservableProperty] private bool    _isChartEmpty = true;

    // ── Period Analytics ────────────────────────────────────────────────────

    [ObservableProperty] private AnalyticsPeriod _selectedPeriod = AnalyticsPeriod.Last7Days;
    
    [ObservableProperty] private string _periodTotalDownloadedText = "—";
    [ObservableProperty] private string _periodTotalUploadedText   = "—";
    [ObservableProperty] private string _periodTotalUsageText      = "—";
    [ObservableProperty] private string _periodAvgDailyText        = "—";
    [ObservableProperty] private string _periodPeakDayText         = "—";
    [ObservableProperty] private string _peakHourText              = "—";
    [ObservableProperty] private string _peakHourUsageText         = "—";
    [ObservableProperty] private string _peakDayInPeriodText       = "—";
    [ObservableProperty] private string _peakDayInPeriodUsageText  = "—";
    [ObservableProperty] private bool   _isHourlyChart             = false;
    [ObservableProperty] private bool   _isPeriodChartEmpty        = true;
    [ObservableProperty] private bool   _isPeriodAnalyticsLoading  = false;

    public ObservableCollection<DailyChartBarViewModel> PeriodChartItems { get; } = new();
    public ObservableCollection<HourlyChartBarViewModel> HourlyChartItems { get; } = new();

    // ── Process Analytics ───────────────────────────────────────────────────
    public ObservableCollection<ProcessNetworkUsage> LiveProcessTraffic { get; } = new();
    public ObservableCollection<ProcessUsageRecord> TopProcesses { get; } = new();

    // ── Chart layout constants ──────────────────────────────────────────────

    /// <summary>Fixed canvas height for the bar chart area in device-independent pixels.</summary>
    public const double ChartHeight = 160.0;

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
        INetworkMonitorWorker    networkMonitorWorker,
        INetworkUsageRepository  repository,
        INetworkConnectionService connectionService,
        IAnalyticsService         analyticsService,
        ProcessNetworkMonitorWorker processMonitorWorker)
    {
        _networkMonitorWorker = networkMonitorWorker ?? throw new ArgumentNullException(nameof(networkMonitorWorker));
        _repository           = repository           ?? throw new ArgumentNullException(nameof(repository));
        _connectionService    = connectionService    ?? throw new ArgumentNullException(nameof(connectionService));
        _analyticsService     = analyticsService     ?? throw new ArgumentNullException(nameof(analyticsService));
        _processMonitorWorker = processMonitorWorker ?? throw new ArgumentNullException(nameof(processMonitorWorker));

        // Populate live card with current worker state immediately
        UpdateLiveValues(
            _networkMonitorWorker.ActiveInterface,
            _networkMonitorWorker.DownloadSpeed,
            _networkMonitorWorker.UploadSpeed,
            _networkMonitorWorker.TotalBytesDownloaded,
            _networkMonitorWorker.TotalBytesUploaded);

        // Subscribe to live updates
        _networkMonitorWorker.NetworkUsageUpdated += OnNetworkUsageUpdated;
        _processMonitorWorker.LiveTrafficUpdated += OnLiveTrafficUpdated;

        // Kick off async analytics (fire-and-forget; errors surfaced via AnalyticsError)
        _ = LoadAnalyticsAsync();

        // Initial load of connection details
        _ = LoadConnectionDetailsAsync(_networkMonitorWorker.ActiveInterface);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Commands
    // ────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SelectPeriodAsync(string periodString)
    {
        if (Enum.TryParse<AnalyticsPeriod>(periodString, out var period))
        {
            if (SelectedPeriod != period)
            {
                SelectedPeriod = period;
                await LoadPeriodAnalyticsAsync(showLoading: true);
            }
        }
    }

    [RelayCommand]
    private void NavigateToProcessAnalytics(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return;
        
        // Find MainWindowViewModel via DI
        var mainWindowVm = App.Services?.GetService(typeof(MainWindowViewModel)) as MainWindowViewModel;
        mainWindowVm?.NavigateToApplicationAnalytics(processName);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Chart width — called from view's SizeChanged handler
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by the view when the chart container changes width.
    /// Triggers a chart geometry rebuild on the background thread.
    /// </summary>
    public void UpdateChartWidth(double newWidth)
    {
        if (newWidth < 50) return; // ignore degenerate sizes
        double rounded = Math.Floor(newWidth);
        if (Math.Abs(rounded - ChartWidth) < 10) return; // ignore sub-10 px jitter
        ChartWidth = rounded;
        _ = RebuildChartAsync();
    }

    private async Task RebuildChartAsync()
    {
        // Re-fetch last ChartDays of daily data and rebuild bars with the new width
        try
        {
            var utcNow    = DateTime.UtcNow;
            var start     = utcNow.Date.AddDays(-(ChartDays - 1));
            var end       = utcNow.Date.AddDays(1).AddTicks(-1);
            var dailyRaw  = (await _repository.GetDailyUsageAsync(start, end)).ToList();
            dailyRaw.Reverse();
            var chartData  = dailyRaw.TakeLast(ChartDays).ToList();
            var chartItems = BuildChartItems(chartData, ChartWidth);

            Dispatcher.UIThread.Post(() =>
            {
                DailyChartItems.Clear();
                foreach (var item in chartItems)
                    DailyChartItems.Add(item);
                IsChartEmpty = !chartItems.Any(b => b.HasData);
            });

            // Also rebuild the period-aware chart for the new width without showing loading overlay
            _ = LoadPeriodAnalyticsAsync(showLoading: false);
        }
        catch
        {
            // Non-fatal — existing bars stay visible
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Live monitoring
    // ────────────────────────────────────────────────────────────────────────

    private void OnLiveTrafficUpdated(IEnumerable<ProcessNetworkUsage> currentBatch)
    {
        // Limit to processes actually transmitting data, sort by download + upload
        var active = currentBatch
            .Where(p => p.DownloadRateBytesPerSec > 0 || p.UploadRateBytesPerSec > 0)
            .OrderByDescending(p => p.DownloadRateBytesPerSec + p.UploadRateBytesPerSec)
            .Take(10)
            .ToList();

        Dispatcher.UIThread.Post(() =>
        {
            LiveProcessTraffic.Clear();
            foreach (var process in active)
            {
                LiveProcessTraffic.Add(process);
            }
        });
    }

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

        // Auto-refresh analytics when the UTC calendar date has rolled over midnight
        var utcToday = DateTime.UtcNow.Date;
        if (utcToday != _lastAnalyticsDate && _lastAnalyticsDate != DateTime.MinValue)
            _ = LoadAnalyticsAsync();

        // Query connection details and analytics every 5 seconds (5 ticks)
        _tickCount++;
        if (_tickCount >= 5)
        {
            _tickCount = 0;
            _ = LoadConnectionDetailsAsync(usage.InterfaceName);
            _ = LoadAnalyticsAsync(showLoading: false);
        }
    }

    private void UpdateLiveValues(
        string? iface,
        double  downloadSpeed,
        double  uploadSpeed,
        long    bytesReceived,
        long    bytesSent)
    {
        bool isConnected   = !string.IsNullOrEmpty(iface) && iface != "None";
        ActiveInterface     = isConnected ? iface! : "Disconnected";
        DownloadSpeedText   = ByteFormatter.FormatSpeed(downloadSpeed);
        UploadSpeedText     = ByteFormatter.FormatSpeed(uploadSpeed);
        TotalDownloadedText = ByteFormatter.FormatBytes(bytesReceived);
        TotalUploadedText   = ByteFormatter.FormatBytes(bytesSent);
        StatusText          = isConnected ? "Monitoring" : "Offline";
        StatusDotColor      = isConnected ? "#00E676" : "#555566";
    }

    // ────────────────────────────────────────────────────────────────────────
    // Analytics loading
    // ── Analytics loading ───────────────────────────────────────────────────

    private async Task LoadAnalyticsAsync(bool showLoading = true)
    {
        // Show loading state on UI thread before kicking off the background work
        if (showLoading)
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsAnalyticsLoading = true;
                AnalyticsError     = null;
            });
        }

        try
        {
            var utcNow = DateTime.UtcNow;

            // ── 1. Today summary ─────────────────────────────────────────────
            var (todayDl, todayUl) = await _repository.GetTodaySummaryAsync();
            long todayTotal        = todayDl + todayUl;

            // ── 2. Yesterday summary ─────────────────────────────────────────
            var yesterdayStart = utcNow.Date.AddDays(-1);
            var yesterdayEnd   = utcNow.Date.AddTicks(-1);
            var yesterdayDaily = (await _repository.GetDailyUsageAsync(yesterdayStart, yesterdayEnd)).FirstOrDefault();
            long yesterdayTotal = yesterdayDaily?.TotalBytes ?? 0;
            bool hasYesterday   = yesterdayDaily != null;

            // Delta percentage vs yesterday
            string deltaText  = "—";
            string deltaColor = "#888899";
            bool   hasDelta   = false;
            if (hasYesterday && yesterdayTotal > 0)
            {
                double pct = (todayTotal - yesterdayTotal) / (double)yesterdayTotal * 100.0;
                string sign = pct >= 0 ? "+" : "";
                deltaText  = $"{sign}{pct:F0}% vs yesterday";
                deltaColor = pct >= 0 ? "#FF9800" : "#00E676"; // orange = higher, green = lower
                hasDelta   = true;
            }

            // ── 3. Month summary ─────────────────────────────────────────────
            var (monthDl, monthUl) = await _repository.GetMonthSummaryAsync();
            long monthTotal        = monthDl + monthUl;

            // Ratio (guard: avoid division by zero)
            double dlRatio = monthTotal > 0 ? (double)monthDl / monthTotal : 0.5;
            double ulRatio = monthTotal > 0 ? (double)monthUl / monthTotal : 0.5;

            // ── 4. Daily chart data — last ChartDays ─────────────────────────
            var chartStart = utcNow.Date.AddDays(-(ChartDays - 1));
            var chartEnd   = utcNow.Date.AddDays(1).AddTicks(-1);
            var dailyRaw   = (await _repository.GetDailyUsageAsync(chartStart, chartEnd)).ToList();

            // GetDailyUsageAsync returns ORDER BY Day DESC — reverse to chronological
            dailyRaw.Reverse();
            var chartData = dailyRaw.TakeLast(ChartDays).ToList();

            // ── 5. Insights — avg/day and peak day ──────────────────────────
            var daysWithData = chartData.Where(d => d.TotalBytes > 0).ToList();
            long avgDailyBytes = daysWithData.Count > 0
                ? (long)daysWithData.Average(d => d.TotalBytes)
                : 0;

            DailyUsageRecord? peakDay = daysWithData.Count > 0
                ? daysWithData.MaxBy(d => d.TotalBytes)
                : null;

            string peakDayText = peakDay != null
                ? $"{peakDay.Day:MMM d} · {ByteFormatter.FormatBytes(peakDay.TotalBytes)}"
                : "—";

            // ── 6. Build chart items with current ChartWidth ─────────────────
            var chartItems = BuildChartItems(chartData, ChartWidth);

            // ── 7. Post all UI updates atomically on the UI thread ────────────
            Dispatcher.UIThread.Post(() =>
            {
                // Today
                TodayDownloadedText  = ByteFormatter.FormatBytes(todayDl);
                TodayUploadedText    = ByteFormatter.FormatBytes(todayUl);
                TodayTotalText       = ByteFormatter.FormatBytes(todayTotal);
                TodayVsYesterdayText = deltaText;
                TodayDeltaColor      = deltaColor;
                HasTodayDelta        = hasDelta;

                // Yesterday
                YesterdayTotalText   = hasYesterday
                    ? ByteFormatter.FormatBytes(yesterdayTotal)
                    : "—";

                // Month
                MonthDownloadedText = ByteFormatter.FormatBytes(monthDl);
                MonthUploadedText   = ByteFormatter.FormatBytes(monthUl);
                MonthTotalText      = ByteFormatter.FormatBytes(monthTotal);
                HasMonthData        = monthTotal > 0;

                // Ratio bar
                DownloadRatioText  = monthTotal > 0 ? $"{dlRatio * 100:F0}%" : "—";
                UploadRatioText    = monthTotal > 0 ? $"{ulRatio * 100:F0}%" : "—";
                DownloadActualText = monthTotal > 0 ? ByteFormatter.FormatBytes(monthDl) : "—";
                UploadActualText   = monthTotal > 0 ? ByteFormatter.FormatBytes(monthUl) : "—";

                // Enforce minimum visible segment only when both sides are non-zero
                if (monthTotal > 0 && monthDl > 0 && monthUl > 0)
                {
                    DownloadColumnWidth = new GridLength(Math.Max(dlRatio, 0.05), GridUnitType.Star);
                    UploadColumnWidth   = new GridLength(Math.Max(ulRatio, 0.05), GridUnitType.Star);
                }
                else
                {
                    DownloadColumnWidth = new GridLength(1, GridUnitType.Star);
                    UploadColumnWidth   = new GridLength(1, GridUnitType.Star);
                }

                // Insights
                AvgDailyText = avgDailyBytes > 0 ? ByteFormatter.FormatBytes(avgDailyBytes) : "—";
                PeakDayText  = peakDayText;

                // Chart
                DailyChartItems.Clear();
                foreach (var item in chartItems)
                    DailyChartItems.Add(item);
                IsChartEmpty = !chartItems.Any(b => b.HasData);

                if (showLoading)
                {
                    IsAnalyticsLoading = false;
                }
            });

            // Record the UTC date so midnight auto-refresh fires correctly
            _lastAnalyticsDate = utcNow.Date;

            // Load period analytics
            await LoadPeriodAnalyticsAsync(showLoading);
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                AnalyticsError     = $"Analytics unavailable: {ex.Message}";
                if (showLoading)
                {
                    IsAnalyticsLoading = false;
                }
            });
        }
    }

    private async Task LoadPeriodAnalyticsAsync(bool showLoading)
    {
        if (showLoading)
        {
            Dispatcher.UIThread.Post(() => IsPeriodAnalyticsLoading = true);
        }

        try
        {
            var summary = await _analyticsService.GetSummaryAsync(SelectedPeriod);

            if (SelectedPeriod == AnalyticsPeriod.Today)
            {
                var hourlyData = await _analyticsService.GetTodayHourlyAsync();
                var hourlyItems = BuildHourlyChartItems(hourlyData.ToList(), ChartWidth);

                Dispatcher.UIThread.Post(() =>
                {
                    IsHourlyChart = true;
                    HourlyChartItems.Clear();
                    foreach (var item in hourlyItems) HourlyChartItems.Add(item);
                    IsPeriodChartEmpty = !hourlyItems.Any(i => i.HasData);
                });
            }
            else
            {
                var dailyData = await _analyticsService.GetDailySeriesAsync(SelectedPeriod);
                var dailyItems = BuildChartItems(dailyData.ToList(), ChartWidth);

                Dispatcher.UIThread.Post(() =>
                {
                    IsHourlyChart = false;
                    PeriodChartItems.Clear();
                    foreach (var item in dailyItems) PeriodChartItems.Add(item);
                    IsPeriodChartEmpty = !dailyItems.Any(i => i.HasData);
                });
            }

            Dispatcher.UIThread.Post(() =>
            {
                PeriodTotalDownloadedText = ByteFormatter.FormatBytes(summary.TotalDownloaded);
                PeriodTotalUploadedText   = ByteFormatter.FormatBytes(summary.TotalUploaded);
                PeriodTotalUsageText      = ByteFormatter.FormatBytes(summary.TotalUsage);
                PeriodAvgDailyText        = summary.AvgDailyBytes > 0 ? ByteFormatter.FormatBytes(summary.AvgDailyBytes) : "—";
                
                PeriodPeakDayText         = summary.PeakDay != null ? summary.PeakDay.Day.ToString("MMM d") : "—";
                PeakDayInPeriodText       = summary.PeakDay != null ? summary.PeakDay.Day.ToString("dddd") : "—";
                PeakDayInPeriodUsageText  = summary.PeakDay != null ? ByteFormatter.FormatBytes(summary.PeakDay.TotalBytes) : "—";

                PeakHourText              = summary.PeakHourToday != null ? $"{summary.PeakHourToday.Hour:00}:00 - {summary.PeakHourToday.Hour + 1:00}:00" : "—";
                PeakHourUsageText         = summary.PeakHourToday != null ? ByteFormatter.FormatBytes(summary.PeakHourToday.TotalBytes) : "—";

                if (showLoading) IsPeriodAnalyticsLoading = false;
            });

            // Load Top Processes
            var topProcesses = await _analyticsService.GetTopDataConsumersAsync(SelectedPeriod, 5);
            Dispatcher.UIThread.Post(() =>
            {
                TopProcesses.Clear();
                foreach (var process in topProcesses)
                {
                    TopProcesses.Add(process);
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Period analytics failed: {ex.Message}");
            Dispatcher.UIThread.Post(() => { if (showLoading) IsPeriodAnalyticsLoading = false; });
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Chart geometry
    // ────────────────────────────────────────────────────────────────────────

    private static List<DailyChartBarViewModel> BuildChartItems(
        List<DailyUsageRecord> daily, double chartWidth)
    {
        if (daily.Count == 0) return new List<DailyChartBarViewModel>();

        long maxTotal = daily.Max(d => d.TotalBytes);
        if (maxTotal <= 0) maxTotal = 1; // guard: prevent division by zero

        int    count    = daily.Count;
        double barWidth = (chartWidth - (count - 1) * BarGap) / Math.Max(count, 1);

        var items = new List<DailyChartBarViewModel>(count);

        for (int i = 0; i < count; i++)
        {
            var d = daily[i];

            double totalBarHeight = (double)d.TotalBytes / maxTotal * ChartHeight;

            // Stacked: upload on top, download on bottom
            double dlFrac = d.TotalBytes > 0 ? (double)d.BytesDownloaded / d.TotalBytes : 0.5;
            double ulFrac = 1.0 - dlFrac;

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
                DownloadBarY    = dlBarY,
                UploadBarY      = ulBarY,
                LabelY          = ChartHeight + 4,
            });
        }

        return items;
    }

    private static List<HourlyChartBarViewModel> BuildHourlyChartItems(
        List<HourlyUsageRecord> hourly, double chartWidth)
    {
        if (hourly.Count == 0) return new List<HourlyChartBarViewModel>();

        long maxTotal = hourly.Max(h => h.TotalBytes);
        if (maxTotal <= 0) maxTotal = 1; // guard

        int    count    = hourly.Count;
        double barWidth = (chartWidth - (count - 1) * BarGap) / Math.Max(count, 1);

        var items = new List<HourlyChartBarViewModel>(count);

        for (int i = 0; i < count; i++)
        {
            var h = hourly[i];

            double totalBarHeight = (double)h.TotalBytes / maxTotal * ChartHeight;

            double dlFrac = h.TotalBytes > 0 ? (double)h.BytesDownloaded / h.TotalBytes : 0.5;
            double ulFrac = 1.0 - dlFrac;

            double dlBarHeight = totalBarHeight * dlFrac;
            double ulBarHeight = totalBarHeight * ulFrac;

            double dlBarY = ChartHeight - dlBarHeight;
            double ulBarY = dlBarY - ulBarHeight;

            double barX = i * (barWidth + BarGap);

            items.Add(new HourlyChartBarViewModel
            {
                Hour            = h.Hour,
                BytesDownloaded = h.BytesDownloaded,
                BytesUploaded   = h.BytesUploaded,
                TotalBytes      = h.TotalBytes,
                DownloadedText  = ByteFormatter.FormatBytes(h.BytesDownloaded),
                UploadedText    = ByteFormatter.FormatBytes(h.BytesUploaded),
                TotalText       = ByteFormatter.FormatBytes(h.TotalBytes),
                BarX            = barX,
                BarWidth        = Math.Max(barWidth, 1),
                DownloadBarHeight = Math.Max(dlBarHeight, 0),
                UploadBarHeight   = Math.Max(ulBarHeight, 0),
                DownloadBarY    = dlBarY,
                UploadBarY      = ulBarY,
                LabelY          = ChartHeight + 4,
            });
        }

        return items;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Connection details
    // ────────────────────────────────────────────────────────────────────────

    private async Task LoadConnectionDetailsAsync(string? interfaceName)
    {
        if (string.IsNullOrEmpty(interfaceName) || interfaceName == "None" || interfaceName == "Disconnected")
        {
            Dispatcher.UIThread.Post(() =>
            {
                ConnectionType         = "—";
                ConnectionState        = "Disconnected";
                ConnectionName         = "—";
                Ipv4Address            = "—";
                Ipv6Address            = "—";
                Gateway                = "—";
                DnsServers             = "—";
                MacAddress             = "—";
                WifiSsid               = "—";
                WifiSignalStrength     = -1;
                WifiSignalStrengthText = "—";
                LinkSpeed              = "—";
                HasWifi                = false;
                NetworkTypeText        = "Disconnected";
                NetworkIdentityText    = "—";
            });
            return;
        }

        Dispatcher.UIThread.Post(() => IsConnectionDetailsLoading = true);

        try
        {
            var details = await _connectionService.GetConnectionDetailsAsync(interfaceName);

            Dispatcher.UIThread.Post(() =>
            {
                ConnectionType         = details.ConnectionType;
                ConnectionState        = details.ConnectionState;
                ConnectionName         = details.ConnectionName;
                Ipv4Address            = details.Ipv4Address;
                Ipv6Address            = details.Ipv6Address;
                Gateway                = details.Gateway;
                DnsServers             = details.DnsServers;
                MacAddress             = details.MacAddress;
                WifiSsid               = details.WifiSsid;
                WifiSignalStrength     = details.WifiSignalStrength;
                WifiSignalStrengthText = details.WifiSignalStrength >= 0 ? $"{details.WifiSignalStrength}%" : "—";
                LinkSpeed              = details.LinkSpeed;
                HasWifi                = details.ConnectionType.Equals("wifi", StringComparison.OrdinalIgnoreCase);
                IsConnectionDetailsLoading = false;

                if (HasWifi)
                {
                    NetworkTypeText = "Wi-Fi";
                    NetworkIdentityText = !string.IsNullOrEmpty(details.WifiSsid) && details.WifiSsid != "—" ? details.WifiSsid : "Connected";
                }
                else if (details.ConnectionType.Equals("ethernet", StringComparison.OrdinalIgnoreCase))
                {
                    NetworkTypeText = "Ethernet";
                    NetworkIdentityText = "Connected";
                }
                else if (string.IsNullOrEmpty(interfaceName) || interfaceName == "None" || interfaceName == "Disconnected")
                {
                    NetworkTypeText = "Disconnected";
                    NetworkIdentityText = "—";
                }
                else
                {
                    NetworkTypeText = details.ConnectionType;
                    NetworkIdentityText = !string.IsNullOrEmpty(details.ConnectionName) && details.ConnectionName != "—" ? details.ConnectionName : "Connected";
                }
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
                _processMonitorWorker.LiveTrafficUpdated -= OnLiveTrafficUpdated;
            }
            _disposed = true;
        }
    }
}
