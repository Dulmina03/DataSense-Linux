using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataSense.Database;
using DataSense.Helpers;
using DataSense.Models;
using DataSense.Services;

namespace DataSense.ViewModels;

public enum HistoryPeriodType
{
    Today,
    Last7Days,
    Month
}

public class MonthSelectItem
{
    public int Year { get; init; }
    public int Month { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public long TotalBytes { get; init; }
    public string TotalFormatted => ByteFormatter.FormatBytes(TotalBytes);

    public override string ToString() => DisplayName;
}

public class HistoricalSessionViewModel : ObservableObject
{
    public long Id { get; init; }
    public string NetworkName { get; init; } = string.Empty;
    public string InterfaceName { get; init; } = string.Empty;
    public string ConnectionType { get; init; } = string.Empty;
    private DateTime _startTime;
    public DateTime StartTime
    {
        get => _startTime;
        set
        {
            if (SetProperty(ref _startTime, value))
            {
                OnPropertyChanged(nameof(TimeRangeText));
            }
        }
    }

    private DateTime? _endTime;
    public DateTime? EndTime
    {
        get => _endTime;
        set
        {
            if (SetProperty(ref _endTime, value))
            {
                OnPropertyChanged(nameof(TimeRangeText));
                OnPropertyChanged(nameof(IsActive));
            }
        }
    }

    private long _bytesDownloaded;
    public long BytesDownloaded
    {
        get => _bytesDownloaded;
        set
        {
            if (SetProperty(ref _bytesDownloaded, value))
            {
                OnPropertyChanged(nameof(TotalBytes));
                OnPropertyChanged(nameof(DownloadedText));
                OnPropertyChanged(nameof(TotalText));
                OnPropertyChanged(nameof(DownloadRatio));
                OnPropertyChanged(nameof(UploadRatio));
            }
        }
    }

    private long _bytesUploaded;
    public long BytesUploaded
    {
        get => _bytesUploaded;
        set
        {
            if (SetProperty(ref _bytesUploaded, value))
            {
                OnPropertyChanged(nameof(TotalBytes));
                OnPropertyChanged(nameof(UploadedText));
                OnPropertyChanged(nameof(TotalText));
                OnPropertyChanged(nameof(DownloadRatio));
                OnPropertyChanged(nameof(UploadRatio));
            }
        }
    }

    public long TotalBytes => BytesDownloaded + BytesUploaded;
    public string DownloadedText => ByteFormatter.FormatBytes(BytesDownloaded);
    public string UploadedText => ByteFormatter.FormatBytes(BytesUploaded);
    public string TotalText => ByteFormatter.FormatBytes(TotalBytes);
    public string SubtitleText => !string.IsNullOrWhiteSpace(ConnectionType) ? ConnectionType : (!string.IsNullOrWhiteSpace(InterfaceName) ? InterfaceName : "Network Session");
    public string DisplayName => !string.IsNullOrWhiteSpace(NetworkName) ? NetworkName : (!string.IsNullOrWhiteSpace(InterfaceName) ? InterfaceName : "Network Session");
    public bool IsActive => !EndTime.HasValue;

    private int _displayIndex;
    public int DisplayIndex { get => _displayIndex; set => SetProperty(ref _displayIndex, value); }

    public double DownloadRatio => TotalBytes > 0 ? (double)BytesDownloaded / TotalBytes * 100.0 : 0.0;
    public double UploadRatio => TotalBytes > 0 ? (double)BytesUploaded / TotalBytes * 100.0 : 0.0;

    private double _relativeUsagePercent = 100.0;
    public double RelativeUsagePercent { get => _relativeUsagePercent; set => SetProperty(ref _relativeUsagePercent, value); }

    public string TimeRangeText => EndTime.HasValue
        ? (StartTime.Date == EndTime.Value.Date
            ? $"{StartTime:HH:mm} — {EndTime.Value:HH:mm}"
            : $"{StartTime:MMM dd} — {EndTime.Value:MMM dd}")
        : $"{StartTime:HH:mm} — Live";

    public void UpdateFrom(HistoricalSessionViewModel other)
    {
        StartTime = other.StartTime;
        EndTime = other.EndTime;
        BytesDownloaded = other.BytesDownloaded;
        BytesUploaded = other.BytesUploaded;
        DisplayIndex = other.DisplayIndex;
        RelativeUsagePercent = other.RelativeUsagePercent;
    }
}

public class NetworkUsageItemViewModel
{
    public string NetworkName { get; set; } = string.Empty;
    public string InterfaceName { get; set; } = string.Empty;
    public string ConnectionType { get; set; } = string.Empty;
    public long BytesDownloaded { get; set; }
    public long BytesUploaded { get; set; }
    public long TotalBytes => BytesDownloaded + BytesUploaded;
    public string DownloadedText => ByteFormatter.FormatBytes(BytesDownloaded);
    public string UploadedText => ByteFormatter.FormatBytes(BytesUploaded);
    public string TotalText => ByteFormatter.FormatBytes(TotalBytes);

    public string DisplayName => !string.IsNullOrWhiteSpace(NetworkName) ? NetworkName : (!string.IsNullOrWhiteSpace(InterfaceName) ? $"Interface: {InterfaceName}" : "Unknown Network");

    public string SubtitleText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(InterfaceName) && InterfaceName != "None")
            {
                string type = !string.IsNullOrWhiteSpace(ConnectionType) && ConnectionType != "Unknown" ? ConnectionType : "Network";
                return $"{type} • {InterfaceName}";
            }
            if (!string.IsNullOrWhiteSpace(ConnectionType) && ConnectionType != "Unknown")
            {
                return ConnectionType;
            }
            return "Connected Network";
        }
    }

    public int DisplayIndex { get; set; }
    public double RelativeUsagePercent { get; set; } = 100.0;
}

public class MonthlyNetworkSummaryViewModel
{
    public string NetworkName { get; init; } = string.Empty;
    public string DisplayName => !string.IsNullOrWhiteSpace(NetworkName) ? NetworkName : (!string.IsNullOrWhiteSpace(InterfaceName) ? InterfaceName : "Network Session");
    public string InterfaceName { get; init; } = string.Empty;
    public string ConnectionType { get; init; } = string.Empty;
    public long BytesDownloaded { get; init; }
    public long BytesUploaded { get; init; }
    public long TotalBytes => BytesDownloaded + BytesUploaded;
    public string DownloadedText => ByteFormatter.FormatBytes(BytesDownloaded);
    public string UploadedText => ByteFormatter.FormatBytes(BytesUploaded);
    public string TotalText => ByteFormatter.FormatBytes(TotalBytes);
    public double RelativeUsagePercent { get; set; } = 100.0;
    public int DisplayIndex { get; set; }
}

public class HistoricalGraphSample
{
    public DateTime Timestamp { get; set; }
    public string Label { get; set; } = string.Empty;
    public string FullTitle { get; set; } = string.Empty;
    public long DownloadBytes { get; set; }
    public long UploadBytes { get; set; }
    public long TotalBytes => DownloadBytes + UploadBytes;
    public double CanvasX { get; set; }
    public double DownloadY { get; set; }
    public double UploadY { get; set; }
    public string DownloadFormatted => ByteFormatter.FormatBytes(DownloadBytes);
    public string UploadFormatted => ByteFormatter.FormatBytes(UploadBytes);
    public string TotalFormatted => ByteFormatter.FormatBytes(TotalBytes);
    public double DownloadBarHeight { get; set; }
    public double UploadBarHeight { get; set; }
}

public partial class HistoryViewModel : ViewModelBase, IDisposable
{
    private readonly INetworkUsageRepository _repository;
    private readonly IHistoricalAnalyticsService _historicalService;
    private readonly IApplicationAnalyticsService _appAnalyticsService;
    private readonly IAppIconService _appIconService;
    private readonly IApplicationChartColorProvider _colorProvider;
    private readonly INetworkMonitorWorker _networkMonitorWorker;
    private readonly INetworkIdentityService _identityService;

