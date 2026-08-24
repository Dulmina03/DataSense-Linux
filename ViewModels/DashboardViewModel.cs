using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataSense.Database;
using DataSense.Helpers;
using DataSense.Models;
using DataSense.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DataSense.ViewModels;

public partial class DashboardViewModel : ViewModelBase, IDisposable
{
    private readonly INetworkMonitorWorker     _networkMonitorWorker;
    private readonly INetworkUsageRepository   _repository;
    private readonly INetworkConnectionService _connectionService;
    private readonly IAnalyticsService         _analyticsService;
    private readonly ProcessNetworkMonitorWorker _processMonitorWorker;
    private readonly IIntelligenceService      _intelligenceService;
    private readonly IForecastService          _forecastService;
    private readonly IPatternAnalysisService   _patternAnalysisService;
    private readonly IApplicationIntelligenceService _appIntelligenceService;
    private readonly IUnifiedIntelligenceService     _unifiedIntelligenceService;
    private readonly IApplicationAnalyticsService    _applicationAnalyticsService;
    private readonly NetworkSessionManager     _sessionManager;
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
    [ObservableProperty] private string _statusDotColor      = "Muted"; // grey until connected
    [ObservableProperty] private string _topActiveProcessText = "None";

    [RelayCommand]
    private void OpenLiveMonitor()
    {
        if (App.Services != null)
        {
            var mainVm = App.Services.GetRequiredService<MainWindowViewModel>();
            mainVm.NavigateToLiveMonitoringCommand.Execute(null);
        }
    }

    // ── Current session properties ──────────────────────────────────────────

    [ObservableProperty] private string _currentSessionNetwork = "No active session";
    [ObservableProperty] private string _currentSessionDuration = "—";
    [ObservableProperty] private string _currentSessionDownload = "—";
    [ObservableProperty] private string _currentSessionUpload = "—";
    [ObservableProperty] private string _currentSessionTotal = "—";
    [ObservableProperty] private bool _hasCurrentSession = false;

    [RelayCommand]
    private void OpenTimeline()
    {
        if (App.Services != null)
        {
            var mainVm = App.Services.GetRequiredService<MainWindowViewModel>();
            mainVm.NavigateToTimelineCommand.Execute(null);
        }
    }

    // ── Today summary properties ────────────────────────────────────────────

    [ObservableProperty] private string _todayDownloadedText   = "—";
    [ObservableProperty] private string _todayUploadedText     = "—";
    [ObservableProperty] private string _todayTotalText        = "—";
    [ObservableProperty] private string _todayVsYesterdayText  = "—";   // e.g. "+12%" / "-5%"
    [ObservableProperty] private string _todayDeltaColor       = "Muted"; // green / red / neutral
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

    // ── Insights ────────────────────────────────────────────────────────────

    public ObservableCollection<NetworkInsight> Insights { get; } = new();
    
    [ObservableProperty] private bool _hasInsights;

    // ── Chart ───────────────────────────────────────────────────────────────

    public ObservableCollection<DailyChartBarViewModel> DailyChartItems { get; } = new();

    // ── Real-Time Network Traffic Chart ──────────────────────────────────────

    public ObservableCollection<LiveThroughputSample> LiveThroughputSamples { get; } = new();
    public ObservableCollection<RealtimeNetworkPoint> RealtimeTrafficPoints { get; } = new();
    public ObservableCollection<string> TimeAxisLabels { get; } = new();

    [ObservableProperty] private Geometry? _realtimeDownloadAreaGeometry;
    [ObservableProperty] private Geometry? _realtimeDownloadLineGeometry;
    [ObservableProperty] private Geometry? _realtimeUploadAreaGeometry;
    [ObservableProperty] private Geometry? _realtimeUploadLineGeometry;

    [ObservableProperty] private Geometry? _timelineTrendLineGeometry;
    [ObservableProperty] private Geometry? _timelineTrendGlowGeometry;
    [ObservableProperty] private double _latestPointX = 0;
    [ObservableProperty] private double _latestPointY = 170;
    [ObservableProperty] private bool _hasLatestPoint = false;

    [ObservableProperty] private DailyChartBarViewModel? _hoveredBar;
    [ObservableProperty] private bool _isHoveringTimeline = false;
    [ObservableProperty] private double _hoverBarX = 0;
    [ObservableProperty] private double _hoverBarWidth = 0;
    [ObservableProperty] private double _hoverPointY = 170;

    [ObservableProperty] private string _liveGraphStatusText = "LIVE";
    [ObservableProperty] private string _liveGraphStatusColor = "Success";
    [ObservableProperty] private bool   _isRealtimeGraphLive = true;
    [ObservableProperty] private bool   _hasRealtimeGraphData = false;

    [ObservableProperty] private string _currentLiveDownloadSpeedText = "0.0 B/s";
    [ObservableProperty] private string _currentLiveUploadSpeedText = "0.0 B/s";
    [ObservableProperty] private string _peakLiveDownloadSpeedText = "0.0 B/s";
    [ObservableProperty] private string _peakLiveUploadSpeedText = "0.0 B/s";
    [ObservableProperty] private double _peakDownloadRateInWindow = 0;
    [ObservableProperty] private double _peakUploadRateInWindow = 0;

    [ObservableProperty] private string _yAxisTopText = "100.0 KB/s";
    [ObservableProperty] private string _yAxisMidHighText = "75.0 KB/s";
    [ObservableProperty] private string _yAxisMidText = "50.0 KB/s";
    [ObservableProperty] private string _yAxisMidLowText = "25.0 KB/s";
    [ObservableProperty] private string _yAxisMinText = "0 B/s";

    [ObservableProperty] private LiveThroughputSample? _hoveredThroughputSample;
    [ObservableProperty] private RealtimeNetworkPoint? _hoveredRealtimePoint;
    [ObservableProperty] private bool   _isHoveringRealtimeGraph = false;
    [ObservableProperty] private double _hoverLineX = 0;
    [ObservableProperty] private double _hoverDownloadY = 180;
    [ObservableProperty] private double _hoverUploadY = 180;
    [ObservableProperty] private double _hoverTooltipX = 0;

    [ObservableProperty] private double _latestDownloadX = 0;
    [ObservableProperty] private double _latestDownloadY = 180;
    [ObservableProperty] private double _latestUploadX = 0;
    [ObservableProperty] private double _latestUploadY = 180;

    private const int MaxRealtimePoints = 60;
    private string _lastInterfaceName = string.Empty;
    private readonly List<RealtimeNetworkPoint> _historicalGraphPoints = new();

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
    [ObservableProperty] private bool _isPeriodToday = false;
    [ObservableProperty] private bool _isPeriod7Days = true;
    [ObservableProperty] private bool _isPeriod30Days = false;
    
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
    public ObservableCollection<ApplicationHistoricalProfile> TopProcesses { get; } = new();

