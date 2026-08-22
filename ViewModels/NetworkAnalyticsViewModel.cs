using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataSense.Helpers;
using DataSense.Models;
using DataSense.Services;

namespace DataSense.ViewModels;

public partial class NetworkAnalyticsViewModel : ViewModelBase
{
    private readonly IAnalyticsService _analyticsService;
    private readonly INetworkMonitorWorker _monitorWorker;
    private readonly INetworkIntelligenceService _networkIntelligenceService;
    private readonly IProcessNetworkIntelligenceService _processNetworkIntelligenceService;

    // ── Application Usage & Behavior by Network ────────────────────────────────
    public ObservableCollection<ProcessNetworkUsageSummary> NetworkProcessUsage { get; } = new();
    public ObservableCollection<ProcessNetworkInsight> BehaviorInsights { get; } = new();
    public ObservableCollection<ProcessNetworkAnomaly> ProcessAnomalies { get; } = new();

    [ObservableProperty] private bool _isNetworkProcessUsageEmpty = true;
    [ObservableProperty] private bool _isBehaviorInsightsEmpty = true;
    [ObservableProperty] private bool _isProcessAnomaliesEmpty = true;

    // ── Network Selection ─────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<string> _availableNetworks = new();
    [ObservableProperty] private string? _selectedNetwork;

