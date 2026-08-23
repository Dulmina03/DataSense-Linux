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

public partial class ApplicationAnalyticsViewModel : ViewModelBase, IDisposable
{
    private readonly IApplicationAnalyticsService _applicationAnalyticsService;
    private readonly ProcessNetworkMonitorWorker _processMonitorWorker;
    private readonly IApplicationIntelligenceService _appIntelligenceService;
    private readonly IProcessNetworkIntelligenceService _processNetworkIntelligenceService;
    
    private bool _disposed;
    
    public override string Title => IsDetailActive ? "Application Detail" : "Application Analytics";

    public ApplicationAnalyticsViewModel(
        IApplicationAnalyticsService applicationAnalyticsService,
        ProcessNetworkMonitorWorker processMonitorWorker,
        IApplicationIntelligenceService appIntelligenceService,
        IProcessNetworkIntelligenceService processNetworkIntelligenceService)
    {
        _applicationAnalyticsService = applicationAnalyticsService ?? throw new ArgumentNullException(nameof(applicationAnalyticsService));
        _processMonitorWorker        = processMonitorWorker        ?? throw new ArgumentNullException(nameof(processMonitorWorker));
        _appIntelligenceService      = appIntelligenceService      ?? throw new ArgumentNullException(nameof(appIntelligenceService));
        _processNetworkIntelligenceService = processNetworkIntelligenceService ?? throw new ArgumentNullException(nameof(processNetworkIntelligenceService));

        _processMonitorWorker.LiveTrafficUpdated += OnLiveTrafficUpdated;
    }

    public void Initialize(string processName)
    {
        _ = InitializeAsync(processName, 0, 0);
    }

    public void Initialize(string processName, int pid, long startTimeTicks)
    {
        _ = InitializeAsync(processName, pid, startTimeTicks);
    }