    [ObservableProperty] private bool _hasLiveProcessTraffic = false;
    [ObservableProperty] private string _downloadHeavyProcessText = "—";
    [ObservableProperty] private string _uploadHeavyProcessText = "—";
    [ObservableProperty] private string _topProcessesBaselineText = "Collecting process usage baseline...";
    [ObservableProperty] private bool _hasTopProcesses = false;
    [ObservableProperty] private string _topAppInsightText = "Analyzing application behavior...";

    // ── Forecast & Budget ────────────────────────────────────────────────────

    public ObservableCollection<ForecastChartPointViewModel> ForecastChartItems { get; } = new();

    [ObservableProperty] private bool   _hasForecast          = false;
    [ObservableProperty] private bool   _hasBudget            = false;
    [ObservableProperty] private string _forecastCurrentText  = "—";
    [ObservableProperty] private string _forecastProjectedText = "—";
    [ObservableProperty] private string _forecastRangeText    = "—";
    [ObservableProperty] private string _forecastAvgDailyText = "—";
    [ObservableProperty] private string _forecastConfidenceText = "—";
    [ObservableProperty] private string _forecastInsufficientText = "Not enough historical data yet. Continue using DataSense to build a forecast baseline.";
    [ObservableProperty] private bool   _isForecastLoading    = false;

    // Budget summary
    [ObservableProperty] private string _budgetUsedText       = "—";
    [ObservableProperty] private string _budgetLimitText      = "—";
    [ObservableProperty] private string _budgetRemainingText  = "—";
    [ObservableProperty] private string _budgetUsedPctText    = "—";
    [ObservableProperty] private string _budgetStatusText     = "—";
    [ObservableProperty] private string _budgetStatusColor    = "Muted";
    [ObservableProperty] private double _budgetProgressValue  = 0;
    [ObservableProperty] private string _budgetExhaustionText = "—";
    [ObservableProperty] private string _budgetPaceText       = "—";
    [ObservableProperty] private bool   _hasDailyBudget       = false;
    [ObservableProperty] private string _dailyBudgetUsedText  = "—";
    [ObservableProperty] private string _dailyBudgetLimitText = "—";
    [ObservableProperty] private string _dailyBudgetStatusText = "—";
    [ObservableProperty] private string _dailyBudgetStatusColor = "Muted";

    // ── Usage Patterns & Anomaly Detection ──────────────────────────────────

    public ObservableCollection<UsageAnomaly> DetectedAnomalies { get; } = new();

    [ObservableProperty] private bool   _hasAnomalies            = false;
    [ObservableProperty] private string _busyHoursText           = "Calculating usage baseline...";
    [ObservableProperty] private string _busyDaysText            = "Calculating usage baseline...";
    [ObservableProperty] private bool   _hasSufficientPatternData = false;

    // ── Application Intelligence & Recommendations ──────────────────────────

    public ObservableCollection<ApplicationUsageProfile> TopAppProfiles { get; } = new();
    public ObservableCollection<ApplicationRecommendation> AppRecommendations { get; } = new();

    [ObservableProperty] private bool _hasAppRecommendations = false;

    // ── Unified Intelligence & System Health Observatory ────────────────────

    public ObservableCollection<IntelligenceEvent> UnifiedEvents { get; } = new();
    [ObservableProperty] private DataSenseHealthModel? _systemHealth;
    [ObservableProperty] private bool _hasUnifiedEvents = false;
    
    [ObservableProperty] private UnifiedInsight? _primaryUnifiedInsight;
    [ObservableProperty] private bool _hasUnifiedInsights;
    private readonly IUnifiedAnalyticsIntelligenceService _unifiedAnalyticsIntelligenceService;

    // ── Chart layout constants ──────────────────────────────────────────────

    /// <summary>Fixed canvas height for the bar chart area in device-independent pixels.</summary>
    public const double ChartHeight = 160.0;

    /// <summary>Gap in pixels between adjacent bars.</summary>
    private const double BarGap = 4.0;

    /// <summary>Number of days shown in the chart.</summary>
    private const int ChartDays = 14;

    // ── Title ───────────────────────────────────────────────────────────────

    public override string Title => "Dashboard";

    // ── Charting ────────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isTodayChartLoading = true;
    [ObservableProperty] private bool _hasTodayChartData = false;

    // ────────────────────────────────────────────────────────────────────────
    // Construction
    // ────────────────────────────────────────────────────────────────────────

    private readonly IChartDataService _chartDataService;
    private readonly IAppIconService _appIconService;

    public DashboardViewModel(
        INetworkMonitorWorker    networkMonitorWorker,
        INetworkUsageRepository  repository,
        INetworkConnectionService connectionService,
        IAnalyticsService         analyticsService,
        ProcessNetworkMonitorWorker processMonitorWorker,
        IIntelligenceService      intelligenceService,
        IForecastService          forecastService,
        IPatternAnalysisService   patternAnalysisService,
        IApplicationIntelligenceService appIntelligenceService,
        IUnifiedIntelligenceService     unifiedIntelligenceService,
        IApplicationAnalyticsService    applicationAnalyticsService,
        IUnifiedAnalyticsIntelligenceService unifiedAnalyticsIntelligenceService,
        NetworkSessionManager           sessionManager,
        IChartDataService               chartDataService,
        IAppIconService?                appIconService = null)
    {
        _networkMonitorWorker   = networkMonitorWorker   ?? throw new ArgumentNullException(nameof(networkMonitorWorker));
        _repository             = repository             ?? throw new ArgumentNullException(nameof(repository));
        _connectionService      = connectionService      ?? throw new ArgumentNullException(nameof(connectionService));
        _analyticsService       = analyticsService       ?? throw new ArgumentNullException(nameof(analyticsService));
        _processMonitorWorker   = processMonitorWorker   ?? throw new ArgumentNullException(nameof(processMonitorWorker));
        _intelligenceService    = intelligenceService    ?? throw new ArgumentNullException(nameof(intelligenceService));
        _forecastService        = forecastService        ?? throw new ArgumentNullException(nameof(forecastService));
        _patternAnalysisService = patternAnalysisService ?? throw new ArgumentNullException(nameof(patternAnalysisService));
        _appIntelligenceService = appIntelligenceService ?? throw new ArgumentNullException(nameof(appIntelligenceService));
        _unifiedIntelligenceService = unifiedIntelligenceService ?? throw new ArgumentNullException(nameof(unifiedIntelligenceService));
        _applicationAnalyticsService = applicationAnalyticsService ?? throw new ArgumentNullException(nameof(applicationAnalyticsService));
        _unifiedAnalyticsIntelligenceService = unifiedAnalyticsIntelligenceService ?? throw new ArgumentNullException(nameof(unifiedAnalyticsIntelligenceService));
        _sessionManager         = sessionManager         ?? throw new ArgumentNullException(nameof(sessionManager));
        _chartDataService       = chartDataService       ?? throw new ArgumentNullException(nameof(chartDataService));
        _appIconService         = appIconService         ?? new LinuxApplicationIconService();

        // Initialise relative timeline axis labels
        TimeAxisLabels.Add("-60s");
        TimeAxisLabels.Add("-45s");
        TimeAxisLabels.Add("-30s");
        TimeAxisLabels.Add("-15s");
        TimeAxisLabels.Add("NOW");

        // Populate live card with current worker state immediately
        UpdateLiveValues(
            _networkMonitorWorker.ActiveInterface,
            _networkMonitorWorker.DownloadSpeed,
            _networkMonitorWorker.UploadSpeed,
            _networkMonitorWorker.TotalBytesDownloaded,
            _networkMonitorWorker.TotalBytesUploaded);

        AddRealtimeTrafficSample(
            _networkMonitorWorker.DownloadSpeed,
            _networkMonitorWorker.UploadSpeed,
            _networkMonitorWorker.ActiveInterface);

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
        AnalyticsPeriod newPeriod = SelectedPeriod;
        if (periodString.Equals("Today", StringComparison.OrdinalIgnoreCase))
            newPeriod = AnalyticsPeriod.Today;
        else if (periodString.Equals("7Days", StringComparison.OrdinalIgnoreCase) || periodString.Equals("Last7Days", StringComparison.OrdinalIgnoreCase))
            newPeriod = AnalyticsPeriod.Last7Days;
        else if (periodString.Equals("30Days", StringComparison.OrdinalIgnoreCase) || periodString.Equals("Last30Days", StringComparison.OrdinalIgnoreCase))
            newPeriod = AnalyticsPeriod.Last30Days;
        else if (Enum.TryParse<AnalyticsPeriod>(periodString, out var parsed))
            newPeriod = parsed;

        if (SelectedPeriod != newPeriod)
        {
            SelectedPeriod = newPeriod;
            IsPeriodToday = SelectedPeriod == AnalyticsPeriod.Today;
            IsPeriod7Days = SelectedPeriod == AnalyticsPeriod.Last7Days;
            IsPeriod30Days = SelectedPeriod == AnalyticsPeriod.Last30Days;
            await LoadPeriodAnalyticsAsync(showLoading: true);
        }
    }