    partial void OnSelectedNetworkChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            _ = LoadAnalyticsAsync(showLoading: true);
    }

    // ── Period Selection ─────────────────────────────────────────────────────
    [ObservableProperty] private AnalyticsPeriod _selectedPeriod = AnalyticsPeriod.Last7Days;

    // ── Connection Status ─────────────────────────────────────────────────────
    [ObservableProperty] private bool _isCurrentlyConnected = false;
    [ObservableProperty] private string _currentConnectionType = "—";
    [ObservableProperty] private string _currentInterface = "—";
    [ObservableProperty] private string _liveDownloadSpeed = "—";
    [ObservableProperty] private string _liveUploadSpeed = "—";

    // ── Summary Cards ─────────────────────────────────────────────────────────
    [ObservableProperty] private string _totalDownloadedText = "—";
    [ObservableProperty] private string _totalUploadedText   = "—";
    [ObservableProperty] private string _totalUsageText      = "—";
    [ObservableProperty] private string _connectionTimeText  = "—";
    [ObservableProperty] private string _totalSessionsText   = "—";

    // ── Connection Statistics ─────────────────────────────────────────────────
    [ObservableProperty] private string _firstConnectedText  = "—";
    [ObservableProperty] private string _lastConnectedText   = "—";

    // ── Download vs Upload Ratio ──────────────────────────────────────────────
    [ObservableProperty] private string _downloadRatioText  = "—";
    [ObservableProperty] private string _uploadRatioText    = "—";
    [ObservableProperty] private string _downloadActualText = "—";
    [ObservableProperty] private string _uploadActualText   = "—";
    [ObservableProperty] private bool _hasPeriodData        = false;
    [ObservableProperty] private GridLength _downloadColumnWidth = new GridLength(1, GridUnitType.Star);
    [ObservableProperty] private GridLength _uploadColumnWidth   = new GridLength(1, GridUnitType.Star);

    // ── Chart Constants ───────────────────────────────────────────────────────
    public const double ChartHeight = 160.0;
    private const double BarGap = 4.0;
    [ObservableProperty] private double _chartWidth = 560.0;
    [ObservableProperty] private bool _isHourlyChart = false;
    [ObservableProperty] private bool _isChartEmpty  = true;

    public ObservableCollection<DailyChartBarViewModel>  PeriodChartItems { get; } = new();
    public ObservableCollection<HourlyChartBarViewModel> HourlyChartItems { get; } = new();

    // ── Session History ───────────────────────────────────────────────────────
    public ObservableCollection<NetworkSession> SessionHistoryItems { get; } = new();
    [ObservableProperty] private bool _isSessionHistoryEmpty = true;

    // ── Network Performance (speed tests) ─────────────────────────────────────
    [ObservableProperty] private bool _hasPerformanceData = false;
    [ObservableProperty] private string _avgDownloadMbpsText  = "—";
    [ObservableProperty] private string _bestDownloadMbpsText = "—";
    [ObservableProperty] private string _avgUploadMbpsText    = "—";
    [ObservableProperty] private string _bestUploadMbpsText   = "—";
    [ObservableProperty] private string _avgPingMsText        = "—";
    [ObservableProperty] private string _bestPingMsText       = "—";
    [ObservableProperty] private string _speedTestCountText   = "—";

    // ── Network Comparison ────────────────────────────────────────────────────
    public ObservableCollection<NetworkComparisonRecord> ComparisonItems { get; } = new();
    [ObservableProperty] private bool _isComparisonEmpty = true;
    [ObservableProperty] private string _mostUsedNetworkName   = "—";
    [ObservableProperty] private string _mostUsedNetworkUsage  = "—";
    [ObservableProperty] private string _mostConnectedNetworkName = "—";
    [ObservableProperty] private string _mostConnectedNetworkTime = "—";
    [ObservableProperty] private string _bestNetworkName    = "—";
    [ObservableProperty] private string _bestNetworkAvgDl   = "—";
    [ObservableProperty] private string _bestNetworkAvgUl   = "—";
    [ObservableProperty] private string _bestNetworkAvgPing = "—";
    [ObservableProperty] private bool _hasBestPerforming    = false;

    // ── Connection Type Breakdown ─────────────────────────────────────────────
    [ObservableProperty] private string _wifiUsagePercent     = "—";
    [ObservableProperty] private string _ethernetUsagePercent = "—";
    [ObservableProperty] private string _otherUsagePercent    = "—";
    [ObservableProperty] private bool _hasTypeBreakdown       = false;

    // ── Loading ───────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isLoading        = false;
    [ObservableProperty] private bool _hasNoNetworks    = false;
    // ── Network Intelligence ─────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<NetworkProfile> _networkProfiles = new();
    [ObservableProperty] private ObservableCollection<NetworkPerformanceProfile> _networkPerformanceProfiles = new();
    [ObservableProperty] private NetworkProfile? _currentNetwork;
    [ObservableProperty] private NetworkProfile? _mostUsedNetwork;
    [ObservableProperty] private NetworkProfile? _mostConnectedNetwork;
    [ObservableProperty] private NetworkPerformanceProfile? _bestPerformingNetwork;
    [ObservableProperty] private NetworkPerformanceProfile? _mostReliableNetwork;
    [ObservableProperty] private bool _isIntelligenceLoading;
    [ObservableProperty] private bool _hasNetworkHistory;
    [ObservableProperty] private bool _hasMultipleNetworks;
    [ObservableProperty] private bool _hasIntelligencePerformanceData;
    [ObservableProperty] private bool _hasComparisonData;
    [ObservableProperty] private string _intelligenceStatusMessage = "";

    // Live monitoring: network name of the currently active connection
    private string? _activeNetworkName;

    public NetworkAnalyticsViewModel(
        IAnalyticsService analyticsService,
        INetworkMonitorWorker monitorWorker,
        INetworkIntelligenceService networkIntelligenceService,
        IProcessNetworkIntelligenceService processNetworkIntelligenceService)
    {
        _analyticsService = analyticsService ?? throw new ArgumentNullException(nameof(analyticsService));
        _monitorWorker    = monitorWorker    ?? throw new ArgumentNullException(nameof(monitorWorker));
        _networkIntelligenceService = networkIntelligenceService ?? throw new ArgumentNullException(nameof(networkIntelligenceService));
        _processNetworkIntelligenceService = processNetworkIntelligenceService ?? throw new ArgumentNullException(nameof(processNetworkIntelligenceService));

        _monitorWorker.NetworkUsageUpdated += OnNetworkUsageUpdated;
    }

    /// <summary>Called by MainWindowViewModel to pre-select a network when navigating here.</summary>
    public void Initialize(string? networkName = null)
    {
        _ = InitializeAsync(networkName);
    }

    private async Task InitializeAsync(string? preselect)
    {
        Dispatcher.UIThread.Post(() => IsLoading = true);

        var networks = (await _analyticsService.GetAvailableNetworksAsync()).ToList();

        Dispatcher.UIThread.Post(() =>
        {
            AvailableNetworks.Clear();
            foreach (var n in networks) AvailableNetworks.Add(n);
            HasNoNetworks = networks.Count == 0;

            if (!string.IsNullOrEmpty(preselect) && networks.Contains(preselect))
                SelectedNetwork = preselect;
            else if (networks.Count > 0)
                SelectedNetwork = networks[0];
            else
                IsLoading = false;
        });

        // Load comparison data (network-independent)
        await LoadComparisonAsync();
    }

    [RelayCommand]
    private void NavigateBack()
    {
        var mainVm = App.Services?.GetService(typeof(MainWindowViewModel)) as MainWindowViewModel;
        mainVm?.NavigateToDashboardCommand.Execute(null);
    }

    [RelayCommand]
    private async Task SelectPeriodAsync(string periodString)
    {
        if (Enum.TryParse<AnalyticsPeriod>(periodString, out var period) && SelectedPeriod != period)
        {
            SelectedPeriod = period;
            await LoadAnalyticsAsync(showLoading: true);
        }
    }

    public void UpdateChartWidth(double newWidth)
    {
        if (newWidth < 50) return;
        double rounded = Math.Floor(newWidth);
        if (Math.Abs(rounded - ChartWidth) < 10) return;
        ChartWidth = rounded;
        _ = LoadAnalyticsAsync(showLoading: false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Data loading
    // ─────────────────────────────────────────────────────────────────────────

    private async Task LoadAnalyticsAsync(bool showLoading)
    {
        if (string.IsNullOrEmpty(SelectedNetwork)) return;
        var network = SelectedNetwork;

        if (showLoading)
            Dispatcher.UIThread.Post(() => IsLoading = true);

        try
        {
            // Run all queries in parallel
            var summaryTask     = _analyticsService.GetNetworkSummaryAsync(network, SelectedPeriod);
            var performanceTask = _analyticsService.GetNetworkPerformanceAsync(network);

            // Chart series
            Task<IList<DailyUsageRecord>>?  dailyTask  = null;
            Task<IList<HourlyUsageRecord>>? hourlyTask = null;
            if (SelectedPeriod == AnalyticsPeriod.Today)
                hourlyTask = _analyticsService.GetNetworkTodayHourlyAsync(network);
            else
                dailyTask = _analyticsService.GetNetworkDailySeriesAsync(network, SelectedPeriod);

            // Sessions
            var sessionsTask = _analyticsService.GetNetworkSessionsAsync(network, SelectedPeriod);

            // Process-network intelligence queries
            var networkProcessUsageTask = _processNetworkIntelligenceService.GetNetworkProcessUsageAsync(network);
            var behaviorInsightsTask = _processNetworkIntelligenceService.GetNetworkSpecificBehaviorInsightsAsync();
            var anomaliesTask = _processNetworkIntelligenceService.GetProcessNetworkAnomaliesAsync();

            await Task.WhenAll(
                summaryTask,
                performanceTask,
                (Task)(dailyTask ?? (Task)Task.CompletedTask),
                (Task)(hourlyTask ?? (Task)Task.CompletedTask),
                (Task)sessionsTask,
                networkProcessUsageTask,
                behaviorInsightsTask,
                anomaliesTask
            );

            var summary     = summaryTask.Result;
            var performance = performanceTask.Result;
            var sessions    = sessionsTask.Result.ToList();
            var processUsageList = networkProcessUsageTask.Result.ToList();
            var allInsights      = behaviorInsightsTask.Result.ToList();
            var allAnomalies     = anomaliesTask.Result.ToList();

            // Build chart items off-thread
            List<DailyChartBarViewModel>?  dailyChartItems  = null;
            List<HourlyChartBarViewModel>? hourlyChartItems = null;

            if (dailyTask != null)
                dailyChartItems = BuildChartItems(dailyTask.Result.ToList(), ChartWidth);
            if (hourlyTask != null)
                hourlyChartItems = BuildHourlyChartItems(hourlyTask.Result.ToList(), ChartWidth);

            // Ratio
            double dlRatio = summary.TotalUsage > 0 ? (double)summary.TotalDownloaded / summary.TotalUsage : 0.5;
            double ulRatio = summary.TotalUsage > 0 ? (double)summary.TotalUploaded   / summary.TotalUsage : 0.5;

            Dispatcher.UIThread.Post(() =>
            {
                // Summary cards
                TotalDownloadedText = ByteFormatter.FormatBytes(summary.TotalDownloaded);
                TotalUploadedText   = ByteFormatter.FormatBytes(summary.TotalUploaded);
                TotalUsageText      = ByteFormatter.FormatBytes(summary.TotalUsage);
                ConnectionTimeText  = FormatDuration(summary.TotalConnectionTime);
                TotalSessionsText   = summary.TotalSessions.ToString();

                // Statistics
                FirstConnectedText = summary.FirstConnected?.ToString("MMM d, yyyy  HH:mm") ?? "—";
                LastConnectedText  = summary.LastConnected?.ToString("MMM d, yyyy  HH:mm") ?? "—";

                // Ratio
                HasPeriodData = summary.TotalUsage > 0;
                DownloadRatioText  = HasPeriodData ? $"{dlRatio * 100:F0}%" : "—";
                UploadRatioText    = HasPeriodData ? $"{ulRatio * 100:F0}%" : "—";
                DownloadActualText = HasPeriodData ? ByteFormatter.FormatBytes(summary.TotalDownloaded) : "—";
                UploadActualText   = HasPeriodData ? ByteFormatter.FormatBytes(summary.TotalUploaded)   : "—";
                if (HasPeriodData && summary.TotalDownloaded > 0 && summary.TotalUploaded > 0)
                {
                    DownloadColumnWidth = new GridLength(Math.Max(dlRatio, 0.05), GridUnitType.Star);
                    UploadColumnWidth   = new GridLength(Math.Max(ulRatio, 0.05), GridUnitType.Star);
                }
                else
                {
                    DownloadColumnWidth = new GridLength(1, GridUnitType.Star);
                    UploadColumnWidth   = new GridLength(1, GridUnitType.Star);
                }

                // Charts
                if (hourlyChartItems != null)
                {
                    IsHourlyChart = true;
                    HourlyChartItems.Clear();
                    foreach (var i in hourlyChartItems) HourlyChartItems.Add(i);
                    IsChartEmpty = !hourlyChartItems.Any(i => i.HasData);
                }
                else if (dailyChartItems != null)
                {
                    IsHourlyChart = false;
                    PeriodChartItems.Clear();
                    foreach (var i in dailyChartItems) PeriodChartItems.Add(i);
                    IsChartEmpty = !dailyChartItems.Any(i => i.HasData);
                }

                // Sessions
                SessionHistoryItems.Clear();
                foreach (var s in sessions) SessionHistoryItems.Add(s);
                IsSessionHistoryEmpty = SessionHistoryItems.Count == 0;

                // Performance
                if (performance != null)
                {
                    HasPerformanceData    = true;
                    AvgDownloadMbpsText   = $"{performance.AvgDownloadMbps:F1} Mbps";
                    BestDownloadMbpsText  = $"{performance.BestDownloadMbps:F1} Mbps";
                    AvgUploadMbpsText     = $"{performance.AvgUploadMbps:F1} Mbps";
                    BestUploadMbpsText    = $"{performance.BestUploadMbps:F1} Mbps";
                    AvgPingMsText         = $"{performance.AvgPingMs:F0} ms";
                    BestPingMsText        = $"{performance.BestPingMs:F0} ms";
                    SpeedTestCountText    = performance.TotalTests.ToString();
                }
                else
                {
                    HasPerformanceData = false;
                }

                // Process network usage list
                NetworkProcessUsage.Clear();
                foreach (var pu in processUsageList) NetworkProcessUsage.Add(pu);
                IsNetworkProcessUsageEmpty = NetworkProcessUsage.Count == 0;

                // Behavior Insights
                BehaviorInsights.Clear();
                foreach (var ins in allInsights.Where(x => x.NetworkName.Equals(network, StringComparison.OrdinalIgnoreCase) || x.NetworkName.Equals("All Networks", StringComparison.OrdinalIgnoreCase)))
                {
                    BehaviorInsights.Add(ins);
                }
                IsBehaviorInsightsEmpty = BehaviorInsights.Count == 0;

                // Anomalies
                ProcessAnomalies.Clear();
                foreach (var anom in allAnomalies.Where(x => x.NetworkName.Equals(network, StringComparison.OrdinalIgnoreCase)))
                {
                    ProcessAnomalies.Add(anom);
                }
                IsProcessAnomaliesEmpty = ProcessAnomalies.Count == 0;

                if (showLoading) IsLoading = false;
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NetworkAnalytics load failed: {ex.Message}");
            Dispatcher.UIThread.Post(() => { if (showLoading) IsLoading = false; });
        }
    }

    // ── Network Intelligence Loading ───────────────────────────────────────────────
    [RelayCommand]
    private async Task LoadNetworkIntelligenceAsync()
    {
        Dispatcher.UIThread.Post(() => IsIntelligenceLoading = true);
        try
        {
            var profiles = await _networkIntelligenceService.GetNetworkProfilesAsync();
            var perfProfiles = await _networkIntelligenceService.GetNetworkPerformanceProfilesAsync();
            var current = await _networkIntelligenceService.GetCurrentNetworkAsync();

            // Update collections
            NetworkProfiles.Clear();
            foreach (var p in profiles) NetworkProfiles.Add(p);
            NetworkPerformanceProfiles.Clear();
            foreach (var pp in perfProfiles) NetworkPerformanceProfiles.Add(pp);
            CurrentNetwork = current;

            // Flags
            HasNetworkHistory = profiles.Any();
            HasMultipleNetworks = profiles.Count() > 1;
            HasIntelligencePerformanceData = perfProfiles.Any(pp => pp.SpeedTestCount > 0);
            HasComparisonData = HasNetworkHistory && perfProfiles.Any();

            // Rankings
            if (profiles.Any())
            {
                MostUsedNetwork = profiles.OrderByDescending(p => p.TotalBytes).FirstOrDefault();
                MostConnectedNetwork = profiles.OrderByDescending(p => p.TotalConnectionDuration).FirstOrDefault();
            }
            if (perfProfiles.Any())
            {
                // Best performing using weighted score
                double maxDl = perfProfiles.Max(p => p.AverageDownloadSpeed);
                double maxUl = perfProfiles.Max(p => p.AverageUploadSpeed);
                double maxLat = perfProfiles.Max(p => p.AverageLatency);
                double maxRel = perfProfiles.Max(p => p.ReliabilityScore);
                foreach (var pp in perfProfiles)
                {
                    double dlNorm = maxDl > 0 ? pp.AverageDownloadSpeed / maxDl : 0;
                    double ulNorm = maxUl > 0 ? pp.AverageUploadSpeed / maxUl : 0;
                    double latNorm = maxLat > 0 ? (maxLat - pp.AverageLatency) / maxLat : 0; // lower is better
                    double relNorm = maxRel > 0 ? pp.ReliabilityScore / maxRel : 0;
                    double score = dlNorm * 0.40 + ulNorm * 0.25 + latNorm * 0.20 + relNorm * 0.15;
                    // Attach a temporary property via a dictionary? For simplicity store in a variable dictionary.
                    pp.GetType().GetProperty("PerformanceScore")?.SetValue(pp, score);
                }
                var bestPerf = perfProfiles.OrderByDescending(p =>
                    ((double?)p.GetType().GetProperty("PerformanceScore")?.GetValue(p) ?? 0)).FirstOrDefault();
                BestPerformingNetwork = bestPerf;
                MostReliableNetwork = perfProfiles.OrderByDescending(p => p.ReliabilityScore).FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            IntelligenceStatusMessage = $"Error loading network intelligence: {ex.Message}";
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsIntelligenceLoading = false);
        }
    }

    private async Task LoadComparisonAsync()
    {
        try
        {
            var comparison = (await _analyticsService.GetNetworkComparisonAsync()).ToList();

            // Type breakdown by total usage
            long wifiTotal     = comparison.Where(c => c.ConnectionType.Equals("wifi",     StringComparison.OrdinalIgnoreCase)).Sum(c => c.TotalUsage);
            long ethernetTotal = comparison.Where(c => c.ConnectionType.Equals("ethernet", StringComparison.OrdinalIgnoreCase)).Sum(c => c.TotalUsage);
            long grandTotal    = comparison.Sum(c => c.TotalUsage);
            long otherTotal    = grandTotal - wifiTotal - ethernetTotal;

            // Best performing (by avg download, at least 1 test)
            var bestPerforming = comparison.Where(c => c.AvgDownloadMbps > 0).OrderByDescending(c => c.AvgDownloadMbps).FirstOrDefault();

            // Most used (by data)
            var mostUsed      = comparison.FirstOrDefault();
            // Most connected (by duration)
            var mostConnected = comparison.OrderByDescending(c => c.TotalConnectionTime).FirstOrDefault();

            Dispatcher.UIThread.Post(() =>
            {
                ComparisonItems.Clear();
                foreach (var c in comparison) ComparisonItems.Add(c);
                IsComparisonEmpty = ComparisonItems.Count == 0;

                // Most used
                if (mostUsed != null)
                {
                    MostUsedNetworkName  = mostUsed.NetworkName;
                    MostUsedNetworkUsage = ByteFormatter.FormatBytes(mostUsed.TotalUsage);
                }

                // Most connected
                if (mostConnected != null)
                {
                    MostConnectedNetworkName = mostConnected.NetworkName;
                    MostConnectedNetworkTime = FormatDuration(mostConnected.TotalConnectionTime);
                }

                // Best performing
                if (bestPerforming != null)
                {
                    HasBestPerforming    = true;
                    BestNetworkName      = bestPerforming.NetworkName;
                    BestNetworkAvgDl     = $"{bestPerforming.AvgDownloadMbps:F1} Mbps";
                    BestNetworkAvgUl     = $"{bestPerforming.AvgUploadMbps:F1} Mbps";
                    // Avg ping not stored in comparison, show note
                    BestNetworkAvgPing   = "—";
                }
                else
                {
                    HasBestPerforming = false;
                }

                // Type breakdown
                if (grandTotal > 0)
                {
                    HasTypeBreakdown       = true;
                    WifiUsagePercent       = $"{(double)wifiTotal     / grandTotal * 100:F0}%";
                    EthernetUsagePercent   = $"{(double)ethernetTotal / grandTotal * 100:F0}%";
                    OtherUsagePercent      = $"{(double)otherTotal    / grandTotal * 100:F0}%";
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Comparison load failed: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Live monitoring
    // ─────────────────────────────────────────────────────────────────────────

    private void OnNetworkUsageUpdated(NetworkUsage usage)
    {
        // Store the currently active network for live status
        // We derive the network name by checking the existing session if the interface matches
        // For simplicity we rely on the DashboardViewModel's current network card data via the usage object
        // The usage object itself doesn't carry a network name — we just check if the active interface is up
        // and show the live speeds if we're on the selected network (based on the currently connected flag set at initialisation)

        // The ViewModel checks: if the selected network matches the currently live interface
        Dispatcher.UIThread.Post(() =>
        {
            if (string.IsNullOrEmpty(usage.InterfaceName) || usage.InterfaceName == "None")
            {
                IsCurrentlyConnected = false;
                return;
            }

            // We'll update live speeds only when the user has the active network selected
            if (IsCurrentlyConnected)
            {
                LiveDownloadSpeed = ByteFormatter.FormatSpeed(usage.DownloadSpeed);
                LiveUploadSpeed   = ByteFormatter.FormatSpeed(usage.UploadSpeed);
            }
        });
    }

    /// <summary>Called externally (e.g., from NetworkSessionManager observer or DashboardViewModel) when connection state changes.</summary>
    public void UpdateActiveNetwork(string networkName, string connectionType, string interfaceName)
    {
        _activeNetworkName = networkName;
        Dispatcher.UIThread.Post(() =>
        {
            IsCurrentlyConnected = !string.IsNullOrEmpty(SelectedNetwork) &&
                                   SelectedNetwork.Equals(networkName, StringComparison.OrdinalIgnoreCase);
            CurrentConnectionType = connectionType;
            CurrentInterface      = interfaceName;
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Chart builders (identical pattern to ApplicationAnalyticsViewModel)
    // ─────────────────────────────────────────────────────────────────────────

    private static List<DailyChartBarViewModel> BuildChartItems(List<DailyUsageRecord> daily, double chartWidth)
    {
        if (daily.Count == 0) return new();
        long maxTotal = daily.Max(d => d.TotalBytes);
        if (maxTotal <= 0) maxTotal = 1;
        int count = daily.Count;
        double barWidth = (chartWidth - (count - 1) * BarGap) / Math.Max(count, 1);
        var items = new List<DailyChartBarViewModel>(count);

        for (int i = 0; i < count; i++)
        {
            var d = daily[i];
            double totalBarHeight = (double)d.TotalBytes / maxTotal * ChartHeight;
            double dlFrac = d.TotalBytes > 0 ? (double)d.BytesDownloaded / d.TotalBytes : 0.5;
            double ulFrac = 1.0 - dlFrac;
            double dlH = totalBarHeight * dlFrac;
            double ulH = totalBarHeight * ulFrac;
            double dlY = ChartHeight - dlH;
            double ulY = dlY - ulH;
            items.Add(new DailyChartBarViewModel
            {
                DayLabel        = d.Day.ToString("MMM d"),
                BytesDownloaded = d.BytesDownloaded,
                BytesUploaded   = d.BytesUploaded,
                TotalBytes      = d.TotalBytes,
                DownloadedText  = ByteFormatter.FormatBytes(d.BytesDownloaded),
                UploadedText    = ByteFormatter.FormatBytes(d.BytesUploaded),
                TotalText       = ByteFormatter.FormatBytes(d.TotalBytes),
                BarX            = i * (barWidth + BarGap),
                BarWidth        = Math.Max(barWidth, 1),
                DownloadBarHeight = Math.Max(dlH, 0),
                UploadBarHeight   = Math.Max(ulH, 0),
                DownloadBarY    = dlY,
                UploadBarY      = ulY,
                LabelY          = ChartHeight + 4,
            });
        }
        return items;
    }

    private static List<HourlyChartBarViewModel> BuildHourlyChartItems(List<HourlyUsageRecord> hourly, double chartWidth)
    {
        if (hourly.Count == 0) return new();
        long maxTotal = hourly.Max(h => h.TotalBytes);
        if (maxTotal <= 0) maxTotal = 1;
        int count = hourly.Count;
        double barWidth = (chartWidth - (count - 1) * BarGap) / Math.Max(count, 1);
        var items = new List<HourlyChartBarViewModel>(count);

        for (int i = 0; i < count; i++)
        {
            var h = hourly[i];
            double totalBarHeight = (double)h.TotalBytes / maxTotal * ChartHeight;
            double dlFrac = h.TotalBytes > 0 ? (double)h.BytesDownloaded / h.TotalBytes : 0.5;
            double ulFrac = 1.0 - dlFrac;
            double dlH = totalBarHeight * dlFrac;
            double ulH = totalBarHeight * ulFrac;
            double dlY = ChartHeight - dlH;
            double ulY = dlY - ulH;
            items.Add(new HourlyChartBarViewModel
            {
                Hour            = h.Hour,
                BytesDownloaded = h.BytesDownloaded,
                BytesUploaded   = h.BytesUploaded,
                TotalBytes      = h.TotalBytes,
                DownloadedText  = ByteFormatter.FormatBytes(h.BytesDownloaded),
                UploadedText    = ByteFormatter.FormatBytes(h.BytesUploaded),
                TotalText       = ByteFormatter.FormatBytes(h.TotalBytes),
                BarX            = i * (barWidth + BarGap),
                BarWidth        = Math.Max(barWidth, 1),
                DownloadBarHeight = Math.Max(dlH, 0),
                UploadBarHeight   = Math.Max(ulH, 0),
                DownloadBarY    = dlY,
                UploadBarY      = ulY,
                LabelY          = ChartHeight + 4,
            });
        }
        return items;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalSeconds < 1) return "—";
        if (ts.TotalHours < 1)  return $"{ts.Minutes}m";
        if (ts.TotalDays < 1)   return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
    }
}