    private bool _initialising = true;
    private bool _disposed;

    public override string Title => "Usage History";

    public HistoryViewModel(
        INetworkUsageRepository repository,
        IHistoricalAnalyticsService historicalService,
        IApplicationAnalyticsService appAnalyticsService,
        IAppIconService appIconService,
        IApplicationChartColorProvider colorProvider,
        INetworkMonitorWorker networkMonitorWorker,
        INetworkIdentityService? identityService = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _historicalService = historicalService ?? throw new ArgumentNullException(nameof(historicalService));
        _appAnalyticsService = appAnalyticsService ?? throw new ArgumentNullException(nameof(appAnalyticsService));
        _appIconService = appIconService ?? throw new ArgumentNullException(nameof(appIconService));
        _colorProvider = colorProvider ?? throw new ArgumentNullException(nameof(colorProvider));
        _networkMonitorWorker = networkMonitorWorker ?? throw new ArgumentNullException(nameof(networkMonitorWorker));
        _identityService = identityService ?? new NetworkIdentityService(new LinuxNetworkConnectionService());

        _selectedPeriod = HistoryPeriodType.Last7Days;
        _selectedInterface = "All";
        _selectedSortOption = "Total (Desc)";
        _initialising = false;

        _networkMonitorWorker.NetworkUsageUpdated += OnNetworkUsageUpdated;

        _ = InitializeAsync();
    }

    private readonly System.Threading.SemaphoreSlim _loadLock = new(1, 1);
    private int _loadVersion = 0;