    [RelayCommand]
    private void NavigateToProcessAnalytics(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return;
        
        var mainWindowVm = App.Services?.GetService(typeof(MainWindowViewModel)) as MainWindowViewModel;
        mainWindowVm?.NavigateToApplicationAnalytics(processName);
    }

    [RelayCommand]
    private void NavigateToNetworkAnalytics(string? networkName = null)
    {
        var mainWindowVm = App.Services?.GetService(typeof(MainWindowViewModel)) as MainWindowViewModel;
        mainWindowVm?.NavigateToNetworkAnalytics(networkName);
    }

    [RelayCommand]
    private void NavigateToUnifiedIntelligence()
    {
        var mainWindowVm = App.Services?.GetService(typeof(MainWindowViewModel)) as MainWindowViewModel;
        mainWindowVm?.NavigateToUnifiedIntelligence();
    }

    [RelayCommand]
    private void InsightTapped(NetworkInsight insight)
    {
        if (insight == null) return;
        if (!string.IsNullOrEmpty(insight.ApplicationName))
        {
            NavigateToProcessAnalytics(insight.ApplicationName);
        }
        else if (!string.IsNullOrEmpty(insight.NetworkName))
        {
            NavigateToNetworkAnalytics(insight.NetworkName);
        }
    }

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
        RebuildRealtimeChartGeometry();
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
        // Aggregate socket/PID-level measurements into process-level entries
        var active = currentBatch
            .GroupBy(p => p.ProcessIdentifier.Trim().ToLowerInvariant())
            .Select(g =>
            {
                var first = g.First();
                return new ProcessNetworkUsage
                {
                    ProcessIdentifier = first.ProcessIdentifier,
                    ExecutablePath = first.ExecutablePath,
                    Pid = first.Pid,
                    User = first.User,
                    DownloadRateBytesPerSec = g.Sum(x => x.DownloadRateBytesPerSec),
                    UploadRateBytesPerSec = g.Sum(x => x.UploadRateBytesPerSec),
                    DownloadBytes = g.Sum(x => x.DownloadBytes),
                    UploadBytes = g.Sum(x => x.UploadBytes),
                    Timestamp = g.Max(x => x.Timestamp),
                    DataSource = first.DataSource,
                    ProcessIdentityKey = first.ProcessIdentityKey,
                    ApplicationDisplayName = _appIconService.GetApplicationDisplayName(first.ProcessIdentifier, first.ExecutablePath),
                    ApplicationIcon = _appIconService.GetApplicationIcon(first.ProcessIdentifier, first.ExecutablePath)
                };
            })
            .Where(p => p.DownloadRateBytesPerSec > 0 || p.UploadRateBytesPerSec > 0 || p.DownloadBytes > 0 || p.UploadBytes > 0)
            .OrderByDescending(p => (p.DownloadRateBytesPerSec + p.UploadRateBytesPerSec) > 0 
                ? (p.DownloadRateBytesPerSec + p.UploadRateBytesPerSec) 
                : (double)(p.DownloadBytes + p.UploadBytes))
            .Take(10)
            .ToList();

