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
    private readonly IUnifiedAnalyticsIntelligenceService _unifiedAnalyticsIntelligenceService;
    private readonly NetworkSessionManager     _sessionManager;
    private readonly INetworkIdentityService   _identityService;
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

    // ── Widget Visibility & Personalization ─────────────────────────────────

    [ObservableProperty] private bool _showHeroNetwork = true;
    [ObservableProperty] private bool _showLiveThroughputGraph = true;
    [ObservableProperty] private bool _showTopConsumers = true;
    [ObservableProperty] private bool _showLiveProcessTraffic = true;
    [ObservableProperty] private bool _showApplicationUsage = true;
    [ObservableProperty] private bool _showNetworkInfo = true;
    [ObservableProperty] private string _cardLayout = "Standard (Default)";
    [ObservableProperty] private string _dataUnit = "Dynamic (Auto)";
    [ObservableProperty] private string _transferRateUnit = "MB/s";
    [ObservableProperty] private bool _enableChartAnimations = true;
    [ObservableProperty] private bool _smoothGraphRendering = true;
    [ObservableProperty] private bool _showChartTooltips = true;

    public bool IsApplicationUsageVisible => ShowTopConsumers && ShowApplicationUsage;
    public bool IsLiveProcessTrafficVisible => ShowLiveProcessTraffic && ShowApplicationUsage;
    public bool IsChartTooltipVisible => ShowChartTooltips && IsHoveringRealtimeGraph;
    public double DashboardSpacing => CardLayout switch
    {
        "Compact Grid" => 12,
        "Expanded Flow" => 28,
        _ => 20
    };

    private string FormatBytesForDisplay(long bytes)
    {
        bytes = Math.Max(0, bytes);
        return DataUnit switch
        {
            "Gigabytes (GB)" => $"{bytes / (1024d * 1024 * 1024):F1} GB",
            "Megabytes (MB)" => $"{bytes / (1024d * 1024):F1} MB",
            _ => ByteFormatter.FormatBytes(bytes)
        };
    }

    private string FormatSpeedForDisplay(double bytesPerSecond)
    {
        bytesPerSecond = Math.Max(0, bytesPerSecond);
        return TransferRateUnit switch
        {
            "KB/s" => $"{bytesPerSecond / 1024:F1} KB/s",
            "Mbps" => $"{bytesPerSecond * 8 / 1_000_000:F1} Mbps",
            _ => ByteFormatter.FormatSpeed(bytesPerSecond)
        };
    }

    public void ApplyDashboardPreferences(
        bool showSummaryCards,
        bool showNetworkChart,
        bool showTopConsumers,
        bool showLiveProcessTraffic,
        bool showApplicationUsage,
        bool showNetworkInfo,
        string? cardLayout,
        string? dataUnit,
        string? transferRateUnit,
        bool smoothGraphRendering,
        bool showChartTooltips)
    {
        ShowHeroNetwork = showSummaryCards;
        ShowLiveThroughputGraph = showNetworkChart;
        ShowTopConsumers = showTopConsumers;
        ShowLiveProcessTraffic = showLiveProcessTraffic;
        ShowApplicationUsage = showApplicationUsage;
        ShowNetworkInfo = showNetworkInfo;
        if (!string.IsNullOrWhiteSpace(cardLayout)) CardLayout = cardLayout;
        if (!string.IsNullOrWhiteSpace(dataUnit)) DataUnit = dataUnit;
        if (!string.IsNullOrWhiteSpace(transferRateUnit)) TransferRateUnit = transferRateUnit;
        SmoothGraphRendering = smoothGraphRendering;
        ShowChartTooltips = showChartTooltips;
        UpdateLiveValues(
            _networkMonitorWorker.ActiveInterface,
            _networkMonitorWorker.DownloadSpeed,
            _networkMonitorWorker.UploadSpeed,
            _networkMonitorWorker.TotalBytesDownloaded,
            _networkMonitorWorker.TotalBytesUploaded);
    }

    public async Task ApplyDefaultPeriodAsync(string? periodName)
    {
        var period = periodName?.Equals("7 Days", StringComparison.OrdinalIgnoreCase) == true
            ? AnalyticsPeriod.Last7Days
            : periodName?.Equals("Month", StringComparison.OrdinalIgnoreCase) == true
                ? AnalyticsPeriod.Last30Days
                : AnalyticsPeriod.Today;
        if (SelectedPeriod == period) return;

        SelectedPeriod = period;
        IsPeriodToday = period == AnalyticsPeriod.Today;
        IsPeriod7Days = period == AnalyticsPeriod.Last7Days;
        IsPeriod30Days = period == AnalyticsPeriod.Last30Days;
        await LoadPeriodAnalyticsAsync(showLoading: false);
    }

    // ── Chart ───────────────────────────────────────────────────────────────

    public ObservableCollection<DailyChartBarViewModel> DailyChartItems { get; } = new();

    // ── Real-Time Network Traffic Chart ──────────────────────────────────────

    public ObservableCollection<LiveThroughputSample> LiveThroughputSamples { get; } = new();
    private readonly List<LiveThroughputSample> _rollingLiveSamples = new();
    public ObservableCollection<RealtimeNetworkPoint> RealtimeTrafficPoints { get; } = new();
    public ObservableCollection<string> TimeAxisLabels { get; } = new();

    [ObservableProperty] private Geometry? _realtimeDownloadAreaGeometry;
    [ObservableProperty] private Geometry? _realtimeDownloadLineGeometry;
    [ObservableProperty] private Geometry? _realtimeUploadAreaGeometry;
    [ObservableProperty] private Geometry? _realtimeUploadLineGeometry;
    [ObservableProperty] private Geometry? _realtimeUploadBarsGeometry;

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

    [ObservableProperty] private string _yAxisUnitText = "Mbps";
    [ObservableProperty] private string _yAxisLevel6Text = "30.0";
    [ObservableProperty] private string _yAxisLevel5Text = "25.0";
    [ObservableProperty] private string _yAxisLevel4Text = "20.0";
    [ObservableProperty] private string _yAxisLevel3Text = "15.0";
    [ObservableProperty] private string _yAxisLevel2Text = "10.0";
    [ObservableProperty] private string _yAxisLevel1Text = "5.0";
    [ObservableProperty] private string _yAxisLevel0Text = "0";

    [ObservableProperty] private string _xAxisLabel0 = "08:00";
    [ObservableProperty] private string _xAxisLabel1 = "09:00";
    [ObservableProperty] private string _xAxisLabel2 = "12:00";
    [ObservableProperty] private string _xAxisLabel3 = "14:00";
    [ObservableProperty] private string _xAxisLabel4 = "16:00";
    [ObservableProperty] private string _xAxisLabel5 = "18:00";
    [ObservableProperty] private string _xAxisLabel6 = "20:00";

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
    public ObservableCollection<ApplicationHistoricalProfile> TopMonthProcesses { get; } = new();

    [ObservableProperty] private bool _hasLiveProcessTraffic = false;
    [ObservableProperty] private string _liveTotalDownloadedText = "0 B";
    [ObservableProperty] private string _liveTotalUploadedText = "0 B";
    [ObservableProperty] private string _liveTotalUsageText = "0 B";
    [ObservableProperty] private string _downloadHeavyProcessText = "—";
    [ObservableProperty] private string _uploadHeavyProcessText = "—";
    [ObservableProperty] private string _topProcessesBaselineText = "Collecting process usage baseline...";
    [ObservableProperty] private bool _hasTopProcesses = false;
    [ObservableProperty] private bool _hasTopMonthProcesses = false;
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
        IAppIconService?                appIconService = null,
        INetworkIdentityService?        identityService = null)
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
        _identityService        = identityService        ?? new NetworkIdentityService(_connectionService);

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

        // Load persisted dashboard widget preferences
        _ = LoadDashboardPreferencesAsync();
    }

    public async Task LoadDashboardPreferencesAsync()
    {
        try
        {
            bool periodChanged = false;
            var showHero = await _repository.GetSettingAsync("ShowSummaryCards");
            if (bool.TryParse(showHero, out bool sh))
                ShowHeroNetwork = sh;

            var showGraph = await _repository.GetSettingAsync("ShowNetworkChart");
            if (bool.TryParse(showGraph, out bool sg))
                ShowLiveThroughputGraph = sg;

            var showConsumers = await _repository.GetSettingAsync("ShowTopConsumers");
            if (bool.TryParse(showConsumers, out bool sc))
                ShowTopConsumers = sc;

            var showProcess = await _repository.GetSettingAsync("ShowLiveProcessTraffic");
            if (bool.TryParse(showProcess, out bool sp))
                ShowLiveProcessTraffic = sp;

            var showApplications = await _repository.GetSettingAsync("ShowApplicationUsage");
            if (bool.TryParse(showApplications, out bool sa))
                ShowApplicationUsage = sa;

            var showNetworkInfo = await _repository.GetSettingAsync("ShowNetworkInfo");
            if (bool.TryParse(showNetworkInfo, out bool si))
                ShowNetworkInfo = si;

            var savedPeriod = await _repository.GetSettingAsync("DefaultDashboardPeriod");
            if (!string.IsNullOrWhiteSpace(savedPeriod))
            {
                var period = savedPeriod.Equals("7 Days", StringComparison.OrdinalIgnoreCase)
                    ? AnalyticsPeriod.Last7Days
                    : savedPeriod.Equals("Month", StringComparison.OrdinalIgnoreCase)
                        ? AnalyticsPeriod.Last30Days
                        : AnalyticsPeriod.Today;
                if (SelectedPeriod != period)
                {
                    SelectedPeriod = period;
                    IsPeriodToday = period == AnalyticsPeriod.Today;
                    IsPeriod7Days = period == AnalyticsPeriod.Last7Days;
                    IsPeriod30Days = period == AnalyticsPeriod.Last30Days;
                    periodChanged = true;
                }
            }

            var savedLayout = await _repository.GetSettingAsync("CardLayout");
            if (!string.IsNullOrWhiteSpace(savedLayout))
                CardLayout = savedLayout;

            var savedDataUnit = await _repository.GetSettingAsync("DataUnit");
            if (!string.IsNullOrWhiteSpace(savedDataUnit))
                DataUnit = savedDataUnit;

            var savedTransferUnit = await _repository.GetSettingAsync("TransferRateUnit");
            if (!string.IsNullOrWhiteSpace(savedTransferUnit))
                TransferRateUnit = savedTransferUnit;

            var savedChartAnimations = await _repository.GetSettingAsync("EnableChartAnimations");
            if (bool.TryParse(savedChartAnimations, out bool chartAnimations))
                EnableChartAnimations = chartAnimations;

            var savedSmoothRendering = await _repository.GetSettingAsync("SmoothGraphRendering");
            if (bool.TryParse(savedSmoothRendering, out bool smoothRendering))
                SmoothGraphRendering = smoothRendering;

            var savedTooltips = await _repository.GetSettingAsync("ShowChartTooltips");
            if (bool.TryParse(savedTooltips, out bool showTooltips))
                ShowChartTooltips = showTooltips;

            UpdateLiveValues(
                _networkMonitorWorker.ActiveInterface,
                _networkMonitorWorker.DownloadSpeed,
                _networkMonitorWorker.UploadSpeed,
                _networkMonitorWorker.TotalBytesDownloaded,
                _networkMonitorWorker.TotalBytesUploaded);

            if (periodChanged)
                await LoadPeriodAnalyticsAsync(showLoading: false);
        }
        catch { }
    }

    partial void OnCardLayoutChanged(string value) => OnPropertyChanged(nameof(DashboardSpacing));
    partial void OnShowTopConsumersChanged(bool value) => OnPropertyChanged(nameof(IsApplicationUsageVisible));
    partial void OnShowApplicationUsageChanged(bool value)
    {
        OnPropertyChanged(nameof(IsApplicationUsageVisible));
        OnPropertyChanged(nameof(IsLiveProcessTrafficVisible));
    }
    partial void OnShowLiveProcessTrafficChanged(bool value) => OnPropertyChanged(nameof(IsLiveProcessTrafficVisible));
    partial void OnShowChartTooltipsChanged(bool value) => OnPropertyChanged(nameof(IsChartTooltipVisible));
    partial void OnIsHoveringRealtimeGraphChanged(bool value) => OnPropertyChanged(nameof(IsChartTooltipVisible));

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
            var start     = DateTime.SpecifyKind(utcNow.Date.AddDays(-(ChartDays - 1)), DateTimeKind.Utc);
            var end       = DateTime.SpecifyKind(utcNow.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
            var procDailyRaw = (await _repository.GetAllProcessesDailyUsageAsync(start, end)).ToList();
            List<DailyUsageRecord> dailyRaw;
            if (procDailyRaw.Count > 0)
            {
                dailyRaw = procDailyRaw;
            }
            else
            {
                dailyRaw = (await _repository.GetDailyUsageAsync(start, end)).ToList();
            }
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

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long DownloadBytes, long UploadBytes, DateTime LastSeen)> _liveProcessCumulativeBytes = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastLiveTrafficSample = DateTime.UtcNow;

    private void OnLiveTrafficUpdated(IEnumerable<ProcessNetworkUsage> currentBatch)
    {
        var now = DateTime.UtcNow;
        double elapsedSec = Math.Max(0.5, Math.Min(5.0, (now - _lastLiveTrafficSample).TotalSeconds));
        _lastLiveTrafficSample = now;

        // Aggregate socket/PID-level measurements into process-level entries
        var processGroups = currentBatch
            .GroupBy(p => p.ProcessIdentifier.Trim().ToLowerInvariant())
            .ToList();

        foreach (var g in processGroups)
        {
            string key = g.Key;
            double dlRate = g.Sum(x => x.DownloadRateBytesPerSec);
            double ulRate = g.Sum(x => x.UploadRateBytesPerSec);
            long rawDl = g.Sum(x => x.DownloadBytes);
            long rawUl = g.Sum(x => x.UploadBytes);

            long dlDelta = rawDl > 0 ? rawDl : (long)(dlRate * elapsedSec);
            long ulDelta = rawUl > 0 ? rawUl : (long)(ulRate * elapsedSec);

            _liveProcessCumulativeBytes.AddOrUpdate(
                key,
                _ => (Math.Max(rawDl, dlDelta), Math.Max(rawUl, ulDelta), now),
                (_, prev) => (prev.DownloadBytes + dlDelta, prev.UploadBytes + ulDelta, now));
        }

        var active = processGroups
            .Select(g =>
            {
                var first = g.First();
                string key = g.Key;
                _liveProcessCumulativeBytes.TryGetValue(key, out var cumulative);

                long dlBytes = Math.Max(g.Sum(x => x.DownloadBytes), cumulative.DownloadBytes);
                long ulBytes = Math.Max(g.Sum(x => x.UploadBytes), cumulative.UploadBytes);

                return new ProcessNetworkUsage
                {
                    ProcessIdentifier = first.ProcessIdentifier,
                    ExecutablePath = first.ExecutablePath,
                    Pid = first.Pid,
                    User = first.User,
                    DownloadRateBytesPerSec = g.Sum(x => x.DownloadRateBytesPerSec),
                    UploadRateBytesPerSec = g.Sum(x => x.UploadRateBytesPerSec),
                    DownloadBytes = dlBytes,
                    UploadBytes = ulBytes,
                    Timestamp = g.Max(x => x.Timestamp),
                    DataSource = first.DataSource,
                    ProcessIdentityKey = first.ProcessIdentityKey,
                    ApplicationDisplayName = _appIconService.GetApplicationDisplayName(first.ProcessIdentifier, first.ExecutablePath),
                    ApplicationIcon = _appIconService.GetApplicationIcon(first.ProcessIdentifier, first.ExecutablePath)
                };
            })
            .Where(p => p.DownloadRateBytesPerSec > 0 || p.UploadRateBytesPerSec > 0 || p.DownloadBytes > 0 || p.UploadBytes > 0)
            .OrderByDescending(p => p.TotalBytes > 0 ? p.TotalBytes : (long)(p.TotalRateBytesPerSec * 10))
            .Take(10)
            .ToList();

        Dispatcher.UIThread.Post(() =>
        {
            // Synchronize LiveProcessTraffic collection in-place so visual elements remain stable and hover state never flickers
            var targetKeys = active.Select(p => p.ProcessIdentifier.Trim().ToLowerInvariant()).ToHashSet();

            // 1. Remove dead processes that are no longer in top active
            for (int i = LiveProcessTraffic.Count - 1; i >= 0; i--)
            {
                var key = LiveProcessTraffic[i].ProcessIdentifier.Trim().ToLowerInvariant();
                if (!targetKeys.Contains(key))
                {
                    LiveProcessTraffic.RemoveAt(i);
                }
            }

            // 2. Update existing items in-place or insert new ones at matching positions
            long totalDl = 0;
            long totalUl = 0;
            for (int i = 0; i < active.Count; i++)
            {
                var item = active[i];
                var key = item.ProcessIdentifier.Trim().ToLowerInvariant();
                int existingIndex = -1;
                for (int j = 0; j < LiveProcessTraffic.Count; j++)
                {
                    if (LiveProcessTraffic[j].ProcessIdentifier.Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                    {
                        existingIndex = j;
                        break;
                    }
                }

                if (existingIndex >= 0)
                {
                    LiveProcessTraffic[existingIndex].UpdateFrom(item);
                    if (existingIndex != i && i < LiveProcessTraffic.Count)
                    {
                        LiveProcessTraffic.Move(existingIndex, i);
                    }
                }
                else
                {
                    LiveProcessTraffic.Insert(Math.Min(i, LiveProcessTraffic.Count), item);
                }

                totalDl += item.DownloadBytes;
                totalUl += item.UploadBytes;
            }

            HasLiveProcessTraffic = LiveProcessTraffic.Count > 0;
            LiveTotalDownloadedText = ByteFormatter.FormatBytes(totalDl);
            LiveTotalUploadedText = ByteFormatter.FormatBytes(totalUl);
            LiveTotalUsageText = ByteFormatter.FormatBytes(totalDl + totalUl);

            if (LiveProcessTraffic.Count > 0 && (!HasTopProcesses || TopProcesses.Count == 0 || TopProcesses.All(p => p.DataSource == "Live Telemetry")))
            {
                var liveGrouped = LiveProcessTraffic
                    .GroupBy(p => p.ProcessIdentifier.Trim().ToLowerInvariant())
                    .Select(g =>
                    {
                        var first = g.First();
                        long dl = g.Sum(x => x.DownloadBytes);
                        long ul = g.Sum(x => x.UploadBytes);
                        return new ApplicationHistoricalProfile
                        {
                            ProcessName = first.ProcessIdentifier,
                            DownloadBytes = dl,
                            UploadBytes = ul,
                            TodayBytes = dl + ul,
                            DataSource = "Live Telemetry",
                            ApplicationDisplayName = _appIconService.GetApplicationDisplayName(first.ProcessIdentifier, first.ExecutablePath),
                            ApplicationIcon = _appIconService.GetApplicationIcon(first.ProcessIdentifier, first.ExecutablePath)
                        };
                    })
                    .Where(p => p.TodayBytes > 0)
                    .OrderByDescending(p => p.TodayBytes)
                    .Take(5)
                    .ToList();

                long totalBytes = liveGrouped.Sum(p => p.TodayBytes);
                for (int i = 0; i < liveGrouped.Count; i++)
                {
                    var p = liveGrouped[i];
                    if (totalBytes > 0)
                    {
                        p.PercentageOfTotal = (double)p.TodayBytes / totalBytes * 100.0;
                    }
                    p.DisplayIndex = i;
                }

                SyncProfileCollection(TopProcesses, liveGrouped);
                HasTopProcesses = TopProcesses.Count > 0;

                if (!HasTopMonthProcesses || TopMonthProcesses.Count == 0 || TopMonthProcesses.All(p => p.DataSource == "Live Telemetry"))
                {
                    SyncProfileCollection(TopMonthProcesses, liveGrouped);
                    HasTopMonthProcesses = TopMonthProcesses.Count > 0;
                }
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

    private double _currentYMax = 100 * 1024.0; // 100 KB/s initial scale

    private double CalculateStableYMax(double maxObserved)
    {
        double[] niceScales = {
            100 * 1024.0,        // 100 KB/s
            250 * 1024.0,        // 250 KB/s
            500 * 1024.0,        // 500 KB/s
            1 * 1024 * 1024.0,   // 1 MB/s
            2 * 1024 * 1024.0,   // 2 MB/s
            5 * 1024 * 1024.0,   // 5 MB/s
            10 * 1024 * 1024.0,  // 10 MB/s
            20 * 1024 * 1024.0,  // 20 MB/s
            25 * 1024 * 1024.0,  // 25 MB/s
            50 * 1024 * 1024.0,  // 50 MB/s
            100 * 1024 * 1024.0, // 100 MB/s
            250 * 1024 * 1024.0, // 250 MB/s
            500 * 1024 * 1024.0, // 500 MB/s
            1024 * 1024 * 1024.0 // 1 GB/s
        };

        double target = Math.Max(100 * 1024.0, maxObserved * 1.20);
        double chosen = niceScales[0];
        foreach (var s in niceScales)
        {
            if (s >= target)
            {
                chosen = s;
                break;
            }
            chosen = s;
        }

        // Hysteresis: scale up immediately, scale down only when traffic is consistently below half
        if (chosen > _currentYMax || chosen < _currentYMax * 0.50)
        {
            _currentYMax = chosen;
        }

        return _currentYMax;
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
            UploadBytesPerSecond = uploadSpeed,
            IsPeriodUsage = false
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

        CurrentLiveDownloadSpeedText = FormatSpeedForDisplay(downloadSpeed);
        CurrentLiveUploadSpeedText = FormatSpeedForDisplay(uploadSpeed);

        PeakDownloadRateInWindow = LiveThroughputSamples.Max(p => p.DownloadBytesPerSecond);
        PeakUploadRateInWindow = LiveThroughputSamples.Max(p => p.UploadBytesPerSecond);

        PeakLiveDownloadSpeedText = FormatSpeedForDisplay(PeakDownloadRateInWindow);
        PeakLiveUploadSpeedText = FormatSpeedForDisplay(PeakUploadRateInWindow);

        HasRealtimeGraphData = LiveThroughputSamples.Count > 0;

        RebuildRealtimeChartGeometry();
    }

    public void RebuildRealtimeChartGeometry()
    {
        double canvasWidth = Math.Max(300.0, ChartWidth - 78.0);
        double usableHeight = 150.0;
        double yBase = 170.0;

        int count = LiveThroughputSamples.Count;
        if (count == 0)
        {
            RealtimeDownloadAreaGeometry = null;
            RealtimeDownloadLineGeometry = null;
            RealtimeUploadAreaGeometry = null;
            RealtimeUploadLineGeometry = null;
            RealtimeUploadBarsGeometry = null;
            HasRealtimeGraphData = false;
            return;
        }

        HasRealtimeGraphData = true;
        double maxObserved = Math.Max(PeakDownloadRateInWindow, PeakUploadRateInWindow);
        double yMax = CalculateStableYMax(maxObserved);

        // 5-Level Y-Axis Labels
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

        var (dlLine, dlArea) = BuildCurveGeometry(downloadPoints, yBase, canvasWidth, SmoothGraphRendering);
        var (ulLine, ulArea) = BuildCurveGeometry(uploadPoints, yBase, canvasWidth, SmoothGraphRendering);

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

        // Generate 7 X-Axis Timestamps (from start of window to NOW)
        var now = DateTime.Now;
        XAxisLabel0 = now.AddSeconds(-60).ToString("HH:mm:ss");
        XAxisLabel1 = now.AddSeconds(-50).ToString("HH:mm:ss");
        XAxisLabel2 = now.AddSeconds(-40).ToString("HH:mm:ss");
        XAxisLabel3 = now.AddSeconds(-30).ToString("HH:mm:ss");
        XAxisLabel4 = now.AddSeconds(-20).ToString("HH:mm:ss");
        XAxisLabel5 = now.AddSeconds(-10).ToString("HH:mm:ss");
        XAxisLabel6 = now.ToString("HH:mm:ss");

        TimeAxisLabels.Clear();
        TimeAxisLabels.Add("-60s");
        TimeAxisLabels.Add("-45s");
        TimeAxisLabels.Add("-30s");
        TimeAxisLabels.Add("-15s");
        TimeAxisLabels.Add("NOW");
    }

    private static (Geometry Line, Geometry Area) BuildCurveGeometry(List<Point> points, double yBase, double canvasWidth, bool smooth)
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

                PathSegment segment = smooth
                    ? new QuadraticBezierSegment
                    {
                        Point1 = new Point(midX, p0.Y),
                        Point2 = p1
                    }
                    : new LineSegment { Point = p1 };
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
        DownloadSpeedText   = FormatSpeedForDisplay(downloadSpeed);
        UploadSpeedText     = FormatSpeedForDisplay(uploadSpeed);
        TotalDownloadedText = FormatBytesForDisplay(bytesReceived);
        TotalUploadedText   = FormatBytesForDisplay(bytesSent);
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

            CurrentSessionDownload = FormatBytesForDisplay(current.BytesDownloaded);
            CurrentSessionUpload = FormatBytesForDisplay(current.BytesUploaded);
            CurrentSessionTotal = FormatBytesForDisplay(current.BytesDownloaded + current.BytesUploaded);
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
            var yesterdayStart = DateTime.SpecifyKind(utcNow.Date.AddDays(-1), DateTimeKind.Utc);
            var yesterdayEnd   = yesterdayStart.AddDays(1).AddTicks(-1);
            var yesterdayProc  = (await _repository.GetAllProcessesDailyUsageAsync(yesterdayStart, yesterdayEnd)).FirstOrDefault();
            long yesterdayTotal = 0;
            if (yesterdayProc != null)
            {
                yesterdayTotal = yesterdayProc.TotalBytes;
            }
            else
            {
                var yesterdayDaily = (await _repository.GetDailyUsageAsync(yesterdayStart, yesterdayEnd)).FirstOrDefault();
                yesterdayTotal = yesterdayDaily?.TotalBytes ?? 0;
            }
            bool hasYesterday   = yesterdayTotal > 0 || yesterdayProc != null;

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
            var chartStart = DateTime.SpecifyKind(utcNow.Date.AddDays(-(ChartDays - 1)), DateTimeKind.Utc);
            var chartEnd   = DateTime.SpecifyKind(utcNow.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
            var procDailyRaw = (await _repository.GetAllProcessesDailyUsageAsync(chartStart, chartEnd)).ToList();
            List<DailyUsageRecord> dailyRaw;
            if (procDailyRaw.Count > 0)
            {
                dailyRaw = procDailyRaw;
            }
            else
            {
                dailyRaw = (await _repository.GetDailyUsageAsync(chartStart, chartEnd)).ToList();
            }

            // GetAllProcessesDailyUsageAsync / GetDailyUsageAsync returns ORDER BY Day DESC — reverse to chronological
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

            // Load Top Processes (Historical Profiles) with forceRefresh to bypass TTL cache
            var allProfiles = (await _applicationAnalyticsService.GetApplicationProfilesAsync(forceRefresh: true)).ToList();

            // ── Top Processes for TODAY ─────────────────────────────────────────
            var todayTopGrouped = allProfiles
                .Where(p => p.TodayBytes > 0 || p.TodayDownloadBytes > 0 || p.TodayUploadBytes > 0)
                .GroupBy(p => p.ProcessName.Trim().ToLowerInvariant())
                .Select(g =>
                {
                    var first = g.First();
                    long todayDl = g.Sum(x => x.TodayDownloadBytes);
                    long todayUl = g.Sum(x => x.TodayUploadBytes);
                    long todayTotal = g.Sum(x => x.TodayBytes);
                    if (todayTotal == 0) todayTotal = todayDl + todayUl;

                    return new ApplicationHistoricalProfile
                    {
                        ProcessName = first.ProcessName,
                        Pid = first.Pid,
                        ExecutablePath = first.ExecutablePath,
                        UserName = first.UserName,
                        DataSource = first.DataSource,
                        DownloadBytes = todayDl,
                        UploadBytes = todayUl,
                        TodayBytes = todayTotal,
                        TodayDownloadBytes = todayDl,
                        TodayUploadBytes = todayUl,
                        YesterdayBytes = g.Sum(x => x.YesterdayBytes),
                        SevenDayTotalBytes = g.Sum(x => x.SevenDayTotalBytes),
                        ThirtyDayTotalBytes = g.Sum(x => x.ThirtyDayTotalBytes),
                        ApplicationDisplayName = _appIconService.GetApplicationDisplayName(first.ProcessName, first.ExecutablePath),
                        ApplicationIcon = _appIconService.GetApplicationIcon(first.ProcessName, first.ExecutablePath)
                    };
                })
                .Where(p => p.TodayBytes > 0)
                .OrderByDescending(p => p.TodayBytes)
                .Take(5)
                .ToList();

            var topProcessesList = todayTopGrouped;

            // Fallback to active live process telemetry if database process table has no today records yet
            if (topProcessesList.Count == 0 && LiveProcessTraffic.Count > 0)
            {
                var liveGrouped = LiveProcessTraffic
                    .GroupBy(p => p.ProcessIdentifier.Trim().ToLowerInvariant())
                    .Select(g =>
                    {
                        var first = g.First();
                        long dl = g.Sum(x => x.DownloadBytes);
                        long ul = g.Sum(x => x.UploadBytes);
                        return new ApplicationHistoricalProfile
                        {
                            ProcessName = first.ProcessIdentifier,
                            DownloadBytes = dl,
                            UploadBytes = ul,
                            TodayBytes = dl + ul,
                            DataSource = "Live Telemetry",
                            ApplicationDisplayName = _appIconService.GetApplicationDisplayName(first.ProcessIdentifier, first.ExecutablePath),
                            ApplicationIcon = _appIconService.GetApplicationIcon(first.ProcessIdentifier, first.ExecutablePath)
                        };
                    })
                    .Where(p => p.TodayBytes > 0)
                    .OrderByDescending(p => p.TodayBytes)
                    .Take(5)
                    .ToList();
                topProcessesList = liveGrouped;
            }

            long totalTodayProcessBytes = topProcessesList.Sum(p => p.TodayBytes);
            for (int i = 0; i < topProcessesList.Count; i++)
            {
                var p = topProcessesList[i];
                if (totalTodayProcessBytes > 0)
                {
                    p.PercentageOfTotal = (double)p.TodayBytes / totalTodayProcessBytes * 100.0;
                }
                p.DisplayIndex = i;
            }

            // ── Top Processes for THIS MONTH ────────────────────────────────────
            var monthTopGrouped = allProfiles
                .GroupBy(p => p.ProcessName.Trim().ToLowerInvariant())
                .Select(g =>
                {
                    var first = g.First();
                    long monthDl = g.Sum(x => x.DownloadBytes);
                    long monthUl = g.Sum(x => x.UploadBytes);
                    long monthTotal = g.Sum(x => x.ThirtyDayTotalBytes > 0 ? x.ThirtyDayTotalBytes : x.TodayBytes);
                    return new ApplicationHistoricalProfile
                    {
                        ProcessName = first.ProcessName,
                        Pid = first.Pid,
                        ExecutablePath = first.ExecutablePath,
                        UserName = first.UserName,
                        DataSource = first.DataSource,
                        DownloadBytes = monthDl,
                        UploadBytes = monthUl,
                        TodayBytes = g.Sum(x => x.TodayBytes),
                        YesterdayBytes = g.Sum(x => x.YesterdayBytes),
                        SevenDayTotalBytes = g.Sum(x => x.SevenDayTotalBytes),
                        ThirtyDayTotalBytes = monthTotal > 0 ? monthTotal : (monthDl + monthUl),
                        ApplicationDisplayName = _appIconService.GetApplicationDisplayName(first.ProcessName, first.ExecutablePath),
                        ApplicationIcon = _appIconService.GetApplicationIcon(first.ProcessName, first.ExecutablePath)
                    };
                })
                .Where(p => p.ThirtyDayTotalBytes > 0)
                .OrderByDescending(p => p.ThirtyDayTotalBytes)
                .Take(5)
                .ToList();

            var topMonthProcessesList = monthTopGrouped;

            if (topMonthProcessesList.Count == 0 && topProcessesList.Count > 0)
            {
                topMonthProcessesList = topProcessesList.Select(p => new ApplicationHistoricalProfile
                {
                    ProcessName = p.ProcessName,
                    Pid = p.Pid,
                    ExecutablePath = p.ExecutablePath,
                    UserName = p.UserName,
                    DataSource = p.DataSource,
                    DownloadBytes = p.DownloadBytes,
                    UploadBytes = p.UploadBytes,
                    TodayBytes = p.TodayBytes,
                    YesterdayBytes = p.YesterdayBytes,
                    SevenDayTotalBytes = p.SevenDayTotalBytes,
                    ThirtyDayTotalBytes = p.TodayBytes,
                    ApplicationDisplayName = p.ApplicationDisplayName,
                    ApplicationIcon = p.ApplicationIcon
                }).ToList();
            }

            long totalMonthProcessBytes = topMonthProcessesList.Sum(p => p.ThirtyDayTotalBytes);
            for (int i = 0; i < topMonthProcessesList.Count; i++)
            {
                var p = topMonthProcessesList[i];
                if (totalMonthProcessBytes > 0)
                {
                    p.PercentageOfTotal = (double)p.ThirtyDayTotalBytes / totalMonthProcessBytes * 100.0;
                }
                p.DisplayIndex = i;
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
                SyncProfileCollection(TopProcesses, topProcessesList);
                HasTopProcesses = TopProcesses.Count > 0;

                SyncProfileCollection(TopMonthProcesses, topMonthProcessesList);
                HasTopMonthProcesses = TopMonthProcesses.Count > 0;

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

        long maxDl = daily.Max(d => d.BytesDownloaded);
        long maxUl = daily.Max(d => d.BytesUploaded);
        long maxObserved = Math.Max(maxDl, maxUl);
        double yMax = Math.Max(1048576.0, maxObserved * 1.25); // 1 MB scale floor

        // Format 7 Y-Axis tick levels & unit
        string unit;
        double divisor;
        if (yMax >= 1024.0 * 1024.0 * 1024.0)
        {
            unit = "GB";
            divisor = 1024.0 * 1024.0 * 1024.0;
        }
        else if (yMax >= 1024.0 * 1024.0)
        {
            unit = "MB";
            divisor = 1024.0 * 1024.0;
        }
        else
        {
            unit = "KB";
            divisor = 1024.0;
        }

        double scaledMax = yMax / divisor;

        int count = daily.Count;
        double colWidth = canvasWidth / Math.Max(count, 1);
        double barGap = Math.Max(4.0, colWidth * 0.25);
        double barWidth = Math.Max(colWidth - barGap, 3.0);

        var items = new List<DailyChartBarViewModel>(count);
        var downloadPoints = new List<Point>(count);
        var uploadPoints = new List<Point>(count);
        var uploadBarsGroup = new GeometryGroup();
        var periodSamples = new List<LiveThroughputSample>(count);

        for (int i = 0; i < count; i++)
        {
            var d = daily[i];
            double x = (i + 0.5) * colWidth;

            double dlRatio = Math.Clamp((double)d.BytesDownloaded / yMax, 0.0, 1.0);
            double ulRatio = Math.Clamp((double)d.BytesUploaded / yMax, 0.0, 1.0);

            double dlY = canvasHeight - (dlRatio * usableHeight);
            double ulBarHeight = d.BytesUploaded > 0 ? Math.Max(4.0, ulRatio * usableHeight) : 0.0;
            double ulBarY = canvasHeight - ulBarHeight;
            double barLeftX = x - (barWidth / 2.0);

            downloadPoints.Add(new Point(x, dlY));
            uploadPoints.Add(new Point(x, ulBarY));

            if (ulBarHeight > 0.5)
            {
                uploadBarsGroup.Children.Add(new RectangleGeometry(new Rect(barLeftX, ulBarY, barWidth, ulBarHeight), 2, 2));
            }

            var sample = new LiveThroughputSample
            {
                Timestamp = d.Day,
                DownloadBytesPerSecond = d.BytesDownloaded,
                UploadBytesPerSecond = d.BytesUploaded,
                CanvasX = x,
                DownloadY = dlY,
                UploadY = ulBarY,
                BarWidth = barWidth,
                BarHeight = ulBarHeight,
                BarLeftX = barLeftX,
                BarTopY = ulBarY
            };
            periodSamples.Add(sample);

            var bar = new DailyChartBarViewModel
            {
                DayLabel        = d.Day.ToString("MMM d"),
                BytesDownloaded = d.BytesDownloaded,
                BytesUploaded   = d.BytesUploaded,
                TotalBytes      = d.TotalBytes,
                DownloadedText  = ByteFormatter.FormatBytes(d.BytesDownloaded),
                UploadedText    = ByteFormatter.FormatBytes(d.BytesUploaded),
                TotalText       = ByteFormatter.FormatBytes(d.TotalBytes),
                BarX            = barLeftX,
                BarWidth        = barWidth,
                DownloadBarHeight = dlRatio * usableHeight,
                UploadBarHeight   = ulBarHeight,
                DownloadBarY    = dlY,
                UploadBarY      = ulBarY,
                LabelY          = canvasHeight + 4,
                IsLatest        = (i == count - 1)
            };

            items.Add(bar);
        }

        var (dlLine, dlArea) = BuildCurveGeometry(downloadPoints, canvasHeight, canvasWidth, true);
        var (ulLine, ulArea) = BuildCurveGeometry(uploadPoints, canvasHeight, canvasWidth, true);
        var (trendLine, trendGlow) = BuildTrendCurveGeometry(downloadPoints, canvasHeight, canvasWidth);

        Dispatcher.UIThread.Post(() =>
        {
            YAxisUnitText = unit;
            YAxisLevel6Text = (scaledMax * 1.0).ToString("0.0");
            YAxisLevel5Text = (scaledMax * 5.0 / 6.0).ToString("0.0");
            YAxisLevel4Text = (scaledMax * 4.0 / 6.0).ToString("0.0");
            YAxisLevel3Text = (scaledMax * 3.0 / 6.0).ToString("0.0");
            YAxisLevel2Text = (scaledMax * 2.0 / 6.0).ToString("0.0");
            YAxisLevel1Text = (scaledMax * 1.0 / 6.0).ToString("0.0");
            YAxisLevel0Text = "0";

            YAxisTopText = ByteFormatter.FormatBytes((long)yMax);
            YAxisMidHighText = ByteFormatter.FormatBytes((long)(yMax * 0.75));
            YAxisMidText = ByteFormatter.FormatBytes((long)(yMax * 0.50));
            YAxisMidLowText = ByteFormatter.FormatBytes((long)(yMax * 0.25));
            YAxisMinText = "0 B";

            RealtimeDownloadLineGeometry = dlLine;
            RealtimeDownloadAreaGeometry = dlArea;
            RealtimeUploadLineGeometry = ulLine;
            RealtimeUploadAreaGeometry = ulArea;
            RealtimeUploadBarsGeometry = uploadBarsGroup;

            TimelineTrendLineGeometry = trendLine;
            TimelineTrendGlowGeometry = trendGlow;

            if (downloadPoints.Count > 0)
            {
                LatestDownloadX = downloadPoints.Last().X;
                LatestDownloadY = downloadPoints.Last().Y;
                LatestUploadX = uploadPoints.Last().X;
                LatestUploadY = uploadPoints.Last().Y;
                LatestPointX = downloadPoints.Last().X;
                LatestPointY = downloadPoints.Last().Y;
                HasLatestPoint = true;
            }
            else
            {
                HasLatestPoint = false;
            }

            LiveThroughputSamples.Clear();
            foreach (var s in periodSamples) LiveThroughputSamples.Add(s);
            HasRealtimeGraphData = periodSamples.Count > 0;

            // Populate 7 X-Axis labels
            if (count <= 7)
            {
                XAxisLabel0 = count > 0 ? items[0].DayLabel : "";
                XAxisLabel1 = count > 1 ? items[1].DayLabel : "";
                XAxisLabel2 = count > 2 ? items[2].DayLabel : "";
                XAxisLabel3 = count > 3 ? items[3].DayLabel : "";
                XAxisLabel4 = count > 4 ? items[4].DayLabel : "";
                XAxisLabel5 = count > 5 ? items[5].DayLabel : "";
                XAxisLabel6 = count > 6 ? items[6].DayLabel : "";
            }
            else
            {
                int step = Math.Max(1, (count - 1) / 6);
                XAxisLabel0 = items[0].DayLabel;
                XAxisLabel1 = items[Math.Min(1 * step, count - 1)].DayLabel;
                XAxisLabel2 = items[Math.Min(2 * step, count - 1)].DayLabel;
                XAxisLabel3 = items[Math.Min(3 * step, count - 1)].DayLabel;
                XAxisLabel4 = items[Math.Min(4 * step, count - 1)].DayLabel;
                XAxisLabel5 = items[Math.Min(5 * step, count - 1)].DayLabel;
                XAxisLabel6 = items[count - 1].DayLabel;
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

        long maxDl = hourly.Count > 0 ? hourly.Max(h => h.BytesDownloaded) : 0;
        long maxUl = hourly.Count > 0 ? hourly.Max(h => h.BytesUploaded) : 0;
        long maxObserved = Math.Max(maxDl, maxUl);
        double yMax = Math.Max(1048576.0, maxObserved * 1.25); // 1 MB minimum scale floor

        string unit;
        double divisor;
        if (yMax >= 1024.0 * 1024.0 * 1024.0)
        {
            unit = "GB";
            divisor = 1024.0 * 1024.0 * 1024.0;
        }
        else if (yMax >= 1024.0 * 1024.0)
        {
            unit = "MB";
            divisor = 1024.0 * 1024.0;
        }
        else
        {
            unit = "KB";
            divisor = 1024.0;
        }

        double scaledMax = yMax / divisor;

        double colWidth = canvasWidth / count;
        double barGap = Math.Max(2.0, colWidth * 0.20);
        double barWidth = Math.Max(colWidth - barGap, 2.5);

        var items = new List<DailyChartBarViewModel>(count);
        var downloadPoints = new List<Point>(count);
        var uploadPoints = new List<Point>(count);
        var uploadBarsGroup = new GeometryGroup();
        var periodSamples = new List<LiveThroughputSample>(count);

        for (int hour = 0; hour < count; hour++)
        {
            hourlyMap.TryGetValue(hour, out var h);
            long dlBytes = h?.BytesDownloaded ?? 0;
            long ulBytes = h?.BytesUploaded ?? 0;
            long total = dlBytes + ulBytes;

            double x = (hour + 0.5) * colWidth;

            double dlRatio = Math.Clamp((double)dlBytes / yMax, 0.0, 1.0);
            double ulRatio = Math.Clamp((double)ulBytes / yMax, 0.0, 1.0);

            double dlY = canvasHeight - (dlRatio * usableHeight);
            double ulBarHeight = ulBytes > 0 ? Math.Max(4.0, ulRatio * usableHeight) : 0.0;
            double ulBarY = canvasHeight - ulBarHeight;
            double barLeftX = x - (barWidth / 2.0);

            downloadPoints.Add(new Point(x, dlY));
            uploadPoints.Add(new Point(x, ulBarY));

            if (ulBarHeight > 0.5)
            {
                uploadBarsGroup.Children.Add(new RectangleGeometry(new Rect(barLeftX, ulBarY, barWidth, ulBarHeight), 2, 2));
            }

            var sample = new LiveThroughputSample
            {
                Timestamp = DateTime.Today.AddHours(hour),
                DownloadBytesPerSecond = dlBytes,
                UploadBytesPerSecond = ulBytes,
                CanvasX = x,
                DownloadY = dlY,
                UploadY = ulBarY,
                BarWidth = barWidth,
                BarHeight = ulBarHeight,
                BarLeftX = barLeftX,
                BarTopY = ulBarY
            };
            periodSamples.Add(sample);

            var bar = new DailyChartBarViewModel
            {
                DayLabel        = $"{hour:00}:00",
                BytesDownloaded = dlBytes,
                BytesUploaded   = ulBytes,
                TotalBytes      = total,
                DownloadedText  = ByteFormatter.FormatBytes(dlBytes),
                UploadedText    = ByteFormatter.FormatBytes(ulBytes),
                TotalText       = ByteFormatter.FormatBytes(total),
                BarX            = barLeftX,
                BarWidth        = barWidth,
                DownloadBarHeight = dlRatio * usableHeight,
                UploadBarHeight   = ulBarHeight,
                DownloadBarY    = dlY,
                UploadBarY      = ulBarY,
                LabelY          = canvasHeight + 4,
                IsLatest        = (hour == currentHour)
            };

            items.Add(bar);
        }

        var (dlLine, dlArea) = BuildCurveGeometry(downloadPoints, canvasHeight, canvasWidth, true);
        var (ulLine, ulArea) = BuildCurveGeometry(uploadPoints, canvasHeight, canvasWidth, true);
        var (trendLine, trendGlow) = BuildTrendCurveGeometry(downloadPoints, canvasHeight, canvasWidth);

        Dispatcher.UIThread.Post(() =>
        {
            YAxisUnitText = unit;
            YAxisLevel6Text = (scaledMax * 1.0).ToString("0.0");
            YAxisLevel5Text = (scaledMax * 5.0 / 6.0).ToString("0.0");
            YAxisLevel4Text = (scaledMax * 4.0 / 6.0).ToString("0.0");
            YAxisLevel3Text = (scaledMax * 3.0 / 6.0).ToString("0.0");
            YAxisLevel2Text = (scaledMax * 2.0 / 6.0).ToString("0.0");
            YAxisLevel1Text = (scaledMax * 1.0 / 6.0).ToString("0.0");
            YAxisLevel0Text = "0";

            YAxisTopText = ByteFormatter.FormatBytes((long)yMax);
            YAxisMidHighText = ByteFormatter.FormatBytes((long)(yMax * 0.75));
            YAxisMidText = ByteFormatter.FormatBytes((long)(yMax * 0.50));
            YAxisMidLowText = ByteFormatter.FormatBytes((long)(yMax * 0.25));
            YAxisMinText = "0 B";

            RealtimeDownloadLineGeometry = dlLine;
            RealtimeDownloadAreaGeometry = dlArea;
            RealtimeUploadLineGeometry = ulLine;
            RealtimeUploadAreaGeometry = ulArea;
            RealtimeUploadBarsGeometry = uploadBarsGroup;

            TimelineTrendLineGeometry = trendLine;
            TimelineTrendGlowGeometry = trendGlow;

            if (downloadPoints.Count > 0)
            {
                var activeIdx = Math.Clamp(currentHour, 0, count - 1);
                LatestDownloadX = downloadPoints[activeIdx].X;
                LatestDownloadY = downloadPoints[activeIdx].Y;
                LatestUploadX = uploadPoints[activeIdx].X;
                LatestUploadY = uploadPoints[activeIdx].Y;
                LatestPointX = downloadPoints[activeIdx].X;
                LatestPointY = downloadPoints[activeIdx].Y;
                HasLatestPoint = true;
            }

            LiveThroughputSamples.Clear();
            foreach (var s in periodSamples) LiveThroughputSamples.Add(s);
            HasRealtimeGraphData = periodSamples.Count > 0;

            XAxisLabel0 = "00:00";
            XAxisLabel1 = "04:00";
            XAxisLabel2 = "08:00";
            XAxisLabel3 = "12:00";
            XAxisLabel4 = "16:00";
            XAxisLabel5 = "20:00";
            XAxisLabel6 = "23:00";
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
            var identity = await _identityService.GetCurrentIdentityAsync(interfaceName);

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

                if (string.IsNullOrEmpty(interfaceName) || interfaceName == "None" || interfaceName == "Disconnected" || !identity.IsConnected)
                {
                    NetworkTypeText = "Disconnected";
                    NetworkIdentityText = "—";
                }
                else if (HasWifi)
                {
                    NetworkTypeText = "Wi-Fi";
                    NetworkIdentityText = identity.DisplayName;
                }
                else if (details.ConnectionType.Equals("ethernet", StringComparison.OrdinalIgnoreCase))
                {
                    NetworkTypeText = "Ethernet";
                    NetworkIdentityText = identity.DisplayName;
                }
                else
                {
                    NetworkTypeText = details.ConnectionType;
                    NetworkIdentityText = identity.DisplayName;
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

    private static void SyncProfileCollection(ObservableCollection<ApplicationHistoricalProfile> target, List<ApplicationHistoricalProfile> source)
    {
        var targetKeys = source.Select(p => p.ProcessName.Trim().ToLowerInvariant()).ToHashSet();
        for (int i = target.Count - 1; i >= 0; i--)
        {
            var key = target[i].ProcessName.Trim().ToLowerInvariant();
            if (!targetKeys.Contains(key))
            {
                target.RemoveAt(i);
            }
        }

        for (int i = 0; i < source.Count; i++)
        {
            var item = source[i];
            var key = item.ProcessName.Trim().ToLowerInvariant();
            int existingIndex = -1;
            for (int j = 0; j < target.Count; j++)
            {
                if (target[j].ProcessName.Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    existingIndex = j;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                target[existingIndex].UpdateFrom(item);
                if (existingIndex != i && i < target.Count)
                {
                    target.Move(existingIndex, i);
                }
            }
            else
            {
                target.Insert(Math.Min(i, target.Count), item);
            }
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