    private async Task InitializeAsync(string processName, int pid, long startTimeTicks)
    {
        Dispatcher.UIThread.Post(() => IsLoading = true);
        
        if (string.IsNullOrEmpty(processName))
        {
            Dispatcher.UIThread.Post(() => {
                IsDetailActive = false;
                SelectedProcess = null;
            });
            await LoadMasterAnalyticsAsync(showLoading: true);
        }
        else
        {
            if (pid == 0 || startTimeTicks == 0)
            {
                var summaries = await _applicationAnalyticsService.GetApplicationSummariesAsync(SelectedPeriod, forceRefresh: true);
                var match = summaries
                    .Where(p => p.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(p => p.TotalBytes)
                    .FirstOrDefault();
                
                if (match != null)
                {
                    pid = match.Pid;
                    startTimeTicks = match.StartTime.Ticks;
                }
            }
            
            if (pid > 0 && startTimeTicks > 0)
            {
                Dispatcher.UIThread.Post(() => IsDetailActive = true);
                await LoadDetailAnalyticsAsync(processName, pid, startTimeTicks, showLoading: true);
            }
            else
            {
                Dispatcher.UIThread.Post(() => {
                    IsDetailActive = false;
                    SelectedProcess = null;
                });
                await LoadMasterAnalyticsAsync(showLoading: true);
            }
        }
    }

    [ObservableProperty] private bool _isDetailActive = false;
    [ObservableProperty] private ApplicationAnalyticsSummary? _selectedProcess;

    // Master View Lists
    public ObservableCollection<ApplicationAnalyticsSummary> TopOverallProcesses { get; } = new();
    public ObservableCollection<ApplicationAnalyticsSummary> TopDownloadProcesses { get; } = new();
    public ObservableCollection<ApplicationAnalyticsSummary> TopUploadProcesses { get; } = new();
    public ObservableCollection<ApplicationAnalyticsSummary> FilteredProcesses { get; } = new();

    // Master View Filtering and Search
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private string _selectedFilter = "All";
    
    [ObservableProperty] private string _sortColumn = "Total";
    [ObservableProperty] private bool _sortAscending = false;

    partial void OnSearchQueryChanged(string value) => ApplyFiltersAndSort();
    partial void OnSelectedFilterChanged(string value) => ApplyFiltersAndSort();
    partial void OnSortColumnChanged(string value) => ApplyFiltersAndSort();
    partial void OnSortAscendingChanged(bool value) => ApplyFiltersAndSort();

    private List<ApplicationAnalyticsSummary> _allSummaries = new();

    [ObservableProperty] private string _processNameText = "—";
    [ObservableProperty] private string _applicationNameText = "—";
    [ObservableProperty] private string _executablePathText = "—";
    [ObservableProperty] private string _userNameText = "—";
    [ObservableProperty] private string _pidText = "—";
    [ObservableProperty] private string _dataSourceText = "Source: Linux nethogs";
    [ObservableProperty] private string _monitoringStateText = "Active";
    
    // Live Status
    [ObservableProperty] private string _liveDownloadSpeed = "—";
    [ObservableProperty] private string _liveUploadSpeed = "—";
    [ObservableProperty] private bool _isCurrentlyActive = false;

    // Period Selection
    [ObservableProperty] private AppAnalyticsPeriod _selectedPeriod = AppAnalyticsPeriod.Last7Days;

    // Summary Cards
    [ObservableProperty] private string _periodTotalDownloadedText = "—";
    [ObservableProperty] private string _periodTotalUploadedText   = "—";
    [ObservableProperty] private string _periodTotalUsageText      = "—";
    [ObservableProperty] private string _periodPercentageShareText = "—";
    
    // Activity
    [ObservableProperty] private string _firstActiveText = "—";
    [ObservableProperty] private string _lastActiveText = "—";
    [ObservableProperty] private string _daysUsedText = "—";

    // Trends
    [ObservableProperty] private string _downloadTrendText = "—";
    [ObservableProperty] private string _uploadTrendText = "—";
    [ObservableProperty] private string _combinedTrendText = "—";

    // Chart Layout Constants
    public const double ChartHeight = 160.0;
    private const double BarGap = 4.0;
    
    [ObservableProperty] private double _chartWidth = 560.0;
    
    // Charts
    public ObservableCollection<DailyChartBarViewModel> PeriodChartItems { get; } = new();
    public ObservableCollection<HourlyChartBarViewModel> HourlyChartItems { get; } = new();
    
    [ObservableProperty] private bool _isHourlyChart = false;
    [ObservableProperty] private bool _isChartEmpty = true;
    
    // Download vs Upload Ratio
    [ObservableProperty] private string _downloadRatioText = "—";
    [ObservableProperty] private string _uploadRatioText = "—";
    [ObservableProperty] private string _downloadActualText = "—";
    [ObservableProperty] private string _uploadActualText = "—";
    [ObservableProperty] private bool _hasPeriodData = false;
    
    [ObservableProperty] private GridLength _downloadColumnWidth = new GridLength(1, GridUnitType.Star);
    [ObservableProperty] private GridLength _uploadColumnWidth   = new GridLength(1, GridUnitType.Star);

    // History Table
    public ObservableCollection<ApplicationUsageTimelinePoint> DailyHistoryItems { get; } = new();
    [ObservableProperty] private bool _isHistoryTableEmpty = true;

    // ── Phase 11.31 Historical Intelligence fields ────────────────────────────
    [ObservableProperty] private string _sevenDayAverageText   = "—";
    [ObservableProperty] private string _thirtyDayAverageText  = "—";
    [ObservableProperty] private string _monthlyProjectionText = "—";
    [ObservableProperty] private string _peakDayText           = "—";
    [ObservableProperty] private string _peakDayBytesText      = "—";
    [ObservableProperty] private string _peakHourText          = "—";
    [ObservableProperty] private string _peakHourBytesText     = "—";
    [ObservableProperty] private string _rankText              = "—";
    [ObservableProperty] private string _trendBadgeText        = "—";
    [ObservableProperty] private string _trendBadgeColor       = "#888899";
    [ObservableProperty] private string _surgeText             = string.Empty;
    [ObservableProperty] private bool   _isUsageSurging        = false;
    [ObservableProperty] private bool   _hasSufficientHistory  = false;
    [ObservableProperty] private string _insufficientDataText  = string.Empty;
    // Comparison: Today vs Yesterday
    [ObservableProperty] private string _todayVsYesterdayText  = "—";
    // Comparison: 7-day vs prev 7-day
    [ObservableProperty] private string _sevenDayComparisonText = "—";
    [ObservableProperty] private string _sevenDayComparisonColor = "#888899";

    // Application Intelligence & Smart Recommendations
    [ObservableProperty] private ApplicationUsageProfile? _currentProfile;
    [ObservableProperty] private ApplicationNetworkProfile? _networkProfile;
    public ObservableCollection<ApplicationRecommendation> ProcessRecommendations { get; } = new();
    [ObservableProperty] private bool _hasProcessRecommendations = false;

    // Loading State
    [ObservableProperty] private bool _isLoading = false;

    // Process-Network Intelligence
    public ObservableCollection<ProcessNetworkProfile> ProcessNetworkUsageProfiles { get; } = new();
    [ObservableProperty] private bool _isProcessNetworkUsageProfilesEmpty = true;
    [ObservableProperty] private string _mostUsedNetwork = "—";
    [ObservableProperty] private string _highestDownloadNetwork = "—";
    [ObservableProperty] private string _highestUploadNetwork = "—";
    [ObservableProperty] private string _networkTrendText = "—";

    [RelayCommand]
    private void NavigateBack()
    {
        if (IsDetailActive)
        {
            _ = InitializeAsync(string.Empty, 0, 0);
        }
        else
        {
            var mainWindowVm = App.Services?.GetService(typeof(MainWindowViewModel)) as MainWindowViewModel;
            mainWindowVm?.NavigateToDashboardCommand.Execute(null);
        }
    }

    [RelayCommand]
    private async Task SelectPeriodAsync(string periodString)
    {
        if (Enum.TryParse<AppAnalyticsPeriod>(periodString, out var period))
        {
            if (SelectedPeriod != period)
            {
                SelectedPeriod = period;
                if (IsDetailActive && SelectedProcess != null)
                {
                    await LoadDetailAnalyticsAsync(SelectedProcess.ProcessName, SelectedProcess.Pid, SelectedProcess.StartTime.Ticks, showLoading: true);
                }
                else
                {
                    await LoadMasterAnalyticsAsync(showLoading: true);
                }
            }
        }
    }

    [RelayCommand]
    private void Sort(string column)
    {
        if (SortColumn == column)
        {
            SortAscending = !SortAscending;
        }
        else
        {
            SortColumn = column;
            SortAscending = true;
        }
    }

    [RelayCommand]
    private void DrillDown(ApplicationAnalyticsSummary process)
    {
        if (process != null)
        {
            _ = InitializeAsync(process.ProcessName, process.Pid, process.StartTime.Ticks);
        }
    }

    public void UpdateChartWidth(double newWidth)
    {
        if (newWidth < 50) return;
        double rounded = Math.Floor(newWidth);
        if (Math.Abs(rounded - ChartWidth) < 10) return;
        ChartWidth = rounded;
        if (IsDetailActive && SelectedProcess != null)
        {
            _ = LoadDetailAnalyticsAsync(SelectedProcess.ProcessName, SelectedProcess.Pid, SelectedProcess.StartTime.Ticks, showLoading: false);
        }
    }

    private async Task LoadMasterAnalyticsAsync(bool showLoading)
    {
        if (showLoading)
        {
            Dispatcher.UIThread.Post(() => IsLoading = true);
        }

        try
        {
            var summaries = (await _applicationAnalyticsService.GetApplicationSummariesAsync(SelectedPeriod, forceRefresh: true)).ToList();
            _allSummaries = summaries;

            Dispatcher.UIThread.Post(() =>
            {
                TopOverallProcesses.Clear();
                foreach (var p in summaries.OrderByDescending(p => p.TotalBytes).Take(5)) TopOverallProcesses.Add(p);

                TopDownloadProcesses.Clear();
                foreach (var p in summaries.OrderByDescending(p => p.DownloadBytes).Take(5)) TopDownloadProcesses.Add(p);

                TopUploadProcesses.Clear();
                foreach (var p in summaries.OrderByDescending(p => p.UploadBytes).Take(5)) TopUploadProcesses.Add(p);

                ApplyFiltersAndSort();

                if (showLoading) IsLoading = false;
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Master analytics load failed: {ex.Message}");
            Dispatcher.UIThread.Post(() => { if (showLoading) IsLoading = false; });
        }
    }

    private void ApplyFiltersAndSort()
    {
        var filtered = _allSummaries.AsEnumerable();

        // 1. Search Query
        if (!string.IsNullOrEmpty(SearchQuery))
        {
            filtered = filtered.Where(p => 
                p.ProcessName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                p.ExecutablePath.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                p.UserName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)
            );
        }

        // 2. Selected Filter
        switch (SelectedFilter)
        {
            case "Active":
                filtered = filtered.Where(p => p.IsCurrentlyRunning);
                break;
            case "Historical":
                filtered = filtered.Where(p => !p.IsCurrentlyRunning);
                break;
            case "Download Heavy":
                filtered = filtered.Where(p => p.DownloadBytes > p.UploadBytes);
                break;
            case "Upload Heavy":
                filtered = filtered.Where(p => p.UploadBytes > p.DownloadBytes);
                break;
            case "High Usage":
                filtered = filtered.Where(p => p.TotalBytes > 1048576); // > 1MB
                break;
            case "Increasing":
                filtered = filtered.Where(p => p.CombinedTrend == "Increasing");
                break;
            case "Decreasing":
                filtered = filtered.Where(p => p.CombinedTrend == "Decreasing");
                break;
        }

        // 3. Sorting
        switch (SortColumn)
        {
            case "Name":
                filtered = SortAscending ? filtered.OrderBy(p => p.ProcessName) : filtered.OrderByDescending(p => p.ProcessName);
                break;
            case "Total":
                filtered = SortAscending ? filtered.OrderBy(p => p.TotalBytes) : filtered.OrderByDescending(p => p.TotalBytes);
                break;
            case "Percentage":
                filtered = SortAscending ? filtered.OrderBy(p => p.PercentageOfTotal) : filtered.OrderByDescending(p => p.PercentageOfTotal);
                break;
            case "Trend":
                filtered = SortAscending ? filtered.OrderBy(p => p.CombinedTrend) : filtered.OrderByDescending(p => p.CombinedTrend);
                break;
            case "Status":
                filtered = SortAscending ? filtered.OrderBy(p => p.IsCurrentlyRunning) : filtered.OrderByDescending(p => p.IsCurrentlyRunning);
                break;
            default:
                filtered = filtered.OrderByDescending(p => p.TotalBytes);
                break;
        }

        FilteredProcesses.Clear();
        foreach (var p in filtered) FilteredProcesses.Add(p);
    }

    private async Task LoadDetailAnalyticsAsync(string processName, int pid, long startTimeTicks, bool showLoading)
    {
        if (showLoading)
        {
            Dispatcher.UIThread.Post(() => IsLoading = true);
        }

        try
        {
            var summary = await _applicationAnalyticsService.GetProcessDetailAsync(processName, pid, startTimeTicks, SelectedPeriod);
            if (summary == null)
            {
                Dispatcher.UIThread.Post(() => { if (showLoading) IsLoading = false; });
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                SelectedProcess = summary;
                ProcessNameText = summary.ProcessName;
                ApplicationNameText = summary.ProcessName;
                ExecutablePathText = string.IsNullOrEmpty(summary.ExecutablePath) ? "—" : summary.ExecutablePath;
                UserNameText = string.IsNullOrEmpty(summary.UserName) ? "—" : summary.UserName;
                PidText = summary.Pid.ToString();
                DataSourceText = $"Source: Linux {summary.DataSource.ToLowerInvariant()}";
                MonitoringStateText = summary.IsCurrentlyRunning ? "Active" : "Exited";

                PeriodTotalDownloadedText = ByteFormatter.FormatBytes(summary.DownloadBytes);
                PeriodTotalUploadedText   = ByteFormatter.FormatBytes(summary.UploadBytes);
                PeriodTotalUsageText      = ByteFormatter.FormatBytes(summary.TotalBytes);
                PeriodPercentageShareText = $"{summary.PercentageOfTotal:F1}%";

                FirstActiveText = summary.FirstSeen?.ToString("MMM d, HH:mm") ?? "—";
                LastActiveText  = summary.LastSeen?.ToString("MMM d, HH:mm") ?? "—";
                DaysUsedText    = summary.ActiveDaysCount > 0 ? summary.ActiveDaysCount.ToString() : "—";

                DownloadTrendText = summary.DownloadTrendPercentage.HasValue 
                    ? $"{summary.DownloadTrend} ({summary.DownloadTrendPercentage.Value:F1}%)" 
                    : "Insufficient historical baseline";

                UploadTrendText = summary.UploadTrendPercentage.HasValue 
                    ? $"{summary.UploadTrend} ({summary.UploadTrendPercentage.Value:F1}%)" 
                    : "Insufficient historical baseline";

                CombinedTrendText = summary.CombinedTrendPercentage.HasValue 
                    ? $"{summary.CombinedTrend} ({summary.CombinedTrendPercentage.Value:F1}%)" 
                    : "Insufficient historical baseline";
            });

            // Build Charts
            var timeline = (await _applicationAnalyticsService.GetProcessTimelineAsync(processName, pid, startTimeTicks, SelectedPeriod)).ToList();
            
            if (SelectedPeriod == AppAnalyticsPeriod.Today)
            {
                var hourlyItems = BuildHourlyChartItems(timeline, ChartWidth);
                Dispatcher.UIThread.Post(() =>
                {
                    IsHourlyChart = true;
                    HourlyChartItems.Clear();
                    foreach (var item in hourlyItems) HourlyChartItems.Add(item);
                    IsChartEmpty = !hourlyItems.Any(i => i.HasData);

                    DailyHistoryItems.Clear();
                    IsHistoryTableEmpty = true;
                });
            }
            else
            {
                var dailyItems = BuildChartItems(timeline, ChartWidth);
                Dispatcher.UIThread.Post(() =>
                {
                    IsHourlyChart = false;
                    PeriodChartItems.Clear();
                    foreach (var item in dailyItems) PeriodChartItems.Add(item);
                    IsChartEmpty = !dailyItems.Any(i => i.HasData);

                    DailyHistoryItems.Clear();
                    foreach (var day in timeline)
                    {
                        if (day.TotalBytes > 0)
                        {
                            DailyHistoryItems.Add(day);
                        }
                    }
                    IsHistoryTableEmpty = DailyHistoryItems.Count == 0;
                });
            }

            // Ratio calculation
            double dlRatio = summary.TotalBytes > 0 ? (double)summary.DownloadBytes / summary.TotalBytes : 0.5;
            double ulRatio = summary.TotalBytes > 0 ? (double)summary.UploadBytes / summary.TotalBytes : 0.5;

            Dispatcher.UIThread.Post(() =>
            {
                HasPeriodData = summary.TotalBytes > 0;
                DownloadRatioText = HasPeriodData ? $"{dlRatio * 100:F0}%" : "—";
                UploadRatioText   = HasPeriodData ? $"{ulRatio * 100:F0}%" : "—";
                DownloadActualText = HasPeriodData ? ByteFormatter.FormatBytes(summary.DownloadBytes) : "—";
                UploadActualText   = HasPeriodData ? ByteFormatter.FormatBytes(summary.UploadBytes) : "—";

                if (HasPeriodData && summary.DownloadBytes > 0 && summary.UploadBytes > 0)
                {
                    DownloadColumnWidth = new GridLength(Math.Max(dlRatio, 0.05), GridUnitType.Star);
                    UploadColumnWidth   = new GridLength(Math.Max(ulRatio, 0.05), GridUnitType.Star);
                }
                else
                {
                    DownloadColumnWidth = new GridLength(1, GridUnitType.Star);
                    UploadColumnWidth   = new GridLength(1, GridUnitType.Star);
                }
            });

            // Application Intelligence & Smart Recommendations
            var profile = await _appIntelligenceService.GetApplicationProfileAsync(processName);
            var netProfile = await _appIntelligenceService.GetApplicationNetworkProfileAsync(processName, pid, startTimeTicks);
            var recs    = (await _appIntelligenceService.GetProcessRecommendationsAsync(processName)).ToList();
            var processProfiles = (await _processNetworkIntelligenceService.GetProcessNetworkUsageAsync(processName, pid, startTimeTicks)).ToList();

            // ── Phase 11.31 Historical Profile ───────────────────────────────
            ApplicationHistoricalProfile? histProfile = null;
            IEnumerable<ApplicationHistoricalProfile>? allProfiles = null;
            try
            {
                histProfile = await _applicationAnalyticsService.GetApplicationProfileAsync(processName, pid, startTimeTicks);
                allProfiles = await _applicationAnalyticsService.GetApplicationProfilesAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Historical profile load failed: {ex.Message}");
            }

            Dispatcher.UIThread.Post(() =>
            {
                CurrentProfile = profile;
                NetworkProfile = netProfile;
                ProcessRecommendations.Clear();
                foreach (var rec in recs) ProcessRecommendations.Add(rec);
                HasProcessRecommendations = ProcessRecommendations.Count > 0;

                ProcessNetworkUsageProfiles.Clear();
                foreach (var prof in processProfiles) ProcessNetworkUsageProfiles.Add(prof);
                IsProcessNetworkUsageProfilesEmpty = ProcessNetworkUsageProfiles.Count == 0;

                if (processProfiles.Any())
                {
                    var mostUsed = processProfiles.OrderByDescending(p => p.TotalBytes).First();
                    MostUsedNetwork = $"{mostUsed.NetworkName} ({ByteFormatter.FormatBytes(mostUsed.TotalBytes)})";

                    var maxDl = processProfiles.OrderByDescending(p => p.DownloadBytes).First();
                    HighestDownloadNetwork = $"{maxDl.NetworkName} ({ByteFormatter.FormatBytes(maxDl.DownloadBytes)})";

                    var maxUl = processProfiles.OrderByDescending(p => p.UploadBytes).First();
                    HighestUploadNetwork = $"{maxUl.NetworkName} ({ByteFormatter.FormatBytes(maxUl.UploadBytes)})";

                    var overallTrend = summary.CombinedTrend;
                    NetworkTrendText = $"{overallTrend} (Active on {processProfiles.Count} networks)";
                }
                else
                {
                    MostUsedNetwork = "—";
                    HighestDownloadNetwork = "—";
                    HighestUploadNetwork = "—";
                    NetworkTrendText = "No network telemetry recorded";
                }

                // ── Populate historical intelligence fields ───────────────────
                if (histProfile != null)
                {
                    HasSufficientHistory  = histProfile.HasSufficientData;
                    InsufficientDataText  = histProfile.HasSufficientData ? string.Empty
                        : "Insufficient historical data (< 3 active days).";

                    SevenDayAverageText   = histProfile.SevenDayAverageBytes.HasValue
                        ? ByteFormatter.FormatBytes((long)histProfile.SevenDayAverageBytes.Value) + " / day"
                        : "Insufficient Data";

                    ThirtyDayAverageText  = histProfile.ThirtyDayAverageBytes.HasValue
                        ? ByteFormatter.FormatBytes((long)histProfile.ThirtyDayAverageBytes.Value) + " / day"
                        : "Insufficient Data";

                    MonthlyProjectionText = histProfile.MonthlyProjectedBytes.HasValue
                        ? ByteFormatter.FormatBytes(histProfile.MonthlyProjectedBytes.Value)
                        : "Insufficient Data";

                    PeakDayText       = histProfile.PeakDay?.ToString("MMM d, yyyy") ?? "—";
                    PeakDayBytesText  = histProfile.PeakDayBytes > 0 ? ByteFormatter.FormatBytes(histProfile.PeakDayBytes) : "—";
                    PeakHourText      = histProfile.PeakHour.HasValue ? $"{histProfile.PeakHour:D2}:00 UTC" : "—";
                    PeakHourBytesText = histProfile.PeakHourBytes > 0 ? ByteFormatter.FormatBytes(histProfile.PeakHourBytes) : "—";

                    // Trend badge
                    (TrendBadgeText, TrendBadgeColor) = histProfile.TrendState switch
                    {
                        "Increasing"       => (histProfile.TrendPercentage.HasValue ? $"↗ +{histProfile.TrendPercentage.Value:F1}%" : "↗ Increasing", "#FF9800"),
                        "Decreasing"       => (histProfile.TrendPercentage.HasValue ? $"↘ {histProfile.TrendPercentage.Value:F1}%"  : "↘ Decreasing", "#00E676"),
                        "Stable"           => ("→ Stable", "#888899"),
                        _                  => ("— Insufficient Data", "#555577")
                    };

                    // Surge
                    IsUsageSurging = histProfile.IsUsageSurging;
                    SurgeText = histProfile.IsUsageSurging && histProfile.SurgePercentage.HasValue
                        ? $"⚠ Surge: {histProfile.SurgePercentage.Value:F0}% above baseline"
                        : string.Empty;

                    // Rank
                    if (allProfiles != null)
                    {
                        var ranked  = allProfiles.OrderByDescending(p => p.TotalBytes).ToList();
                        int rankIdx = ranked.FindIndex(p => p.ProcessName == processName && p.Pid == pid && p.StartTimeTicks == startTimeTicks);
                        RankText = rankIdx >= 0
                            ? $"#{rankIdx + 1} of {ranked.Count} applications"
                            : "—";
                    }

                    // 7-day vs prev-7-day comparison text
                    if (histProfile.TrendPercentage.HasValue)
                    {
                        double pct = histProfile.TrendPercentage.Value;
                        string arrow = pct > 0 ? "↑" : pct < 0 ? "↓" : "→";
                        SevenDayComparisonText  = $"{arrow} {Math.Abs(pct):F1}% vs previous 7 days";
                        SevenDayComparisonColor = pct > 10 ? "#FF9800" : pct < -10 ? "#00E676" : "#888899";
                    }
                    else
                    {
                        SevenDayComparisonText  = "Insufficient history for comparison";
                        SevenDayComparisonColor = "#555577";
                    }

                    // Today vs Yesterday
                    if (histProfile.TodayBytes > 0 || histProfile.YesterdayBytes > 0)
                    {
                        if (histProfile.YesterdayBytes > 0)
                        {
                            double diff = (double)(histProfile.TodayBytes - histProfile.YesterdayBytes) / histProfile.YesterdayBytes * 100.0;
                            string arrow = diff > 0 ? "↑" : diff < 0 ? "↓" : "→";
                            TodayVsYesterdayText = $"{arrow} {Math.Abs(diff):F1}% vs yesterday";
                        }
                        else
                        {
                            TodayVsYesterdayText = histProfile.TodayBytes > 0 ? "New activity today" : "No activity today";
                        }
                    }
                    else
                    {
                        TodayVsYesterdayText = "No data";
                    }
                }
                else
                {
                    // histProfile null — reset all fields
                    HasSufficientHistory  = false;
                    InsufficientDataText  = "Historical data unavailable.";
                    SevenDayAverageText   = "Unavailable";
                    ThirtyDayAverageText  = "Unavailable";
                    MonthlyProjectionText = "Unavailable";
                    PeakDayText           = "—";
                    PeakDayBytesText      = "—";
                    PeakHourText          = "—";
                    PeakHourBytesText     = "—";
                    TrendBadgeText        = "— Insufficient Data";
                    TrendBadgeColor       = "#555577";
                    RankText              = "—";
                    SurgeText             = string.Empty;
                    IsUsageSurging        = false;
                    SevenDayComparisonText  = "Insufficient history";
                    SevenDayComparisonColor = "#555577";
                    TodayVsYesterdayText    = "—";
                }

                if (showLoading) IsLoading = false;
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"App detail analytics failed: {ex.Message}");
            Dispatcher.UIThread.Post(() => { if (showLoading) IsLoading = false; });
        }
    }

    private void OnLiveTrafficUpdated(IEnumerable<ProcessNetworkUsage> currentBatch)
    {
        if (!IsDetailActive || SelectedProcess == null) return;
        
        var active = currentBatch.FirstOrDefault(p => p.ProcessIdentifier.Equals(SelectedProcess.ProcessName, StringComparison.OrdinalIgnoreCase) && p.Pid == SelectedProcess.Pid);
        Dispatcher.UIThread.Post(() =>
        {
            if (active != null)
            {
                if (!string.IsNullOrEmpty(active.ExecutablePath)) ExecutablePathText = active.ExecutablePath;
                if (!string.IsNullOrEmpty(active.User)) UserNameText = active.User;
                if (active.Pid > 0) PidText = active.Pid.ToString();
                if (!string.IsNullOrEmpty(active.DataSource)) DataSourceText = $"Source: Linux {active.DataSource.ToLowerInvariant()}";

                if (active.DownloadRateBytesPerSec > 0 || active.UploadRateBytesPerSec > 0)
                {
                    IsCurrentlyActive = true;
                    LiveDownloadSpeed = ByteFormatter.FormatSpeed(active.DownloadRateBytesPerSec);
                    LiveUploadSpeed = ByteFormatter.FormatSpeed(active.UploadRateBytesPerSec);
                }
                else
                {
                    IsCurrentlyActive = false;
                    LiveDownloadSpeed = "—";
                    LiveUploadSpeed = "—";
                }
            }
            else
            {
                IsCurrentlyActive = false;
                LiveDownloadSpeed = "—";
                LiveUploadSpeed = "—";
            }
        });
    }

    private static List<DailyChartBarViewModel> BuildChartItems(List<ApplicationUsageTimelinePoint> daily, double chartWidth)
    {
        if (daily.Count == 0) return new List<DailyChartBarViewModel>();
        long maxTotal = daily.Max(d => d.TotalBytes);
        if (maxTotal <= 0) maxTotal = 1;
        int count = daily.Count;
        double barWidth = (chartWidth - (count - 1) * BarGap) / Math.Max(count, 1);
        var items = new List<DailyChartBarViewModel>(count);

        for (int i = 0; i < count; i++)
        {
            var d = daily[i];
            double totalBarHeight = (double)d.TotalBytes / maxTotal * ChartHeight;
            double dlFrac = d.TotalBytes > 0 ? (double)d.DownloadBytes / d.TotalBytes : 0.5;
            double ulFrac = 1.0 - dlFrac;
            double dlBarHeight = totalBarHeight * dlFrac;
            double ulBarHeight = totalBarHeight * ulFrac;
            double dlBarY = ChartHeight - dlBarHeight;
            double ulBarY = dlBarY - ulBarHeight;
            double barX = i * (barWidth + BarGap);

            items.Add(new DailyChartBarViewModel
            {
                DayLabel        = d.Timestamp.ToString("MMM d"),
                BytesDownloaded = d.DownloadBytes,
                BytesUploaded   = d.UploadBytes,
                TotalBytes      = d.TotalBytes,
                DownloadedText  = ByteFormatter.FormatBytes(d.DownloadBytes),
                UploadedText    = ByteFormatter.FormatBytes(d.UploadBytes),
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

    private static List<HourlyChartBarViewModel> BuildHourlyChartItems(List<ApplicationUsageTimelinePoint> hourly, double chartWidth)
    {
        if (hourly.Count == 0) return new List<HourlyChartBarViewModel>();
        long maxTotal = hourly.Max(h => h.TotalBytes);
        if (maxTotal <= 0) maxTotal = 1;
        int count = hourly.Count;
        double barWidth = (chartWidth - (count - 1) * BarGap) / Math.Max(count, 1);
        var items = new List<HourlyChartBarViewModel>(count);

        for (int i = 0; i < count; i++)
        {
            var h = hourly[i];
            double totalBarHeight = (double)h.TotalBytes / maxTotal * ChartHeight;
            double dlFrac = h.TotalBytes > 0 ? (double)h.DownloadBytes / h.TotalBytes : 0.5;
            double ulFrac = 1.0 - dlFrac;
            double dlBarHeight = totalBarHeight * dlFrac;
            double ulBarHeight = totalBarHeight * ulFrac;
            double dlBarY = ChartHeight - dlBarHeight;
            double ulBarY = dlBarY - ulBarHeight;
            double barX = i * (barWidth + BarGap);

            items.Add(new HourlyChartBarViewModel
            {
                Hour            = h.Timestamp.Hour,
                BytesDownloaded = h.DownloadBytes,
                BytesUploaded   = h.UploadBytes,
                TotalBytes      = h.TotalBytes,
                DownloadedText  = ByteFormatter.FormatBytes(h.DownloadBytes),
                UploadedText    = ByteFormatter.FormatBytes(h.UploadBytes),
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
                _processMonitorWorker.LiveTrafficUpdated -= OnLiveTrafficUpdated;
            }
            _disposed = true;
        }
    }
}