        Dispatcher.UIThread.Post(() =>
        {
            LiveProcessTraffic.Clear();
            foreach (var process in active)
            {
                LiveProcessTraffic.Add(process);
            }
            HasLiveProcessTraffic = LiveProcessTraffic.Count > 0;

            if ((!HasTopProcesses || TopProcesses.Count == 0) && LiveProcessTraffic.Count > 0)
            {
                var liveGrouped = LiveProcessTraffic
                    .GroupBy(p => p.ProcessIdentifier)
                    .Select(g =>
                    {
                        var first = g.First();
                        return new ApplicationHistoricalProfile
                        {
                            ProcessName = g.Key,
                            DownloadBytes = g.Sum(x => x.DownloadBytes),
                            UploadBytes = g.Sum(x => x.UploadBytes),
                            TodayBytes = g.Sum(x => x.DownloadBytes + x.UploadBytes),
                            DataSource = "Live Telemetry",
                            ApplicationDisplayName = _appIconService.GetApplicationDisplayName(first.ProcessIdentifier, first.ExecutablePath),
                            ApplicationIcon = _appIconService.GetApplicationIcon(first.ProcessIdentifier, first.ExecutablePath)
                        };
                    })
                    .OrderByDescending(p => p.TotalBytes)
                    .Take(5)
                    .ToList();

                long totalBytes = liveGrouped.Sum(p => p.TotalBytes);
                if (totalBytes > 0)
                {
                    foreach (var p in liveGrouped)
                    {
                        p.PercentageOfTotal = (double)p.TotalBytes / totalBytes * 100.0;
                    }
                }

                TopProcesses.Clear();
                foreach (var p in liveGrouped)
                {
                    TopProcesses.Add(p);
                }
                HasTopProcesses = TopProcesses.Count > 0;
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

            AddRealtimeTrafficSample(
                usage.DownloadSpeed,
                usage.UploadSpeed,
                usage.InterfaceName);
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

    public void AddRealtimeTrafficSample(double downloadSpeed, double uploadSpeed, string? interfaceName)
    {
        bool isConnected = !string.IsNullOrEmpty(interfaceName) && interfaceName != "None" && interfaceName != "Disconnected";

        if (!isConnected)
        {
            IsRealtimeGraphLive = false;
            LiveGraphStatusText = "OFFLINE";
            LiveGraphStatusColor = "Muted";
            CurrentLiveDownloadSpeedText = "0.0 B/s";
            CurrentLiveUploadSpeedText = "0.0 B/s";
            return;
        }

        IsRealtimeGraphLive = true;
        LiveGraphStatusText = "LIVE";
        LiveGraphStatusColor = "Success";

        // If network interface switched, reset history window to avoid huge false rate spike
        if (!string.IsNullOrEmpty(_lastInterfaceName) && _lastInterfaceName != interfaceName)
        {
            LiveThroughputSamples.Clear();
            RealtimeTrafficPoints.Clear();
        }
        _lastInterfaceName = interfaceName ?? string.Empty;

        var sample = new LiveThroughputSample
        {
            Timestamp = DateTime.UtcNow,
            DownloadBytesPerSecond = downloadSpeed,
            UploadBytesPerSecond = uploadSpeed
        };

        LiveThroughputSamples.Add(sample);
        while (LiveThroughputSamples.Count > MaxRealtimePoints)
        {
            LiveThroughputSamples.RemoveAt(0);
        }

        // Keep RealtimeTrafficPoints populated for backwards compatibility
        var legacyPoint = new RealtimeNetworkPoint
        {
            Timestamp = sample.Timestamp,
            DownloadRateBytesPerSec = downloadSpeed,
            UploadRateBytesPerSec = uploadSpeed
        };
        RealtimeTrafficPoints.Add(legacyPoint);
        while (RealtimeTrafficPoints.Count > MaxRealtimePoints)
        {
            RealtimeTrafficPoints.RemoveAt(0);
        }

        CurrentLiveDownloadSpeedText = ByteFormatter.FormatSpeed(downloadSpeed);
        CurrentLiveUploadSpeedText = ByteFormatter.FormatSpeed(uploadSpeed);

        PeakDownloadRateInWindow = LiveThroughputSamples.Max(p => p.DownloadBytesPerSecond);
        PeakUploadRateInWindow = LiveThroughputSamples.Max(p => p.UploadBytesPerSecond);

        PeakLiveDownloadSpeedText = ByteFormatter.FormatSpeed(PeakDownloadRateInWindow);
        PeakLiveUploadSpeedText = ByteFormatter.FormatSpeed(PeakUploadRateInWindow);

        HasRealtimeGraphData = LiveThroughputSamples.Count > 0;

        RebuildRealtimeChartGeometry();
    }

    public void RebuildRealtimeChartGeometry()
    {
        double canvasWidth = Math.Max(300.0, ChartWidth - 78.0);
        double usableHeight = 160.0;
        double yBase = 170.0;

        int count = LiveThroughputSamples.Count;
        if (count == 0)
        {
            RealtimeDownloadAreaGeometry = null;
            RealtimeDownloadLineGeometry = null;
            RealtimeUploadAreaGeometry = null;
            RealtimeUploadLineGeometry = null;
            HasRealtimeGraphData = false;
            return;
        }

        HasRealtimeGraphData = true;
        double maxObserved = Math.Max(PeakDownloadRateInWindow, PeakUploadRateInWindow);
        double yMax = Math.Max(102400.0, maxObserved * 1.20); // 100 KB/s minimum scale floor, 20% headroom

        YAxisTopText = ByteFormatter.FormatSpeed(yMax);
        YAxisMidHighText = ByteFormatter.FormatSpeed(yMax * 0.75);
        YAxisMidText = ByteFormatter.FormatSpeed(yMax * 0.50);
        YAxisMidLowText = ByteFormatter.FormatSpeed(yMax * 0.25);
        YAxisMinText = "0 B/s";

        var downloadPoints = new List<Point>(count);
        var uploadPoints = new List<Point>(count);

        for (int i = 0; i < count; i++)
        {
            var p = LiveThroughputSamples[i];
            p.SecondsAgo = count - 1 - i;

            double x = count == 1 ? canvasWidth : (double)i / (count - 1) * canvasWidth;

            double dlRatio = Math.Clamp(p.DownloadBytesPerSecond / yMax, 0.0, 1.0);
            double ulRatio = Math.Clamp(p.UploadBytesPerSecond / yMax, 0.0, 1.0);

            double dlY = yBase - (dlRatio * usableHeight);
            double ulY = yBase - (ulRatio * usableHeight);

            p.CanvasX = x;
            p.DownloadY = dlY;
            p.UploadY = ulY;

            downloadPoints.Add(new Point(x, dlY));
            uploadPoints.Add(new Point(x, ulY));
        }

        var (dlLine, dlArea) = BuildCurveGeometry(downloadPoints, yBase, canvasWidth);
        var (ulLine, ulArea) = BuildCurveGeometry(uploadPoints, yBase, canvasWidth);

        RealtimeDownloadLineGeometry = dlLine;
        RealtimeDownloadAreaGeometry = dlArea;
        RealtimeUploadLineGeometry = ulLine;
        RealtimeUploadAreaGeometry = ulArea;

        if (downloadPoints.Count > 0)
        {
            LatestDownloadX = downloadPoints.Last().X;
            LatestDownloadY = downloadPoints.Last().Y;
            LatestUploadX = uploadPoints.Last().X;
            LatestUploadY = uploadPoints.Last().Y;
        }

        TimeAxisLabels.Clear();
        TimeAxisLabels.Add("-60s");
        TimeAxisLabels.Add("-45s");
        TimeAxisLabels.Add("-30s");
        TimeAxisLabels.Add("-15s");
        TimeAxisLabels.Add("NOW");
    }

    private static (Geometry Line, Geometry Area) BuildCurveGeometry(List<Point> points, double yBase, double canvasWidth)
    {
        if (points.Count == 0)
        {
            return (Geometry.Parse($"M 0,{yBase} L 100,{yBase}"), Geometry.Parse($"M 0,{yBase} L {canvasWidth},{yBase} Z"));
        }

        var lineGeometry = new PathGeometry();
        var lineFigure = new PathFigure
        {
            StartPoint = points[0],
            IsClosed = false,
            IsFilled = false
        };

        var areaGeometry = new PathGeometry();
        var areaFigure = new PathFigure
        {
            StartPoint = new Point(points[0].X, yBase),
            IsClosed = true,
            IsFilled = true
        };
        areaFigure.Segments!.Add(new LineSegment { Point = points[0] });

        if (points.Count == 1)
        {
            lineFigure.Segments!.Add(new LineSegment { Point = new Point(canvasWidth, points[0].Y) });
            areaFigure.Segments!.Add(new LineSegment { Point = new Point(canvasWidth, points[0].Y) });
            areaFigure.Segments.Add(new LineSegment { Point = new Point(canvasWidth, yBase) });
        }
        else
        {
            for (int i = 0; i < points.Count - 1; i++)
            {
                var p0 = points[i];
                var p1 = points[i + 1];
                double midX = (p0.X + p1.X) / 2.0;

                var segment = new QuadraticBezierSegment
                {
                    Point1 = new Point(midX, p0.Y),
                    Point2 = p1
                };
                lineFigure.Segments!.Add(segment);
                areaFigure.Segments!.Add(segment);
            }
            areaFigure.Segments.Add(new LineSegment { Point = new Point(points.Last().X, yBase) });
        }

        areaFigure.Segments.Add(new LineSegment { Point = new Point(points[0].X, yBase) });

        lineGeometry.Figures!.Add(lineFigure);
        areaGeometry.Figures!.Add(areaFigure);

        return (lineGeometry, areaGeometry);
    }

    public void UpdateRealtimeHover(double mouseX)
    {
        if (LiveThroughputSamples.Count == 0)
        {
            IsHoveringRealtimeGraph = false;
            HoveredThroughputSample = null;
            HoveredRealtimePoint = null;
            return;
        }

        double canvasWidth = Math.Max(300.0, ChartWidth - 78.0);
        var closest = LiveThroughputSamples.OrderBy(p => Math.Abs(p.CanvasX - mouseX)).FirstOrDefault();

        if (closest != null)
        {
            HoveredThroughputSample = closest;
            HoverLineX = closest.CanvasX;
            HoverDownloadY = closest.DownloadY;
            HoverUploadY = closest.UploadY;
            HoverTooltipX = Math.Clamp(closest.CanvasX - 80, 10, canvasWidth - 170);
            IsHoveringRealtimeGraph = true;
        }
    }

    public void ClearRealtimeHover()
    {
        IsHoveringRealtimeGraph = false;
        HoveredThroughputSample = null;
        HoveredRealtimePoint = null;
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
        StatusDotColor      = isConnected ? "Success" : "Muted";

        var current = _sessionManager.CurrentSession;
        if (current != null)
        {
            HasCurrentSession = true;
            CurrentSessionNetwork = string.IsNullOrEmpty(current.NetworkName) ? "Unknown" : current.NetworkName;
            
            var duration = DateTime.UtcNow - current.StartTime;
            CurrentSessionDuration = duration.TotalHours >= 1 
                ? $"{(int)duration.TotalHours}h {duration.Minutes}m" 
                : $"{duration.Minutes}m {duration.Seconds}s";

            CurrentSessionDownload = ByteFormatter.FormatBytes(current.BytesDownloaded);
            CurrentSessionUpload = ByteFormatter.FormatBytes(current.BytesUploaded);
            CurrentSessionTotal = ByteFormatter.FormatBytes(current.BytesDownloaded + current.BytesUploaded);
        }
        else
        {
            HasCurrentSession = false;
            CurrentSessionNetwork = "No active session";
            CurrentSessionDuration = "—";
            CurrentSessionDownload = "—";
            CurrentSessionUpload = "—";
            CurrentSessionTotal = "—";
        }
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
            string deltaColor = "Muted";
            bool   hasDelta   = false;
            if (hasYesterday && yesterdayTotal > 0)
            {
                double pct = (todayTotal - yesterdayTotal) / (double)yesterdayTotal * 100.0;
                string sign = pct >= 0 ? "+" : "";
                deltaText  = $"{sign}{pct:F0}% vs yesterday";
                deltaColor = pct >= 0 ? "Warning" : "Success"; // orange = higher, green = lower
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
            
            // Update insights with budget and anomaly awareness
            var forecast      = await _forecastService.GetForecastAsync();
            long calcAvgDaily = forecast.HasSufficientData ? forecast.AverageDailyUsageBytes : 0;
            var budgetResult  = await _forecastService.GetBudgetResultAsync(monthTotal, todayTotal, calcAvgDaily);

            // Pattern Analysis & Anomaly Detection
            var anomalies = (await _patternAnalysisService.DetectAnomaliesAsync()).ToList();
            var (busyHoursText, busyDaysText) = await _patternAnalysisService.GetUsagePatternSummaryAsync();

            var insightsList = await _intelligenceService.GenerateInsightsWithBudgetAsync(
                SelectedPeriod, ConnectionName, budgetResult, forecast.HasSufficientData ? forecast : null, anomalies);

            // Application Intelligence & Recommendations
            var topProfiles = (await _appIntelligenceService.GetTopApplicationProfilesAsync(SelectedPeriod, 5)).ToList();
            var appRecs     = (await _appIntelligenceService.GenerateApplicationRecommendationsAsync()).ToList();

            // Unified Intelligence & Health Observatory
            var unifiedStream = (await _unifiedIntelligenceService.GetUnifiedEventsAsync(8)).ToList();
            var healthState   = await _unifiedIntelligenceService.GetDataSenseHealthAsync();
            var topUnifiedInsight = (await _unifiedAnalyticsIntelligenceService.GetUnifiedInsightsAsync()).FirstOrDefault();

            Dispatcher.UIThread.Post(() =>
            {
                Insights.Clear();
                foreach (var insight in insightsList)
                    Insights.Add(insight);
                HasInsights = Insights.Count > 0;

                DetectedAnomalies.Clear();
                foreach (var anomaly in anomalies)
                    DetectedAnomalies.Add(anomaly);
                HasAnomalies = DetectedAnomalies.Count > 0;

                BusyHoursText = busyHoursText;
                BusyDaysText  = busyDaysText;
                HasSufficientPatternData = !busyHoursText.Contains("Not enough historical data");

                TopAppProfiles.Clear();
                foreach (var profile in topProfiles)
                    TopAppProfiles.Add(profile);

                AppRecommendations.Clear();
                foreach (var rec in appRecs)
                    AppRecommendations.Add(rec);
                HasAppRecommendations = AppRecommendations.Count > 0;

                UnifiedEvents.Clear();
                foreach (var evt in unifiedStream)
                    UnifiedEvents.Add(evt);
                HasUnifiedEvents = UnifiedEvents.Count > 0;

                SystemHealth = healthState;
                
                PrimaryUnifiedInsight = topUnifiedInsight;
                HasUnifiedInsights = topUnifiedInsight != null;
            });

            // Load forecast/budget section
            await LoadForecastAsync(forecast, budgetResult, monthTotal, todayTotal, calcAvgDaily);
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
                    DailyChartItems.Clear();
                    foreach (var item in hourlyItems) DailyChartItems.Add(item);
                    IsChartEmpty = !hourlyItems.Any(i => i.HasData);
                    IsPeriodChartEmpty = IsChartEmpty;
                });
            }
            else
            {
                var dailyData = await _analyticsService.GetDailySeriesAsync(SelectedPeriod);
                var dailyItems = BuildChartItems(dailyData.ToList(), ChartWidth);

                Dispatcher.UIThread.Post(() =>
                {
                    IsHourlyChart = false;
                    DailyChartItems.Clear();
                    foreach (var item in dailyItems) DailyChartItems.Add(item);
                    IsChartEmpty = !dailyItems.Any(i => i.HasData);
                    IsPeriodChartEmpty = IsChartEmpty;
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

            // Load Top Processes (Historical Profiles)
            var rawTop = (await _applicationAnalyticsService.GetTopApplicationsAsync(5)).ToList();
            var topProcessesList = rawTop
                .GroupBy(p => p.ProcessName.Trim().ToLowerInvariant())
                .Select(g =>
                {
                    var first = g.First();
                    return new ApplicationHistoricalProfile
                    {
                        ProcessName = first.ProcessName,
                        Pid = first.Pid,
                        ExecutablePath = first.ExecutablePath,
                        UserName = first.UserName,
                        DataSource = first.DataSource,
                        DownloadBytes = g.Sum(x => x.DownloadBytes),
                        UploadBytes = g.Sum(x => x.UploadBytes),
                        TodayBytes = g.Sum(x => x.TodayBytes),
                        YesterdayBytes = g.Sum(x => x.YesterdayBytes),
                        SevenDayTotalBytes = g.Sum(x => x.SevenDayTotalBytes),
                        ThirtyDayTotalBytes = g.Sum(x => x.ThirtyDayTotalBytes),
                        ApplicationDisplayName = _appIconService.GetApplicationDisplayName(first.ProcessName, first.ExecutablePath),
                        ApplicationIcon = _appIconService.GetApplicationIcon(first.ProcessName, first.ExecutablePath)
                    };
                })
                .OrderByDescending(p => p.TotalBytes)
                .Take(5)
                .ToList();
            
            // Fallback to active live process telemetry if database process table has no records yet
            if (topProcessesList.Count == 0 && LiveProcessTraffic.Count > 0)
            {
                var liveGrouped = LiveProcessTraffic
                    .GroupBy(p => p.ProcessIdentifier.Trim().ToLowerInvariant())
                    .Select(g =>
                    {
                        var first = g.First();
                        return new ApplicationHistoricalProfile
                        {
                            ProcessName = first.ProcessIdentifier,
                            DownloadBytes = g.Sum(x => x.DownloadBytes),
                            UploadBytes = g.Sum(x => x.UploadBytes),
                            TodayBytes = g.Sum(x => x.DownloadBytes + x.UploadBytes),
                            DataSource = "Live Telemetry",
                            ApplicationDisplayName = _appIconService.GetApplicationDisplayName(first.ProcessIdentifier, first.ExecutablePath),
                            ApplicationIcon = _appIconService.GetApplicationIcon(first.ProcessIdentifier, first.ExecutablePath)
                        };
                    })
                    .OrderByDescending(p => p.TotalBytes)
                    .Take(5)
                    .ToList();
                topProcessesList = liveGrouped;
            }

            long totalProcessBytes = topProcessesList.Sum(p => p.TotalBytes);
            if (totalProcessBytes > 0)
            {
                foreach (var p in topProcessesList)
                {
                    p.PercentageOfTotal = (double)p.TotalBytes / totalProcessBytes * 100.0;
                }
            }

            var dlHeavy = topProcessesList.Count > 0 ? topProcessesList.MaxBy(p => p.TodayBytes) : null; // simplified max
            var ulHeavy = dlHeavy; // Profiles don't separate dl/ul cleanly for MaxBy, but we can just map it

            // Application Intelligence Insight
            string insight = "Insufficient application history.";
            try
            {
                var recs = await _appIntelligenceService.GenerateApplicationRecommendationsAsync();
                var topRec = recs.FirstOrDefault();
                if (topRec != null && topRec.Title != "Establishing Application Baselines")
                {
                    insight = topRec.Description;
                }
                else if (topProcessesList.Count > 0)
                {
                    var topApp = topProcessesList[0];
                    if (topApp.PercentageOfTotal > 0)
                        insight = $"{topApp.ProcessName} is responsible for {topApp.PercentageOfTotal:F0}% of usage.";
                    else
                        insight = "No unusual application activity detected.";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"App intelligence failed: {ex.Message}");
            }

            Dispatcher.UIThread.Post(() =>
            {
                TopProcesses.Clear();
                foreach (var process in topProcessesList)
                {
                    TopProcesses.Add(process);
                }

                HasTopProcesses = TopProcesses.Count > 0;
                TopAppInsightText = insight;
                
                if (dlHeavy != null && dlHeavy.TodayBytes > 0)
                {
                    DownloadHeavyProcessText = $"{dlHeavy.ProcessName} ({ByteFormatter.FormatBytes(dlHeavy.TodayBytes)})";
                    UploadHeavyProcessText = $"{dlHeavy.ProcessName} ({ByteFormatter.FormatBytes(dlHeavy.TodayBytes)})"; // Placeholder for UI layout
                }
                else
                {
                    DownloadHeavyProcessText = "—";
                    UploadHeavyProcessText = "—";
                }

                TopProcessesBaselineText = HasTopProcesses ? "" : "Collecting process usage baseline...";
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

    private List<DailyChartBarViewModel> BuildChartItems(
        List<DailyUsageRecord> daily, double chartWidth)
    {
        if (daily.Count == 0) return new List<DailyChartBarViewModel>();

        double canvasWidth = Math.Max(300.0, chartWidth - 78.0);
        double canvasHeight = 170.0;
        double usableHeight = 150.0;

        long maxTotal = daily.Max(d => d.TotalBytes);
        double yMax = Math.Max(1048576.0, maxTotal * 1.25); // 1 MB scale floor

        Dispatcher.UIThread.Post(() =>
        {
            YAxisTopText = ByteFormatter.FormatBytes((long)yMax);
            YAxisMidHighText = ByteFormatter.FormatBytes((long)(yMax * 0.75));
            YAxisMidText = ByteFormatter.FormatBytes((long)(yMax * 0.50));
            YAxisMidLowText = ByteFormatter.FormatBytes((long)(yMax * 0.25));
            YAxisMinText = "0 B";
        });

        int count = daily.Count;
        double colWidth = canvasWidth / Math.Max(count, 1);
        double barGap = Math.Max(4.0, colWidth * 0.25);
        double barWidth = Math.Max(colWidth - barGap, 3.0);

        var items = new List<DailyChartBarViewModel>(count);
        var trendPoints = new List<Point>(count);

        for (int i = 0; i < count; i++)
        {
            var d = daily[i];
            double totalBarHeight = (double)d.TotalBytes / yMax * usableHeight;

            double dlFrac = d.TotalBytes > 0 ? (double)d.BytesDownloaded / d.TotalBytes : 0.5;
            double ulFrac = 1.0 - dlFrac;

            double dlBarHeight = totalBarHeight * dlFrac;
            double ulBarHeight = totalBarHeight * ulFrac;

            double dlBarY = canvasHeight - dlBarHeight;
            double ulBarY = dlBarY - ulBarHeight;
            double barX = i * colWidth + (barGap / 2.0);
            bool isLatest = (i == count - 1);

            var bar = new DailyChartBarViewModel
            {
                DayLabel        = d.Day.ToString("MMM d"),
                BytesDownloaded = d.BytesDownloaded,
                BytesUploaded   = d.BytesUploaded,
                TotalBytes      = d.TotalBytes,
                DownloadedText  = ByteFormatter.FormatBytes(d.BytesDownloaded),
                UploadedText    = ByteFormatter.FormatBytes(d.BytesUploaded),
                TotalText       = ByteFormatter.FormatBytes(d.TotalBytes),
                BarX            = barX,
                BarWidth        = barWidth,
                DownloadBarHeight = Math.Max(dlBarHeight, 0),
                UploadBarHeight   = Math.Max(ulBarHeight, 0),
                DownloadBarY    = dlBarY,
                UploadBarY      = ulBarY,
                LabelY          = canvasHeight + 4,
                IsLatest        = isLatest
            };

            items.Add(bar);
            trendPoints.Add(new Point(bar.CenterX, bar.TopY));
        }

        var (lineGeom, glowGeom) = BuildTrendCurveGeometry(trendPoints, canvasHeight, canvasWidth);
        
        Dispatcher.UIThread.Post(() =>
        {
            TimelineTrendLineGeometry = lineGeom;
            TimelineTrendGlowGeometry = glowGeom;

            if (items.Count > 0)
            {
                LatestPointX = items.Last().CenterX;
                LatestPointY = items.Last().TopY;
                HasLatestPoint = true;
            }
            else
            {
                HasLatestPoint = false;
            }

            TimeAxisLabels.Clear();
            if (count <= 7)
            {
                foreach (var it in items) TimeAxisLabels.Add(it.DayLabel);
            }
            else
            {
                int step = Math.Max(1, (count - 1) / 4);
                for (int i = 0; i < count; i += step)
                {
                    TimeAxisLabels.Add(items[i].DayLabel);
                    if (TimeAxisLabels.Count == 5) break;
                }
                while (TimeAxisLabels.Count < 5 && count > 0)
                {
                    TimeAxisLabels.Add(items.Last().DayLabel);
                }
            }
        });

        return items;
    }

    private List<DailyChartBarViewModel> BuildHourlyChartItems(
        List<HourlyUsageRecord> hourly, double chartWidth)
    {
        double canvasWidth = Math.Max(300.0, chartWidth - 78.0);
        double canvasHeight = 170.0;
        double usableHeight = 150.0;

        var hourlyMap = hourly.ToDictionary(h => h.Hour);
        int currentHour = DateTime.UtcNow.Hour;
        int count = 24; // 24 hours of today

        long maxTotal = hourly.Count > 0 ? hourly.Max(h => h.TotalBytes) : 0;
        double yMax = Math.Max(1048576.0, maxTotal * 1.25); // 1 MB minimum scale floor

        Dispatcher.UIThread.Post(() =>
        {
            YAxisTopText = ByteFormatter.FormatBytes((long)yMax);
            YAxisMidHighText = ByteFormatter.FormatBytes((long)(yMax * 0.75));
            YAxisMidText = ByteFormatter.FormatBytes((long)(yMax * 0.50));
            YAxisMidLowText = ByteFormatter.FormatBytes((long)(yMax * 0.25));
            YAxisMinText = "0 B";
        });

        double colWidth = canvasWidth / count;
        double barGap = Math.Max(2.0, colWidth * 0.20);
        double barWidth = Math.Max(colWidth - barGap, 2.0);

        var items = new List<DailyChartBarViewModel>(count);
        var trendPoints = new List<Point>(count);

        for (int hour = 0; hour < count; hour++)
        {
            hourlyMap.TryGetValue(hour, out var h);
            long dlBytes = h?.BytesDownloaded ?? 0;
            long ulBytes = h?.BytesUploaded ?? 0;
            long total = dlBytes + ulBytes;

            double totalBarHeight = (double)total / yMax * usableHeight;
            double dlFrac = total > 0 ? (double)dlBytes / total : 0.5;
            double ulFrac = 1.0 - dlFrac;

            double dlBarHeight = totalBarHeight * dlFrac;
            double ulBarHeight = totalBarHeight * ulFrac;

            double dlBarY = canvasHeight - dlBarHeight;
            double ulBarY = dlBarY - ulBarHeight;
            double barX = hour * colWidth + (barGap / 2.0);
            bool isLatest = (hour == currentHour);

            var bar = new DailyChartBarViewModel
            {
                DayLabel        = $"{hour:00}:00",
                BytesDownloaded = dlBytes,
                BytesUploaded   = ulBytes,
                TotalBytes      = total,
                DownloadedText  = ByteFormatter.FormatBytes(dlBytes),
                UploadedText    = ByteFormatter.FormatBytes(ulBytes),
                TotalText       = ByteFormatter.FormatBytes(total),
                BarX            = barX,
                BarWidth        = barWidth,
                DownloadBarHeight = Math.Max(dlBarHeight, 0),
                UploadBarHeight   = Math.Max(ulBarHeight, 0),
                DownloadBarY    = dlBarY,
                UploadBarY      = ulBarY,
                LabelY          = canvasHeight + 4,
                IsLatest        = isLatest
            };

            items.Add(bar);
            trendPoints.Add(new Point(bar.CenterX, bar.TopY));
        }

        var (lineGeom, glowGeom) = BuildTrendCurveGeometry(trendPoints, canvasHeight, canvasWidth);

        Dispatcher.UIThread.Post(() =>
        {
            TimelineTrendLineGeometry = lineGeom;
            TimelineTrendGlowGeometry = glowGeom;

            if (items.Count > 0)
            {
                var activeBar = items.FirstOrDefault(b => b.IsLatest) ?? items.Last();
                LatestPointX = activeBar.CenterX;
                LatestPointY = activeBar.TopY;
                HasLatestPoint = true;
            }

            TimeAxisLabels.Clear();
            TimeAxisLabels.Add("00:00");
            TimeAxisLabels.Add("04:00");
            TimeAxisLabels.Add("08:00");
            TimeAxisLabels.Add("12:00");
            TimeAxisLabels.Add("16:00");
            TimeAxisLabels.Add("20:00");
            TimeAxisLabels.Add("23:00");
        });

        return items;
    }

    private static (Geometry Line, Geometry Glow) BuildTrendCurveGeometry(List<Point> points, double canvasHeight, double canvasWidth)
    {
        if (points.Count == 0)
        {
            return (Geometry.Parse($"M 0,{canvasHeight} L {canvasWidth},{canvasHeight}"), Geometry.Parse($"M 0,{canvasHeight} L {canvasWidth},{canvasHeight}"));
        }

        var lineGeometry = new PathGeometry();
        var lineFigure = new PathFigure
        {
            StartPoint = points[0],
            IsClosed = false,
            IsFilled = false
        };

        if (points.Count == 1)
        {
            lineFigure.Segments!.Add(new LineSegment { Point = new Point(canvasWidth, points[0].Y) });
        }
        else
        {
            for (int i = 0; i < points.Count - 1; i++)
            {
                var p0 = points[i];
                var p1 = points[i + 1];
                double midX = (p0.X + p1.X) / 2.0;

                lineFigure.Segments!.Add(new QuadraticBezierSegment
                {
                    Point1 = new Point(midX, p0.Y),
                    Point2 = p1
                });
            }
        }

        lineGeometry.Figures!.Add(lineFigure);
        return (lineGeometry, lineGeometry);
    }

    public void UpdateTimelineHover(double mouseX)
    {
        if (DailyChartItems.Count == 0)
        {
            IsHoveringTimeline = false;
            HoveredBar = null;
            return;
        }

        var closest = DailyChartItems.OrderBy(b => Math.Abs(b.CenterX - mouseX)).FirstOrDefault();
        if (closest != null)
        {
            double canvasWidth = Math.Max(300.0, ChartWidth - 78.0);
            HoveredBar = closest;
            HoverLineX = closest.CenterX;
            HoverBarX = Math.Max(0, closest.BarX - 2.0);
            HoverBarWidth = closest.BarWidth + 4.0;
            HoverPointY = closest.TopY;
            HoverTooltipX = Math.Clamp(closest.CenterX - 80.0, 8.0, Math.Max(8.0, canvasWidth - 170.0));
            IsHoveringTimeline = true;
        }
    }

    public void ClearTimelineHover()
    {
        IsHoveringTimeline = false;
        HoveredBar = null;
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
    // Forecast & Budget loading
    // ────────────────────────────────────────────────────────────────────────

    private async Task LoadForecastAsync(
        UsageForecast forecast,
        BudgetResult? budgetResult,
        long          monthUsageBytes,
        long          todayUsageBytes,
        long          avgDailyBytes)
    {
        try
        {
            // Build chart items
            var points     = await _forecastService.GetMonthForecastPointsAsync();
            var chartItems = BuildForecastChartItems(points, ChartWidth);

            Dispatcher.UIThread.Post(() =>
            {
                // ── Forecast section ────────────────────────────────────────
                HasForecast = forecast.HasSufficientData;

                if (forecast.HasSufficientData)
                {
                    ForecastCurrentText   = ByteFormatter.FormatBytes(forecast.CurrentUsageBytes);
                    ForecastProjectedText = ByteFormatter.FormatBytes(forecast.ProjectedMonthEndBytes);
                    ForecastRangeText     = $"{ByteFormatter.FormatBytes(forecast.LowerBoundBytes)} – {ByteFormatter.FormatBytes(forecast.UpperBoundBytes)}";
                    ForecastAvgDailyText  = ByteFormatter.FormatBytes(forecast.AverageDailyUsageBytes);
                    ForecastConfidenceText = forecast.Confidence switch
                    {
                        ForecastConfidence.High   => "High",
                        ForecastConfidence.Medium => "Medium",
                        _                         => "Low"
                    };
                }

                // Chart
                ForecastChartItems.Clear();
                foreach (var item in chartItems)
                    ForecastChartItems.Add(item);

                // ── Budget section ────────────────────────────────────────
                HasBudget = budgetResult != null;
                if (budgetResult != null)
                {
                    BudgetUsedText      = ByteFormatter.FormatBytes(budgetResult.UsedBytes);
                    BudgetLimitText     = ByteFormatter.FormatBytes(budgetResult.LimitBytes);
                    BudgetRemainingText = budgetResult.RemainingBytes >= 0
                        ? ByteFormatter.FormatBytes(budgetResult.RemainingBytes)
                        : $"-{ByteFormatter.FormatBytes(-budgetResult.RemainingBytes)}";
                    BudgetUsedPctText   = $"{Math.Min(budgetResult.UsedPercent, 100):F0}%";
                    BudgetStatusText    = budgetResult.StatusLabel;
                    BudgetStatusColor   = budgetResult.StatusColor;
                    BudgetProgressValue = budgetResult.ProgressValue;

                    // Exhaustion date
                    BudgetExhaustionText = budgetResult.EstimatedExhaustionDate.HasValue
                        ? (budgetResult.Status == BudgetStatus.Exceeded
                            ? "Already exceeded"
                            : $"Est. limit: {budgetResult.EstimatedExhaustionDate.Value:MMM d}")
                        : "Projected to remain within allowance";

                    // Pace
                    if (budgetResult.RequiredDailyPaceBytes.HasValue)
                    {
                        long req = budgetResult.RequiredDailyPaceBytes.Value;
                        long cur = budgetResult.CurrentDailyPaceBytes;
                        BudgetPaceText = cur > req
                            ? $"Usage pace: {ByteFormatter.FormatBytes(cur)}/day  ·  Required: {ByteFormatter.FormatBytes(req)}/day"
                            : $"Required pace: {ByteFormatter.FormatBytes(req)}/day  ·  Current: {ByteFormatter.FormatBytes(cur)}/day";
                    }
                    else
                    {
                        BudgetPaceText = "";
                    }

                    // Daily budget
                    HasDailyBudget = budgetResult.HasDailyBudget;
                    if (budgetResult.HasDailyBudget)
                    {
                        DailyBudgetUsedText  = ByteFormatter.FormatBytes(budgetResult.TodayUsedBytes);
                        DailyBudgetLimitText = ByteFormatter.FormatBytes(budgetResult.DailyLimitBytes);
                        bool dailyOk = budgetResult.TodayUsedBytes <= budgetResult.DailyLimitBytes;
                        DailyBudgetStatusText  = dailyOk ? "✅ Within daily limit" : "❌ Daily limit exceeded";
                        DailyBudgetStatusColor = dailyOk ? "Success" : "Danger";
                    }
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Forecast load failed: {ex.Message}");
        }
    }

    private static List<ForecastChartPointViewModel> BuildForecastChartItems(
        IList<DataSense.Models.ForecastPoint> points,
        double chartWidth)
    {
        if (points.Count == 0) return new List<ForecastChartPointViewModel>();

        // Find max bytes for scaling
        long maxBytes = 1;
        foreach (var p in points)
        {
            long val = p.IsForecast ? p.ForecastBytes : p.ActualBytes;
            if (val > maxBytes) maxBytes = val;
        }

        int    count    = points.Count;
        double barWidth = (chartWidth - (count - 1) * BarGap) / Math.Max(count, 1);
        var    items    = new List<ForecastChartPointViewModel>(count);

        for (int i = 0; i < count; i++)
        {
            var p         = points[i];
            double barX   = i * (barWidth + BarGap);

            if (!p.IsForecast)
            {
                double barH = (double)p.ActualBytes / maxBytes * ChartHeight;
                items.Add(new ForecastChartPointViewModel
                {
                    DayLabel        = p.Label,
                    IsForecast      = false,
                    IsToday         = p.IsToday,
                    BarX            = barX,
                    BarWidth        = Math.Max(barWidth, 1),
                    ActualBarHeight = Math.Max(barH, 0),
                    ActualBarY      = ChartHeight - Math.Max(barH, 0),
                    Tooltip         = p.Tooltip,
                });
            }
            else
            {
                double barH = (double)p.ForecastBytes / maxBytes * ChartHeight;
                items.Add(new ForecastChartPointViewModel
                {
                    DayLabel           = p.Label,
                    IsForecast         = true,
                    IsToday            = false,
                    BarX               = barX,
                    BarWidth           = Math.Max(barWidth, 1),
                    ForecastBarHeight  = Math.Max(barH, 0),
                    ForecastBarY       = ChartHeight - Math.Max(barH, 0),
                    Tooltip            = p.Tooltip,
                });
            }
        }
        return items;
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