    private static void RunOnUI(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    // ── Observable Collections ─────────────────────────────────────────────────

    public ObservableCollection<HistoricalSessionViewModel> NetworkSessions { get; } = new();
    public ObservableCollection<HistoricalSessionViewModel> FilteredNetworkSessions { get; } = new();

    public ObservableCollection<ApplicationHistoricalProfile> Applications { get; } = new();
    public ObservableCollection<ApplicationHistoricalProfile> FilteredApplications { get; } = new();
    public ObservableCollection<ApplicationHistoricalProfile> ApplicationBreakdownItems { get; } = new();

    public ObservableCollection<MonthSelectItem> AvailableMonths { get; } = new();
    public ObservableCollection<string> Interfaces { get; } = new();

    public string[] SortOptions { get; } = ["Total (Desc)", "Download (Desc)", "Upload (Desc)", "Share (Desc)"];

    // Chart #1: Selected Period Network Usage (12 buckets for Today, 7 for 7-Days, 28-31 for Month)
    public ObservableCollection<HistoricalGraphSample> HistoricalChartPoints { get; } = new();

    // Chart #2: Monthly Usage Breakdown (Always 12 calendar months: Jan - Dec)
    public ObservableCollection<HistoricalGraphSample> TwelveMonthChartPoints { get; } = new();

    // ── State ──────────────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _isDataState;
    [ObservableProperty] private string? _errorMessage;

    // ── Period & Filter State ──────────────────────────────────────────────────

    [ObservableProperty] private HistoryPeriodType _selectedPeriod = HistoryPeriodType.Last7Days;
    [ObservableProperty] private bool _isTodayActive;
    [ObservableProperty] private bool _is7DaysActive = true;
    [ObservableProperty] private bool _isMonthActive;

    [ObservableProperty] private MonthSelectItem? _selectedMonth;
    [ObservableProperty] private string _selectedInterface = "All";
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedSortOption = "Total (Desc)";

    // ── Overview Metrics ───────────────────────────────────────────────────────

    [ObservableProperty] private string _periodBadgeText = "LAST 7 DAYS";
    [ObservableProperty] private string _totalUsageText = "0 B";
    [ObservableProperty] private string _totalUsageTrendText = "for selected period";
    [ObservableProperty] private string _totalUsageTrendColor = "TextSecondary";

    [ObservableProperty] private string _totalDownloadedText = "0 B";
    [ObservableProperty] private string _downloadShareText = "0.0%";

    [ObservableProperty] private string _totalUploadedText = "0 B";
    [ObservableProperty] private string _uploadShareText = "0.0%";

    [ObservableProperty] private string _averageUsageLabel = "AVG / DAY";
    [ObservableProperty] private string _averageUsageText = "0 B";
    [ObservableProperty] private string _averageUsageTrendText = "Active days only";
    [ObservableProperty] private string _averageUsageTrendColor = "TextSecondary";

    [ObservableProperty] private string _applicationBreakdownSubtitle = "Application network usage for the last 7 days";
    [ObservableProperty] private string _usageExplorerSubtitle = "Application network usage for the last 7 days";
    [ObservableProperty] private string _applicationCountText = "0 applications";
    [ObservableProperty] private string _totalApplicationUsageText = "0 B";
    [ObservableProperty] private string _totalApplicationDownloadText = "0 B";
    [ObservableProperty] private string _totalApplicationUploadText = "0 B";

    // ── Chart #1 (Network Usage) State ─────────────────────────────────────────

    [ObservableProperty] private bool _hasHistoricalGraphData;
    [ObservableProperty] private double _downloadBarWidth = 32.0;
    [ObservableProperty] private double _uploadBarWidth = 32.0;
    [ObservableProperty] private double _barGap = 8.0;

    [ObservableProperty] private string _yAxisTopText = "1 MB";
    [ObservableProperty] private string _yAxisMidHighText = "750 KB";
    [ObservableProperty] private string _yAxisMidText = "500 KB";
    [ObservableProperty] private string _yAxisMidLowText = "250 KB";
    [ObservableProperty] private string _yAxisMinText = "0 B";

    [ObservableProperty] private double _chartWidth = 900.0;
    [ObservableProperty] private double _chartHeight = 300.0;

    // Chart #1 Hover Tooltip
    [ObservableProperty] private bool _isHoverActive;
    [ObservableProperty] private double _hoverX;
    [ObservableProperty] private double _hoverY;
    [ObservableProperty] private string _hoverTimestampText = "";
    [ObservableProperty] private string _hoverDownloadText = "";
    [ObservableProperty] private string _hoverUploadText = "";
    [ObservableProperty] private string _hoverTotalText = "";

    // ── Chart #2 (Monthly Usage Breakdown - 12 Months) State ───────────────────

    [ObservableProperty] private bool _hasTwelveMonthData;
    [ObservableProperty] private double _twelveMonthDownloadBarWidth = 18.0;
    [ObservableProperty] private double _twelveMonthUploadBarWidth = 18.0;
    [ObservableProperty] private double _twelveMonthBarGap = 4.0;

    [ObservableProperty] private string _twelveMonthYAxisTopText = "1 GB";
    [ObservableProperty] private string _twelveMonthYAxisMidHighText = "750 MB";
    [ObservableProperty] private string _twelveMonthYAxisMidText = "500 MB";
    [ObservableProperty] private string _twelveMonthYAxisMidLowText = "250 MB";
    [ObservableProperty] private string _twelveMonthYAxisMinText = "0 B";

    // Chart #2 Hover Tooltip
    [ObservableProperty] private bool _isTwelveMonthHoverActive;
    [ObservableProperty] private double _twelveMonthHoverX;
    [ObservableProperty] private double _twelveMonthHoverY;
    [ObservableProperty] private string _twelveMonthHoverTimestampText = "";
    [ObservableProperty] private string _twelveMonthHoverDownloadText = "";
    [ObservableProperty] private string _twelveMonthHoverUploadText = "";
    [ObservableProperty] private string _twelveMonthHoverTotalText = "";

    // ── Panel Flags & Network Usage State ──────────────────────────────────────

    public ObservableCollection<NetworkUsageItemViewModel> NetworkUsageItems { get; } = new();
    public ObservableCollection<MonthlyNetworkSummaryViewModel> MonthlyNetworkSummaries { get; } = new();

    [ObservableProperty] private HistoricalSessionViewModel? _currentLiveSession;
    [ObservableProperty] private bool _hasLiveSession;
    [ObservableProperty] private string _sessionNetworkCountText = "0 SESSIONS · 0 NETWORKS";
    [ObservableProperty] private string _monthlySectionHeaderRightText = "AUGUST 2026";
    [ObservableProperty] private string _networkUsageSectionTitle = "DAILY NETWORK USAGE";
    [ObservableProperty] private string _networkUsageHeaderBadge = "TODAY";
    [ObservableProperty] private string _monthlyTotalDownloadText = "0 B";
    [ObservableProperty] private string _monthlyTotalUploadText = "0 B";
    [ObservableProperty] private string _monthlyTotalUsageText = "0 B";

    [ObservableProperty] private bool _hasNetworkUsage;
    [ObservableProperty] private bool _hasNetworkSessions;
    [ObservableProperty] private bool _hasApplications;

    // ── Commands ───────────────────────────────────────────────────────────────

    [RelayCommand]
    public void SelectToday()
    {
        if (SelectedPeriod == HistoryPeriodType.Today)
        {
            _ = LoadAsync(showLoading: true);
        }
        else
        {
            SelectedPeriod = HistoryPeriodType.Today;
        }
    }

    [RelayCommand]
    public void SelectLast7Days()
    {
        if (SelectedPeriod == HistoryPeriodType.Last7Days)
        {
            _ = LoadAsync(showLoading: true);
        }
        else
        {
            SelectedPeriod = HistoryPeriodType.Last7Days;
        }
    }

    [RelayCommand]
    public void SelectMonth()
    {
        if (SelectedPeriod == HistoryPeriodType.Month)
        {
            _ = LoadAsync(showLoading: true);
        }
        else
        {
            SelectedPeriod = HistoryPeriodType.Month;
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await LoadAsync(showLoading: true);
    }

    // ── Property-Change Handlers ───────────────────────────────────────────────

    partial void OnSelectedPeriodChanged(HistoryPeriodType value)
    {
        IsTodayActive = value == HistoryPeriodType.Today;
        Is7DaysActive = value == HistoryPeriodType.Last7Days;
        IsMonthActive = value == HistoryPeriodType.Month;

        AverageUsageLabel = value == HistoryPeriodType.Today ? "AVG / HOUR" : "AVG / DAY";

        // Assign large, readable bar dimensions with prominent separation for Chart #1
        switch (value)
        {
            case HistoryPeriodType.Today:
                DownloadBarWidth = 28.0;
                UploadBarWidth = 28.0;
                BarGap = 8.0;
                break;
            case HistoryPeriodType.Last7Days:
                DownloadBarWidth = 28.0;
                UploadBarWidth = 28.0;
                BarGap = 8.0;
                break;
            case HistoryPeriodType.Month:
                DownloadBarWidth = 9.0;
                UploadBarWidth = 9.0;
                BarGap = 2.0;
                break;
        }

        if (!_initialising)
        {
            _ = LoadAsync(showLoading: true);
        }
    }

    partial void OnSelectedMonthChanged(MonthSelectItem? value)
    {
        if (!_initialising)
        {
            _ = LoadAsync(showLoading: true);
        }
    }

    partial void OnSelectedInterfaceChanged(string value)
    {
        if (!_initialising)
        {
            _ = LoadAsync(showLoading: true);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilters();
    }

    partial void OnSelectedSortOptionChanged(string value)
    {
        ApplyFilters();
    }

    // ── Lifecycle & Telemetry Callbacks ────────────────────────────────────────

    private async Task InitializeAsync()
    {
        // 1. Populate Interface list
        try
        {
            var ifaces = await _repository.GetInterfaceNamesAsync();
            Interfaces.Clear();
            Interfaces.Add("All");
            foreach (var iface in ifaces)
                Interfaces.Add(iface);
        }
        catch { }

        // 2. Populate Available Months (last 12 months)
        try
        {
            AvailableMonths.Clear();
            var utcNow = DateTime.UtcNow;
            for (int i = 0; i < 12; i++)
            {
                var dt = utcNow.AddMonths(-i);
                var start = new DateTime(dt.Year, dt.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                var item = new MonthSelectItem
                {
                    Year = dt.Year,
                    Month = dt.Month,
                    DisplayName = dt.ToString("MMMM yyyy", CultureInfo.InvariantCulture)
                };
                AvailableMonths.Add(item);
            }
            SelectedMonth = AvailableMonths.FirstOrDefault();
        }
        catch { }

        await LoadAsync(showLoading: true);
    }

    private void OnNetworkUsageUpdated(Models.NetworkUsage usage)
    {
        _ = LoadAsync(showLoading: false);
    }

    // ── Core Async Load ────────────────────────────────────────────────────────

    public (DateTime start, DateTime end) ComputeDateRange()
    {
        var utcNow = DateTime.UtcNow;
        var todayStart = DateTime.SpecifyKind(utcNow.Date, DateTimeKind.Utc);
        var todayEnd = todayStart.AddDays(1).AddTicks(-1);

        return SelectedPeriod switch
        {
            HistoryPeriodType.Today => (todayStart, todayEnd),
            HistoryPeriodType.Last7Days => (todayStart.AddDays(-6), todayEnd),
            HistoryPeriodType.Month =>
                SelectedMonth != null
                    ? (new DateTime(SelectedMonth.Year, SelectedMonth.Month, 1, 0, 0, 0, DateTimeKind.Utc),
                       new DateTime(SelectedMonth.Year, SelectedMonth.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1).AddTicks(-1))
                    : (new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc),
                       new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1).AddTicks(-1)),
            _ => (todayStart.AddDays(-6), todayEnd)
        };
    }

    private static (DateTime start, DateTime end) GetMondayToSundayRange(DateTime date)
    {
        int diffToMonday = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var monday = DateTime.SpecifyKind(date.AddDays(-diffToMonday), DateTimeKind.Utc);
        var sunday = monday.AddDays(6);
        return (monday, sunday.AddDays(1).AddTicks(-1));
    }

    /// <summary>
    /// Single authoritative method for querying and aggregating period-aware application telemetry from SQLite.
    /// </summary>
    public async Task<List<ApplicationHistoricalProfile>> LoadApplicationUsageAsync(DateTime start, DateTime end)
    {
        var topApps = (await _repository.GetTopProcessesAsync(start, end, 1000)).ToList();

        var mappedApps = topApps
            .GroupBy(a => a.ProcessName.Trim().ToLowerInvariant())
            .Select(g =>
            {
                var first = g.First();
                long appDl = g.Sum(x => x.BytesDownloaded);
                long appUl = g.Sum(x => x.BytesUploaded);

                return new ApplicationHistoricalProfile
                {
                    ProcessName = first.ProcessName,
                    Pid = first.Pid,
                    ExecutablePath = first.ExecutablePath,
                    UserName = first.UserName,
                    DataSource = first.DataSource,
                    DownloadBytes = appDl,
                    UploadBytes = appUl,
                    ApplicationDisplayName = _appIconService.GetApplicationDisplayName(first.ProcessName, first.ExecutablePath),
                    ApplicationIcon = _appIconService.GetApplicationIcon(first.ProcessName, first.ExecutablePath)
                };
            })
            .Where(a => a.DownloadBytes > 0 || a.UploadBytes > 0)
            .OrderByDescending(a => a.TotalBytes)
            .ToList();

        long totalAppDl = mappedApps.Sum(a => a.DownloadBytes);
        long totalAppUl = mappedApps.Sum(a => a.UploadBytes);
        long totalAppUsage = totalAppDl + totalAppUl;

        long maxAppTotal = mappedApps.Count > 0 ? mappedApps.Max(a => a.TotalBytes) : 0;
        for (int i = 0; i < mappedApps.Count; i++)
        {
            var app = mappedApps[i];
            app.DisplayIndex = i;
            app.PercentageOfTotal = totalAppUsage > 0 ? (double)app.TotalBytes / totalAppUsage * 100.0 : 0.0;
            app.RelativeUsagePercent = maxAppTotal > 0
                ? Math.Max((double)app.TotalBytes / maxAppTotal * 100.0, app.TotalBytes > 0 ? 3.0 : 0.0)
                : 0.0;
        }

        return mappedApps;
    }

    public async Task LoadAsync(bool showLoading = true)
    {
        int version = Interlocked.Increment(ref _loadVersion);
        await _loadLock.WaitAsync();
        try
        {
            if (version != _loadVersion) return;

            if (showLoading)
            {
                RunOnUI(() =>
                {
                    IsLoading = true;
                    HasHistoricalGraphData = false;
                    Applications.Clear();
                    FilteredApplications.Clear();
                    NetworkSessions.Clear();
                    FilteredNetworkSessions.Clear();
                    HasApplications = false;
                    HasNetworkSessions = false;
                    HasNetworkUsage = false;
                });
            }
            ErrorMessage = null;

            // Ensure canonical identity is resolved and cached for the active interface
            if (!string.IsNullOrWhiteSpace(_networkMonitorWorker.ActiveInterface) &&
                _networkMonitorWorker.ActiveInterface != "None" &&
                _networkMonitorWorker.ActiveInterface != "Disconnected")
            {
                try
                {
                    await _identityService.GetCurrentIdentityAsync(_networkMonitorWorker.ActiveInterface);
                }
                catch { }
            }

            var (start, end) = ComputeDateRange();
            string? ifaceFilter = SelectedInterface == "All" ? null : SelectedInterface;

            // 1. Fetch Applications for the selected period (Chart #3 & Explorer)
            var mappedApps = await LoadApplicationUsageAsync(start, end);
            long totalAppDl = mappedApps.Sum(a => a.DownloadBytes);
            long totalAppUl = mappedApps.Sum(a => a.UploadBytes);

            // 2. Fetch Daily or Hourly Usage for Chart #1 & Overview
            List<DailyUsageRecord> dailyList = new();
            List<HourlyUsageRecord> hourlyList = new();

            if (SelectedPeriod == HistoryPeriodType.Today)
            {
                hourlyList = (await _repository.GetHourlyUsageAsync(start.Date, ifaceFilter)).ToList();
                if (hourlyList.Count == 0 && (totalAppDl > 0 || totalAppUl > 0))
                {
                    hourlyList = (await _repository.GetAllProcessesHourlyUsageAsync(start.Date)).ToList();
                }
            }
            else
            {
                dailyList = (await _repository.GetDailyUsageAsync(start, end, ifaceFilter)).ToList();
                if (dailyList.Count == 0 && (totalAppDl > 0 || totalAppUl > 0))
                {
                    dailyList = (await _repository.GetAllProcessesDailyUsageAsync(start, end)).ToList();
                }
            }

            // 3. Fetch Sessions
            var sessions = (await _repository.GetSessionsAsync(start, end, ifaceFilter)).ToList();

            // 4. Fetch 12 Calendar Months for Chart #2 (January to December of active year)
            int activeYear = SelectedMonth != null ? SelectedMonth.Year : DateTime.UtcNow.Year;
            var twelveMonthSamples = new List<HistoricalGraphSample>(12);
            for (int m = 1; m <= 12; m++)
            {
                var (mStart, mEnd) = DateRangeHelper.GetLocalMonthRange(activeYear, m);
                var mDaily = (await _repository.GetDailyUsageAsync(mStart, mEnd, ifaceFilter)).ToList();
                long mDl = mDaily.Sum(d => d.BytesDownloaded);
                long mUl = mDaily.Sum(d => d.BytesUploaded);

                twelveMonthSamples.Add(new HistoricalGraphSample
                {
                    Timestamp = mStart,
                    Label = new DateTime(activeYear, m, 1).ToString("MMM", CultureInfo.InvariantCulture),
                    FullTitle = new DateTime(activeYear, m, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture),
                    DownloadBytes = mDl,
                    UploadBytes = mUl
                });
            }

            if (version != _loadVersion) return;

            long totalDl = 0;
            long totalUl = 0;

            if (SelectedPeriod == HistoryPeriodType.Today)
            {
                totalDl = hourlyList.Sum(h => h.BytesDownloaded);
                totalUl = hourlyList.Sum(h => h.BytesUploaded);
                if (totalDl == 0 && totalUl == 0)
                {
                    var (tDl, tUl) = await _repository.GetTodaySummaryAsync(ifaceFilter);
                    totalDl = tDl;
                    totalUl = tUl;
                }
                if (totalDl == 0 && totalUl == 0 && (totalAppDl > 0 || totalAppUl > 0))
                {
                    totalDl = totalAppDl;
                    totalUl = totalAppUl;
                }
            }
            else
            {
                totalDl = dailyList.Sum(d => d.BytesDownloaded);
                totalUl = dailyList.Sum(d => d.BytesUploaded);
                if (totalDl == 0 && totalUl == 0 && SelectedPeriod == HistoryPeriodType.Month)
                {
                    var (mDl, mUl) = await _repository.GetMonthSummaryAsync(ifaceFilter);
                    totalDl = mDl;
                    totalUl = mUl;
                }
                if (totalDl == 0 && totalUl == 0 && (totalAppDl > 0 || totalAppUl > 0))
                {
                    totalDl = totalAppDl;
                    totalUl = totalAppUl;
                }
            }

            long totalUsage = totalDl + totalUl;

            double dlShare = totalUsage > 0 ? (double)totalDl / totalUsage * 100.0 : 0.0;
            double ulShare = totalUsage > 0 ? (double)totalUl / totalUsage * 100.0 : 0.0;

            long avgUsage = 0;
            if (SelectedPeriod == HistoryPeriodType.Today)
            {
                int currentHour = DateTime.Now.Hour;
                avgUsage = currentHour >= 0 ? totalUsage / (currentHour + 1) : 0;
            }
            else
            {
                var activeDays = dailyList.Where(d => d.TotalBytes > 0).ToList();
                avgUsage = activeDays.Count > 0 ? (long)activeDays.Average(d => d.TotalBytes) : 0;
            }

            // 6. Compare with previous period for Trend Calculation
            string trendUsageText = "for selected period";
            string trendUsageColor = "TextSecondary";
            string trendAvgText = SelectedPeriod == HistoryPeriodType.Today ? "Average per hour" : "Active days only";
            string trendAvgColor = "TextSecondary";

            try
            {
                var duration = end - start;
                var prevStart = start - duration;
                var prevEnd = start.AddTicks(-1);

                long prevTotal = 0;
                if (SelectedPeriod == HistoryPeriodType.Today)
                {
                    var yesterdayHourly = await _repository.GetHourlyUsageAsync(prevStart.Date, ifaceFilter);
                    prevTotal = yesterdayHourly.Sum(h => h.TotalBytes);
                }
                else
                {
                    var prevDaily = await _repository.GetDailyUsageAsync(prevStart, prevEnd, ifaceFilter);
                    prevTotal = prevDaily.Sum(d => d.TotalBytes);
                }

                if (prevTotal > 0 && totalUsage > 0)
                {
                    double changePct = ((double)(totalUsage - prevTotal) / prevTotal) * 100.0;
                    string arrow = changePct >= 0 ? "↑" : "↓";
                    trendUsageText = $"{arrow} {Math.Abs(changePct):F1}% vs previous";
                    trendUsageColor = changePct >= 0 ? "Warning" : "Success";
                    trendAvgText = $"{arrow} {Math.Abs(changePct):F1}% vs prev trend";
                    trendAvgColor = trendUsageColor;
                }
            }
            catch { }

            // 7. Map Network Usage (Strictly aggregate data usage totals by canonical network identity)
            var networkGrouped = sessions
                .GroupBy(s => _identityService.GetCanonicalKey(s.NetworkName, s.InterfaceName))
                .Select(g =>
                {
                    var firstValid = g.FirstOrDefault(x => _identityService.IsValidNetworkName(x.NetworkName)) ?? g.First();
                    string displayName = _identityService.NormalizeNetworkName(firstValid.NetworkName, firstValid.InterfaceName);

                    return new NetworkUsageItemViewModel
                    {
                        NetworkName = displayName,
                        InterfaceName = firstValid.InterfaceName,
                        ConnectionType = firstValid.ConnectionType,
                        BytesDownloaded = g.Sum(x => x.BytesDownloaded),
                        BytesUploaded = g.Sum(x => x.BytesUploaded)
                    };
                })
                .OrderByDescending(m => m.TotalBytes)
                .ToList();

            if (sessions.Count == 0 && networkGrouped.Count > 0)
            {
                long netDlSum = networkGrouped.Sum(n => n.BytesDownloaded);
                long netUlSum = networkGrouped.Sum(n => n.BytesUploaded);
                if (netDlSum < totalDl) networkGrouped[0].BytesDownloaded += (totalDl - netDlSum);
                if (netUlSum < totalUl) networkGrouped[0].BytesUploaded += (totalUl - netUlSum);
            }
            else if (networkGrouped.Count == 0 && totalUsage > 0)
            {
                string activeIface = (!string.IsNullOrWhiteSpace(SelectedInterface) && SelectedInterface != "All"
                    ? SelectedInterface
                    : _networkMonitorWorker.ActiveInterface) ?? "wlo1";
                string activeNetworkName = _identityService.NormalizeNetworkName(null, activeIface);
                string activeConnType = (activeIface.StartsWith("wl", StringComparison.OrdinalIgnoreCase) || activeIface.StartsWith("wlan", StringComparison.OrdinalIgnoreCase))
                    ? "Wi-Fi"
                    : "Ethernet";

                try
                {
                    var id = _identityService.GetCurrentIdentityAsync(activeIface).GetAwaiter().GetResult();
                    if (id != null && (_identityService.IsValidNetworkName(id.DisplayName) || id.DisplayName == "Ethernet"))
                        activeNetworkName = id.DisplayName;
                    if (id != null)
                        activeConnType = id.Type.ToString();
                }
                catch { }

                networkGrouped.Add(new NetworkUsageItemViewModel
                {
                    NetworkName = activeNetworkName,
                    InterfaceName = activeIface,
                    ConnectionType = activeConnType,
                    BytesDownloaded = totalDl,
                    BytesUploaded = totalUl
                });
            }


            long maxNetworkTotal = networkGrouped.Count > 0 ? networkGrouped.Max(m => m.TotalBytes) : 0;
            for (int i = 0; i < networkGrouped.Count; i++)
            {
                networkGrouped[i].DisplayIndex = i;
                networkGrouped[i].RelativeUsagePercent = maxNetworkTotal > 0
                    ? Math.Max((double)networkGrouped[i].TotalBytes / maxNetworkTotal * 100.0, networkGrouped[i].TotalBytes > 0 ? 3.0 : 0.0)
                    : 0.0;
            }

            long totalNetworkDl = networkGrouped.Sum(m => m.BytesDownloaded);
            long totalNetworkUl = networkGrouped.Sum(m => m.BytesUploaded);
            long totalNetworkUsage = totalNetworkDl + totalNetworkUl;

            string networkSectionTitle = SelectedPeriod switch
            {
                HistoryPeriodType.Today => "DAILY NETWORK USAGE",
                HistoryPeriodType.Last7Days => "DAILY NETWORK USAGE",
                HistoryPeriodType.Month => "MONTHLY NETWORK USAGE",
                _ => "NETWORK USAGE"
            };

            string networkHeaderBadge = SelectedPeriod switch
            {
                HistoryPeriodType.Today => "TODAY",
                HistoryPeriodType.Last7Days => "LAST 7 DAYS",
                HistoryPeriodType.Month => SelectedMonth != null
                    ? SelectedMonth.DisplayName.ToUpperInvariant()
                    : DateTime.UtcNow.ToString("MMMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant(),
                _ => ""
            };

            // Dynamic Subtitles based on selected period
            string appSubtitle = SelectedPeriod switch
            {
                HistoryPeriodType.Today => "Application network usage for today",
                HistoryPeriodType.Last7Days => "Application network usage for the last 7 days",
                HistoryPeriodType.Month => SelectedMonth != null
                    ? $"Application network usage for {SelectedMonth.DisplayName}"
                    : $"Application network usage for {DateTime.UtcNow:MMMM yyyy}",
                _ => "Application network usage for the selected period"
            };

            // 8. Build Chart #1 Samples (6 buckets for Today, 7 for 7-Days, 28-31 for Month)
            var chartPoints = BuildHistoricalGraphSamples(dailyList, hourlyList, start, end);

            // Harmonize Chart #1 samples so they reconcile exactly to Usage Overview totals (totalDl, totalUl)
            long chartRawDl = chartPoints.Sum(p => p.DownloadBytes);
            long chartRawUl = chartPoints.Sum(p => p.UploadBytes);

            if (chartRawDl > 0 && totalDl > 0)
            {
                foreach (var p in chartPoints)
                {
                    p.DownloadBytes = (long)Math.Round((double)p.DownloadBytes / chartRawDl * totalDl);
                }
            }
            else if (chartRawDl == 0 && totalDl > 0 && chartPoints.Count > 0)
            {
                int activeIdx = SelectedPeriod == HistoryPeriodType.Today
                    ? Math.Min(DateTime.Now.Hour / 4, chartPoints.Count - 1)
                    : chartPoints.Count - 1;
                chartPoints[activeIdx].DownloadBytes = totalDl;
            }

            if (chartRawUl > 0 && totalUl > 0)
            {
                foreach (var p in chartPoints)
                {
                    p.UploadBytes = (long)Math.Round((double)p.UploadBytes / chartRawUl * totalUl);
                }
            }
            else if (chartRawUl == 0 && totalUl > 0 && chartPoints.Count > 0)
            {
                int activeIdx = SelectedPeriod == HistoryPeriodType.Today
                    ? Math.Min(DateTime.Now.Hour / 4, chartPoints.Count - 1)
                    : chartPoints.Count - 1;
                chartPoints[activeIdx].UploadBytes = totalUl;
            }

            // Harmonize Chart #2 active month sample so it matches Usage Overview total for that month
            int activeMonthNum = SelectedMonth != null ? SelectedMonth.Month : DateTime.UtcNow.Month;
            if (activeMonthNum >= 1 && activeMonthNum <= 12 && twelveMonthSamples.Count >= activeMonthNum)
            {
                var activeSample = twelveMonthSamples[activeMonthNum - 1];
                activeSample.DownloadBytes = Math.Max(activeSample.DownloadBytes, totalDl);
                activeSample.UploadBytes = Math.Max(activeSample.UploadBytes, totalUl);
            }

            // 9. Scale Bar Heights for Chart #1 & Chart #2
            ScaleBarHeights(chartPoints, twelveMonthSamples);

            string periodBadge = SelectedPeriod switch
            {
                HistoryPeriodType.Today => "TODAY",
                HistoryPeriodType.Last7Days => "LAST 7 DAYS",
                HistoryPeriodType.Month => SelectedMonth != null ? SelectedMonth.DisplayName.ToUpperInvariant() : "THIS MONTH",
                _ => "PERIOD"
            };

            // Post all results to UI
            RunOnUI(() =>
            {
                if (version != _loadVersion) return;
                PeriodBadgeText = periodBadge;
                TotalUsageText = ByteFormatter.FormatBytes(totalUsage);
                TotalUsageTrendText = trendUsageText;
                TotalUsageTrendColor = trendUsageColor;

                TotalDownloadedText = ByteFormatter.FormatBytes(totalDl);
                DownloadShareText = $"{dlShare:F1}% of total";

                TotalUploadedText = ByteFormatter.FormatBytes(totalUl);
                UploadShareText = $"{ulShare:F1}% of total";

                AverageUsageText = ByteFormatter.FormatBytes(avgUsage);
                AverageUsageTrendText = trendAvgText;
                AverageUsageTrendColor = trendAvgColor;

                ApplicationBreakdownSubtitle = appSubtitle;
                UsageExplorerSubtitle = appSubtitle;

                long totalAppDl = mappedApps.Sum(a => a.DownloadBytes);
                long totalAppUl = mappedApps.Sum(a => a.UploadBytes);
                long totalAppUsage = totalAppDl + totalAppUl;
                TotalApplicationUsageText = ByteFormatter.FormatBytes(totalAppUsage);
                TotalApplicationDownloadText = ByteFormatter.FormatBytes(totalAppDl);
                TotalApplicationUploadText = ByteFormatter.FormatBytes(totalAppUl);

                NetworkUsageSectionTitle = networkSectionTitle;
                NetworkUsageHeaderBadge = networkHeaderBadge;
                MonthlyTotalDownloadText = ByteFormatter.FormatBytes(totalNetworkDl);
                MonthlyTotalUploadText = ByteFormatter.FormatBytes(totalNetworkUl);
                MonthlyTotalUsageText = ByteFormatter.FormatBytes(totalNetworkUsage);

                NetworkUsageItems.Clear();
                foreach (var item in networkGrouped)
                    NetworkUsageItems.Add(item);

                NetworkSessions.Clear();
                MonthlyNetworkSummaries.Clear();

                var groupedSessionList = sessions
                    .GroupBy(s => _identityService.GetCanonicalKey(s.NetworkName, s.InterfaceName))
                    .Select(g =>
                    {
                        var firstValid = g.FirstOrDefault(x => _identityService.IsValidNetworkName(x.NetworkName)) ?? g.First();
                        string displayName = _identityService.NormalizeNetworkName(firstValid.NetworkName, firstValid.InterfaceName);
                        long dl = g.Sum(x => x.BytesDownloaded);
                        long ul = g.Sum(x => x.BytesUploaded);
                        long total = dl + ul;
                        var minStart = g.Min(x => x.StartTime);
                        bool hasActive = g.Any(x => !x.EndTime.HasValue);
                        DateTime? maxEnd = hasActive ? null : g.Max(x => x.EndTime);

                        return new
                        {
                            FirstSession = firstValid,
                            DisplayName = displayName,
                            BytesDownloaded = dl,
                            BytesUploaded = ul,
                            TotalBytes = total,
                            StartTime = minStart,
                            EndTime = maxEnd
                        };
                    })
                    .OrderByDescending(g => g.TotalBytes)
                    .ToList();

                long maxGroupUsage = groupedSessionList.Count > 0 ? groupedSessionList.Max(g => Math.Max(0, g.TotalBytes)) : 0;

                foreach (var group in groupedSessionList)
                {
                    NetworkSessions.Add(new HistoricalSessionViewModel
                    {
                        Id = group.FirstSession.Id,
                        NetworkName = group.DisplayName,
                        InterfaceName = group.FirstSession.InterfaceName,
                        ConnectionType = group.FirstSession.ConnectionType,
                        StartTime = group.StartTime,
                        EndTime = group.EndTime,
                        BytesDownloaded = group.BytesDownloaded,
                        BytesUploaded = group.BytesUploaded,
                        DisplayIndex = NetworkSessions.Count,
                        RelativeUsagePercent = maxGroupUsage > 0
                            ? Math.Max((double)Math.Max(0, group.TotalBytes) / maxGroupUsage * 100.0, group.TotalBytes > 0 ? 1.5 : 0.0)
                            : 0.0
                    });
                }

                if (NetworkSessions.Count == 0 && networkGrouped.Count > 0)
                {
                    foreach (var item in networkGrouped)
                    {
                        NetworkSessions.Add(new HistoricalSessionViewModel
                        {
                            Id = 0,
                            NetworkName = item.NetworkName,
                            InterfaceName = item.InterfaceName,
                            ConnectionType = item.ConnectionType,
                            StartTime = DateTime.UtcNow,
                            EndTime = null,
                            BytesDownloaded = item.BytesDownloaded,
                            BytesUploaded = item.BytesUploaded,
                            DisplayIndex = NetworkSessions.Count,
                            RelativeUsagePercent = item.RelativeUsagePercent
                        });
                    }
                }

                foreach (var item in networkGrouped)
                {
                    MonthlyNetworkSummaries.Add(new MonthlyNetworkSummaryViewModel
                    {
                        NetworkName = item.NetworkName,
                        InterfaceName = item.InterfaceName,
                        ConnectionType = item.ConnectionType,
                        BytesDownloaded = item.BytesDownloaded,
                        BytesUploaded = item.BytesUploaded,
                        DisplayIndex = item.DisplayIndex,
                        RelativeUsagePercent = item.RelativeUsagePercent
                    });
                }

                Applications.Clear();
                foreach (var a in mappedApps)
                    Applications.Add(a);

                ApplicationBreakdownItems.Clear();
                foreach (var a in mappedApps.Take(8))
                    ApplicationBreakdownItems.Add(a);

                HistoricalChartPoints.Clear();
                foreach (var p in chartPoints)
                    HistoricalChartPoints.Add(p);

                TwelveMonthChartPoints.Clear();
                foreach (var p in twelveMonthSamples)
                    TwelveMonthChartPoints.Add(p);

                ApplyFilters();

                IsDataState = totalUsage > 0 || NetworkSessions.Count > 0 || Applications.Count > 0;
                IsEmpty = !IsDataState;
                HasHistoricalGraphData = HistoricalChartPoints.Count > 0;
                HasTwelveMonthData = TwelveMonthChartPoints.Count > 0;

                if (showLoading) IsLoading = false;
            });
        }
        catch (Exception ex)
        {
            RunOnUI(() =>
            {
                ErrorMessage = $"Unable to load historical telemetry: {ex.Message}";
                IsLoading = false;
                IsEmpty = true;
                IsDataState = false;
                HasHistoricalGraphData = false;
                HasTwelveMonthData = false;
            });
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private List<HistoricalGraphSample> BuildHistoricalGraphSamples(
        List<DailyUsageRecord> dailyList,
        List<HourlyUsageRecord> hourlyList,
        DateTime start,
        DateTime end)
    {
        var chartPoints = new List<HistoricalGraphSample>();

        if (SelectedPeriod == HistoryPeriodType.Today)
        {
            // Exactly 6 four-hour buckets: 00–04, 04–08, 08–12, 12–16, 16–20, 20–24
            var hourlyDict = hourlyList.ToDictionary(h => h.Hour, h => h);
            for (int b = 0; b < 6; b++)
            {
                int startHour = b * 4;
                long bDl = 0;
                long bUl = 0;

                for (int h = startHour; h < startHour + 4; h++)
                {
                    if (hourlyDict.TryGetValue(h, out var rec))
                    {
                        bDl += rec.BytesDownloaded;
                        bUl += rec.BytesUploaded;
                    }
                }

                string bucketLabel = $"{startHour:D2}–{startHour + 4:D2}";
                chartPoints.Add(new HistoricalGraphSample
                {
                    Timestamp = start.Date.AddHours(startHour),
                    Label = bucketLabel,
                    FullTitle = $"Today, {startHour:D2}:00–{startHour + 4:D2}:00",
                    DownloadBytes = bDl,
                    UploadBytes = bUl
                });
            }
        }
        else if (SelectedPeriod == HistoryPeriodType.Last7Days)
        {
            // Exactly 7 calendar days normalized to Monday -> Sunday order
            var dailyDict = dailyList.ToDictionary(d => d.Day.ToUniversalTime().Date, d => d);
            for (int i = 0; i < 7; i++)
            {
                var day = start.Date.AddDays(i);
                dailyDict.TryGetValue(day.ToUniversalTime().Date, out var rec);
                chartPoints.Add(new HistoricalGraphSample
                {
                    Timestamp = day,
                    Label = day.ToString("ddd", CultureInfo.InvariantCulture),
                    FullTitle = day.ToString("dddd, MMM d", CultureInfo.InvariantCulture),
                    DownloadBytes = rec?.BytesDownloaded ?? 0,
                    UploadBytes = rec?.BytesUploaded ?? 0
                });
            }
        }
        else // Month
        {
            int targetYear = SelectedMonth != null ? SelectedMonth.Year : start.Year;
            int targetMonth = SelectedMonth != null ? SelectedMonth.Month : start.Month;
            int daysInMonth = DateTime.DaysInMonth(targetYear, targetMonth);
            var dailyDict = dailyList.ToDictionary(d => d.Day.Date, d => d);

            for (int d = 1; d <= daysInMonth; d++)
            {
                var day = new DateTime(targetYear, targetMonth, d, 0, 0, 0, DateTimeKind.Utc);
                dailyDict.TryGetValue(day.Date, out var rec);
                chartPoints.Add(new HistoricalGraphSample
                {
                    Timestamp = day,
                    Label = d.ToString(),
                    FullTitle = day.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture),
                    DownloadBytes = rec?.BytesDownloaded ?? 0,
                    UploadBytes = rec?.BytesUploaded ?? 0
                });
            }
        }

        return chartPoints;
    }

    private void ScaleBarHeights(List<HistoricalGraphSample> chartPoints, List<HistoricalGraphSample> twelveMonthSamples)
    {
        const double Chart1ContentHeight = 300.0;
        const double Chart2ContentHeight = 280.0;

        // 1. Scale Chart #1 (Network Usage)
        double maxObserved1 = chartPoints.Count > 0 ? chartPoints.Max(p => Math.Max(p.DownloadBytes, p.UploadBytes)) : 0;
        var axis1 = CalculateCleanYAxis(maxObserved1);

        YAxisTopText = axis1.top;
        YAxisMidHighText = axis1.midHigh;
        YAxisMidText = axis1.mid;
        YAxisMidLowText = axis1.midLow;
        YAxisMinText = axis1.min;

        double canvasWidth = Math.Max(ChartWidth, 200.0);
        int count1 = chartPoints.Count;

        for (int i = 0; i < count1; i++)
        {
            var p = chartPoints[i];
            p.CanvasX = count1 == 1 ? canvasWidth / 2 : (double)i / (count1 - 1) * canvasWidth;

            double dlRatio = axis1.yMax > 0 ? Math.Clamp((double)p.DownloadBytes / axis1.yMax, 0.0, 1.0) : 0.0;
            double ulRatio = axis1.yMax > 0 ? Math.Clamp((double)p.UploadBytes / axis1.yMax, 0.0, 1.0) : 0.0;

            p.DownloadBarHeight = p.DownloadBytes > 0 ? Math.Max(dlRatio * Chart1ContentHeight, 3.0) : 0.0;
            p.UploadBarHeight = p.UploadBytes > 0 ? Math.Max(ulRatio * Chart1ContentHeight, 3.0) : 0.0;
        }

        // 2. Scale Chart #2 (Monthly Usage Breakdown - 12 Months)
        double maxObserved2 = twelveMonthSamples.Count > 0 ? twelveMonthSamples.Max(p => Math.Max(p.DownloadBytes, p.UploadBytes)) : 0;
        var axis2 = CalculateCleanYAxis(maxObserved2);

        TwelveMonthYAxisTopText = axis2.top;
        TwelveMonthYAxisMidHighText = axis2.midHigh;
        TwelveMonthYAxisMidText = axis2.mid;
        TwelveMonthYAxisMidLowText = axis2.midLow;
        TwelveMonthYAxisMinText = axis2.min;

        int count2 = twelveMonthSamples.Count;
        for (int i = 0; i < count2; i++)
        {
            var p = twelveMonthSamples[i];
            p.CanvasX = count2 == 1 ? canvasWidth / 2 : (double)i / (count2 - 1) * canvasWidth;

            double dlRatio = axis2.yMax > 0 ? Math.Clamp((double)p.DownloadBytes / axis2.yMax, 0.0, 1.0) : 0.0;
            double ulRatio = axis2.yMax > 0 ? Math.Clamp((double)p.UploadBytes / axis2.yMax, 0.0, 1.0) : 0.0;

            p.DownloadBarHeight = p.DownloadBytes > 0 ? Math.Max(dlRatio * Chart2ContentHeight, 3.0) : 0.0;
            p.UploadBarHeight = p.UploadBytes > 0 ? Math.Max(ulRatio * Chart2ContentHeight, 3.0) : 0.0;
        }
    }

    public void UpdateChartDimensions(double width, double height)
    {
        if (width > 50 && height > 50)
        {
            ChartWidth = width;
            ChartHeight = height;
            ScaleBarHeights(HistoricalChartPoints.ToList(), TwelveMonthChartPoints.ToList());
        }
    }

    public void UpdateHoverPosition(double mouseX, double mouseY)
    {
        if (HistoricalChartPoints.Count == 0)
        {
            IsHoverActive = false;
            return;
        }

        var closest = HistoricalChartPoints.OrderBy(p => Math.Abs(p.CanvasX - mouseX)).FirstOrDefault();
        if (closest != null)
        {
            IsHoverActive = true;
            HoverX = closest.CanvasX;
            HoverY = 300.0 - Math.Max(closest.DownloadBarHeight, closest.UploadBarHeight) - 15.0;
            HoverY = Math.Clamp(HoverY, 10.0, 240.0);

            HoverTimestampText = !string.IsNullOrEmpty(closest.FullTitle) ? closest.FullTitle : closest.Label;
            HoverDownloadText = closest.DownloadFormatted;
            HoverUploadText = closest.UploadFormatted;
            HoverTotalText = closest.TotalFormatted;
        }
    }

    public void ClearHover()
    {
        IsHoverActive = false;
    }

    public void UpdateTwelveMonthHoverPosition(double mouseX, double mouseY)
    {
        if (TwelveMonthChartPoints.Count == 0)
        {
            IsTwelveMonthHoverActive = false;
            return;
        }

        var closest = TwelveMonthChartPoints.OrderBy(p => Math.Abs(p.CanvasX - mouseX)).FirstOrDefault();
        if (closest != null)
        {
            IsTwelveMonthHoverActive = true;
            TwelveMonthHoverX = closest.CanvasX;
            TwelveMonthHoverY = 280.0 - Math.Max(closest.DownloadBarHeight, closest.UploadBarHeight) - 15.0;
            TwelveMonthHoverY = Math.Clamp(TwelveMonthHoverY, 10.0, 220.0);

            TwelveMonthHoverTimestampText = !string.IsNullOrEmpty(closest.FullTitle) ? closest.FullTitle : closest.Label;
            TwelveMonthHoverDownloadText = closest.DownloadFormatted;
            TwelveMonthHoverUploadText = closest.UploadFormatted;
            TwelveMonthHoverTotalText = closest.TotalFormatted;
        }
    }

    public void ClearTwelveMonthHover()
    {
        IsTwelveMonthHoverActive = false;
    }

    private static (double yMax, string top, string midHigh, string mid, string midLow, string min) CalculateCleanYAxis(double maxObserved)
    {
        if (maxObserved <= 0)
        {
            return (1024 * 1024, "1.0 MB", "750 KB", "500 KB", "250 KB", "0 B");
        }

        // Determine unit
        double unitBytes;
        string unitSuffix;

        if (maxObserved < 1024)
        {
            unitBytes = 1.0;
            unitSuffix = "B";
        }
        else if (maxObserved < 1024 * 1024)
        {
            unitBytes = 1024.0;
            unitSuffix = "KB";
        }
        else if (maxObserved < 1024.0 * 1024 * 1024)
        {
            unitBytes = 1024.0 * 1024.0;
            unitSuffix = "MB";
        }
        else if (maxObserved < 1024.0 * 1024 * 1024 * 1024)
        {
            unitBytes = 1024.0 * 1024.0 * 1024.0;
            unitSuffix = "GB";
        }
        else
        {
            unitBytes = 1024.0 * 1024.0 * 1024.0 * 1024.0;
            unitSuffix = "TB";
        }

        double valInUnit = maxObserved / unitBytes;

        // Clean candidate steps where 4 divisions (100%, 75%, 50%, 25%) are clean
        double[] candidateSteps = [
            1, 2, 4, 5, 8, 10, 12, 16, 20, 24, 28, 32, 40, 50, 60, 80, 100,
            120, 160, 200, 240, 280, 320, 400, 500, 600, 800, 1000
        ];

        // Find smallest clean step where maxObserved occupies <= 90% (i.e. >= 10% headroom)
        double chosenStep = 0;
        foreach (var s in candidateSteps)
        {
            if (s >= valInUnit && (valInUnit / s) <= 0.90)
            {
                chosenStep = s;
                break;
            }
        }

        if (chosenStep == 0)
        {
            // If valInUnit is greater than 1000 or between steps with tight margins:
            double rawStep = Math.Ceiling(valInUnit / 0.88);
            chosenStep = Math.Ceiling(rawStep / 4.0) * 4.0;
        }

        double yMax = chosenStep * unitBytes;

        // Format 5 clean levels
        string FormatLevel(double fraction)
        {
            double levelVal = chosenStep * fraction;
            if (Math.Abs(levelVal) < 0.001) return "0 B";

            if (Math.Abs(levelVal - Math.Round(levelVal)) < 0.001)
            {
                return $"{(long)Math.Round(levelVal)} {unitSuffix}";
            }
            return $"{levelVal:0.##} {unitSuffix}";
        }

        return (
            yMax,
            FormatLevel(1.0),
            FormatLevel(0.75),
            FormatLevel(0.50),
            FormatLevel(0.25),
            "0 B"
        );
    }

    // ── Search & Sorting Filters ───────────────────────────────────────────────

    private void ApplyFilters()
    {
        // 1. Filter Network Sessions
        var sessionQuery = NetworkSessions.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string query = SearchText.Trim();
            sessionQuery = sessionQuery.Where(s =>
                s.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.SubtitleText.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        var sessionList = sessionQuery.ToList();
        var targetSessionKeys = sessionList.Select(s => s.DisplayName + "|" + s.SubtitleText).ToHashSet();
        for (int i = FilteredNetworkSessions.Count - 1; i >= 0; i--)
        {
            var key = FilteredNetworkSessions[i].DisplayName + "|" + FilteredNetworkSessions[i].SubtitleText;
            if (!targetSessionKeys.Contains(key))
            {
                FilteredNetworkSessions.RemoveAt(i);
            }
        }

        for (int i = 0; i < sessionList.Count; i++)
        {
            var item = sessionList[i];
            var key = item.DisplayName + "|" + item.SubtitleText;
            int existingIndex = -1;
            for (int j = 0; j < FilteredNetworkSessions.Count; j++)
            {
                if ((FilteredNetworkSessions[j].DisplayName + "|" + FilteredNetworkSessions[j].SubtitleText).Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    existingIndex = j;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                FilteredNetworkSessions[existingIndex].UpdateFrom(item);
                if (existingIndex != i && i < FilteredNetworkSessions.Count)
                {
                    FilteredNetworkSessions.Move(existingIndex, i);
                }
            }
            else
            {
                FilteredNetworkSessions.Insert(Math.Min(i, FilteredNetworkSessions.Count), item);
            }
        }

        HasNetworkUsage = NetworkUsageItems.Count > 0;
        HasNetworkSessions = HasNetworkUsage;

        // 2. Filter & Sort Applications
        var appQuery = Applications.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string query = SearchText.Trim();
            appQuery = appQuery.Where(a =>
                a.EffectiveDisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                a.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        appQuery = SelectedSortOption switch
        {
            "Download (Desc)" => appQuery.OrderByDescending(a => a.DownloadBytes),
            "Upload (Desc)" => appQuery.OrderByDescending(a => a.UploadBytes),
            "Share (Desc)" => appQuery.OrderByDescending(a => a.PercentageOfTotal),
            _ => appQuery.OrderByDescending(a => a.TotalBytes)
        };

        var appList = appQuery.ToList();
        for (int i = 0; i < appList.Count; i++)
        {
            appList[i].DisplayIndex = i;
        }

        // In-place collection synchronization to keep visual elements stable and prevent hover flickering
        var targetAppNames = appList.Select(a => a.ProcessName.Trim().ToLowerInvariant()).ToHashSet();
        for (int i = FilteredApplications.Count - 1; i >= 0; i--)
        {
            var name = FilteredApplications[i].ProcessName.Trim().ToLowerInvariant();
            if (!targetAppNames.Contains(name))
            {
                FilteredApplications.RemoveAt(i);
            }
        }

        for (int i = 0; i < appList.Count; i++)
        {
            var item = appList[i];
            var name = item.ProcessName.Trim().ToLowerInvariant();
            int existingIndex = -1;
            for (int j = 0; j < FilteredApplications.Count; j++)
            {
                if (FilteredApplications[j].ProcessName.Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    existingIndex = j;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                FilteredApplications[existingIndex].UpdateFrom(item);
                if (existingIndex != i && i < FilteredApplications.Count)
                {
                    FilteredApplications.Move(existingIndex, i);
                }
            }
            else
            {
                FilteredApplications.Insert(Math.Min(i, FilteredApplications.Count), item);
            }
        }

        HasApplications = FilteredApplications.Count > 0;

        long totalAppDl = (string.IsNullOrWhiteSpace(SearchText) ? Applications : FilteredApplications).Sum(a => a.DownloadBytes);
        long totalAppUl = (string.IsNullOrWhiteSpace(SearchText) ? Applications : FilteredApplications).Sum(a => a.UploadBytes);
        long totalAppUsage = totalAppDl + totalAppUl;
        TotalApplicationUsageText = ByteFormatter.FormatBytes(totalAppUsage);
        TotalApplicationDownloadText = ByteFormatter.FormatBytes(totalAppDl);
        TotalApplicationUploadText = ByteFormatter.FormatBytes(totalAppUl);

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            ApplicationCountText = $"Showing {FilteredApplications.Count} of {Applications.Count} applications";
        }
        else
        {
            ApplicationCountText = Applications.Count == 1 ? "1 application" : $"{Applications.Count} applications";
        }
    }

    // ── IDisposable ────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _networkMonitorWorker.NetworkUsageUpdated -= OnNetworkUsageUpdated;
        }
    }
}
