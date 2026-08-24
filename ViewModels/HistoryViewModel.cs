using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
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

public class HistoricalSessionViewModel
{
    public string NetworkName { get; init; } = string.Empty;
    public string InterfaceName { get; init; } = string.Empty;
    public string ConnectionType { get; init; } = string.Empty;
    public long BytesDownloaded { get; init; }
    public long BytesUploaded { get; init; }
    public long TotalBytes => BytesDownloaded + BytesUploaded;
    public string DownloadedText => ByteFormatter.FormatBytes(BytesDownloaded);
    public string UploadedText => ByteFormatter.FormatBytes(BytesUploaded);
    public string TotalText => ByteFormatter.FormatBytes(TotalBytes);
    public string SubtitleText => !string.IsNullOrWhiteSpace(ConnectionType) ? ConnectionType : InterfaceName;
    public string DisplayName => !string.IsNullOrWhiteSpace(NetworkName) ? NetworkName : (!string.IsNullOrWhiteSpace(InterfaceName) ? InterfaceName : "Network Session");
    public bool IsActive { get; init; }
}

public class HistoricalGraphSample
{
    public DateTime Timestamp { get; set; }
    public string Label { get; set; } = string.Empty;
    public long DownloadBytes { get; set; }
    public long UploadBytes { get; set; }
    public long TotalBytes => DownloadBytes + UploadBytes;
    public double CanvasX { get; set; }
    public double DownloadY { get; set; }
    public double UploadY { get; set; }
    public string DownloadFormatted => ByteFormatter.FormatBytes(DownloadBytes);
    public string UploadFormatted => ByteFormatter.FormatBytes(UploadBytes);
    public string TotalFormatted => ByteFormatter.FormatBytes(TotalBytes);
}

public partial class HistoryViewModel : ViewModelBase, IDisposable
{
    private readonly INetworkUsageRepository _repository;
    private readonly IHistoricalAnalyticsService _historicalService;
    private readonly IApplicationAnalyticsService _appAnalyticsService;
    private readonly IAppIconService _appIconService;
    private readonly IApplicationChartColorProvider _colorProvider;
    private readonly INetworkMonitorWorker _networkMonitorWorker;

    private bool _initialising = true;
    private bool _disposed;
    private int _tickCount = 4;

    public override string Title => "Usage History";

    public HistoryViewModel(
        INetworkUsageRepository repository,
        IHistoricalAnalyticsService historicalService,
        IApplicationAnalyticsService appAnalyticsService,
        IAppIconService appIconService,
        IApplicationChartColorProvider colorProvider,
        INetworkMonitorWorker networkMonitorWorker)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _historicalService = historicalService ?? throw new ArgumentNullException(nameof(historicalService));
        _appAnalyticsService = appAnalyticsService ?? throw new ArgumentNullException(nameof(appAnalyticsService));
        _appIconService = appIconService ?? throw new ArgumentNullException(nameof(appIconService));
        _colorProvider = colorProvider ?? throw new ArgumentNullException(nameof(colorProvider));
        _networkMonitorWorker = networkMonitorWorker ?? throw new ArgumentNullException(nameof(networkMonitorWorker));

        _selectedPeriod = HistoryPeriodType.Last7Days;
        _selectedInterface = "All";
        _selectedSortOption = "Total (Desc)";
        _initialising = false;

        _networkMonitorWorker.NetworkUsageUpdated += OnNetworkUsageUpdated;

        _ = InitializeAsync();
    }

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

    public ObservableCollection<MonthSelectItem> AvailableMonths { get; } = new();
    public ObservableCollection<string> Interfaces { get; } = new();

    public string[] SortOptions { get; } = ["Total (Desc)", "Download (Desc)", "Upload (Desc)", "Share (Desc)"];

    public List<HistoricalGraphSample> HistoricalChartPoints { get; } = new();

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

    // ── Historical Graph State ─────────────────────────────────────────────────

    [ObservableProperty] private bool _hasHistoricalGraphData;
    [ObservableProperty] private Geometry? _historicalDownloadLineGeometry;
    [ObservableProperty] private Geometry? _historicalDownloadAreaGeometry;
    [ObservableProperty] private Geometry? _historicalUploadLineGeometry;
    [ObservableProperty] private Geometry? _historicalUploadAreaGeometry;

    [ObservableProperty] private string _yAxisTopText = "1 MB";
    [ObservableProperty] private string _yAxisMidHighText = "750 KB";
    [ObservableProperty] private string _yAxisMidText = "500 KB";
    [ObservableProperty] private string _yAxisMidLowText = "250 KB";
    [ObservableProperty] private string _yAxisMinText = "0 B";

    [ObservableProperty] private string _xAxisLabel0 = "";
    [ObservableProperty] private string _xAxisLabel1 = "";
    [ObservableProperty] private string _xAxisLabel2 = "";
    [ObservableProperty] private string _xAxisLabel3 = "";
    [ObservableProperty] private string _xAxisLabel4 = "";
    [ObservableProperty] private string _xAxisLabel5 = "";
    [ObservableProperty] private string _xAxisLabel6 = "";

    [ObservableProperty] private double _chartWidth = 900.0;
    [ObservableProperty] private double _chartHeight = 180.0;

    // Hover Tooltip
    [ObservableProperty] private bool _isHoverActive;
    [ObservableProperty] private double _hoverX;
    [ObservableProperty] private double _hoverY;
    [ObservableProperty] private string _hoverTimestampText = "";
    [ObservableProperty] private string _hoverDownloadText = "";
    [ObservableProperty] private string _hoverUploadText = "";
    [ObservableProperty] private string _hoverTotalText = "";

    // ── Panel Flags ────────────────────────────────────────────────────────────

    [ObservableProperty] private bool _hasNetworkSessions;
    [ObservableProperty] private bool _hasApplications;

    // ── Commands ───────────────────────────────────────────────────────────────

    [RelayCommand]
    public void SelectToday()
    {
        SelectedPeriod = HistoryPeriodType.Today;
    }

    [RelayCommand]
    public void SelectLast7Days()
    {
        SelectedPeriod = HistoryPeriodType.Last7Days;
    }

    [RelayCommand]
    public void SelectMonth()
    {
        SelectedPeriod = HistoryPeriodType.Month;
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

        if (!_initialising)
        {
            _ = LoadAsync(showLoading: true);
        }
    }

    partial void OnSelectedMonthChanged(MonthSelectItem? value)
    {
        if (!_initialising && SelectedPeriod == HistoryPeriodType.Month)
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
                var end = start.AddMonths(1).AddTicks(-1);
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
        _tickCount++;
        if (_tickCount >= 5)
        {
            _tickCount = 0;
            _ = LoadAsync(showLoading: false);
        }
    }

    // ── Core Async Load ────────────────────────────────────────────────────────

    public (DateTime start, DateTime end) ComputeDateRange()
    {
        var utcNow = DateTime.UtcNow;
        return SelectedPeriod switch
        {
            HistoryPeriodType.Today => (utcNow.Date, utcNow.Date.AddDays(1).AddTicks(-1)),
            HistoryPeriodType.Last7Days => (utcNow.Date.AddDays(-6), utcNow.Date.AddDays(1).AddTicks(-1)),
            HistoryPeriodType.Month =>
                SelectedMonth != null
                    ? (new DateTime(SelectedMonth.Year, SelectedMonth.Month, 1, 0, 0, 0, DateTimeKind.Utc),
                       new DateTime(SelectedMonth.Year, SelectedMonth.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1).AddTicks(-1))
                    : (new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc),
                       new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1).AddTicks(-1)),
            _ => (utcNow.Date.AddDays(-6), utcNow.Date.AddDays(1).AddTicks(-1))
        };
    }

    public async Task LoadAsync(bool showLoading = true)
    {
        if (showLoading)
        {
            RunOnUI(() => IsLoading = true);
        }
        ErrorMessage = null;

        try
        {
            var (start, end) = ComputeDateRange();
            string? ifaceFilter = SelectedInterface == "All" ? null : SelectedInterface;

            // 1. Fetch Daily or Hourly Usage
            List<DailyUsageRecord> dailyList = new();
            List<HourlyUsageRecord> hourlyList = new();

            if (SelectedPeriod == HistoryPeriodType.Today)
            {
                hourlyList = (await _repository.GetHourlyUsageAsync(start.Date, ifaceFilter)).ToList();
            }
            else
            {
                dailyList = (await _repository.GetDailyUsageAsync(start, end, ifaceFilter)).ToList();
            }

            // 2. Fetch Sessions
            var sessions = (await _repository.GetSessionsAsync(start, end, ifaceFilter)).ToList();

            // 3. Fetch Top Applications for the period
            var topApps = (await _repository.GetTopProcessesAsync(start, end, 30)).ToList();

            // 4. Compute Totals & Averages
            long totalDl = SelectedPeriod == HistoryPeriodType.Today
                ? hourlyList.Sum(h => h.BytesDownloaded)
                : dailyList.Sum(d => d.BytesDownloaded);

            long totalUl = SelectedPeriod == HistoryPeriodType.Today
                ? hourlyList.Sum(h => h.BytesUploaded)
                : dailyList.Sum(d => d.BytesUploaded);

            long totalUsage = totalDl + totalUl;

            double dlShare = totalUsage > 0 ? (double)totalDl / totalUsage * 100.0 : 0.0;
            double ulShare = totalUsage > 0 ? (double)totalUl / totalUsage * 100.0 : 0.0;

            long avgUsage = 0;
            if (SelectedPeriod == HistoryPeriodType.Today)
            {
                int currentHour = DateTime.UtcNow.Hour;
                avgUsage = currentHour >= 0 ? totalUsage / (currentHour + 1) : 0;
            }
            else
            {
                var activeDays = dailyList.Where(d => d.TotalBytes > 0).ToList();
                avgUsage = activeDays.Count > 0 ? (long)activeDays.Average(d => d.TotalBytes) : 0;
            }

            // 5. Compare with previous period for Trend Calculation
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

            // 6. Build Historical Graph Points & Geometry
            BuildHistoricalGraph(dailyList, hourlyList, start, end);

            // 7. Map Network Sessions
            var mappedSessions = sessions
                .GroupBy(s => !string.IsNullOrWhiteSpace(s.NetworkName) ? s.NetworkName : (!string.IsNullOrWhiteSpace(s.InterfaceName) ? s.InterfaceName : "Network Session"))
                .Select(g =>
                {
                    var first = g.First();
                    return new HistoricalSessionViewModel
                    {
                        NetworkName = g.Key,
                        InterfaceName = first.InterfaceName,
                        ConnectionType = first.ConnectionType,
                        BytesDownloaded = g.Sum(x => x.BytesDownloaded),
                        BytesUploaded = g.Sum(x => x.BytesUploaded),
                        IsActive = g.Any(x => !x.EndTime.HasValue)
                    };
                })
                .OrderByDescending(s => s.TotalBytes)
                .ToList();

            // 8. Map Application Profiles
            long grandAppTotal = topApps.Sum(a => a.BytesDownloaded + a.BytesUploaded);
            var mappedApps = topApps
                .GroupBy(a => a.ProcessName.Trim().ToLowerInvariant())
                .Select(g =>
                {
                    var first = g.First();
                    long appDl = g.Sum(x => x.BytesDownloaded);
                    long appUl = g.Sum(x => x.BytesUploaded);
                    long appTotal = appDl + appUl;
                    double appShare = grandAppTotal > 0 ? (double)appTotal / grandAppTotal * 100.0 : 0.0;

                    return new ApplicationHistoricalProfile
                    {
                        ProcessName = first.ProcessName,
                        Pid = first.Pid,
                        ExecutablePath = first.ExecutablePath,
                        UserName = first.UserName,
                        DataSource = first.DataSource,
                        DownloadBytes = appDl,
                        UploadBytes = appUl,
                        PercentageOfTotal = appShare,
                        ApplicationDisplayName = _appIconService.GetApplicationDisplayName(first.ProcessName, first.ExecutablePath),
                        ApplicationIcon = _appIconService.GetApplicationIcon(first.ProcessName, first.ExecutablePath)
                    };
                })
                .OrderByDescending(a => a.TotalBytes)
                .ToList();

            for (int i = 0; i < mappedApps.Count; i++)
            {
                mappedApps[i].DisplayIndex = i;
            }

            // Post all results to UI
            RunOnUI(() =>
            {
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

                NetworkSessions.Clear();
                foreach (var s in mappedSessions)
                    NetworkSessions.Add(s);

                Applications.Clear();
                foreach (var a in mappedApps)
                    Applications.Add(a);

                ApplyFilters();

                IsDataState = totalUsage > 0 || NetworkSessions.Count > 0 || Applications.Count > 0;
                IsEmpty = !IsDataState;

                if (showLoading) IsLoading = false;
            });
        }
        catch (Exception ex)
        {
            RunOnUI(() =>
            {
                ErrorMessage = $"Unable to load historical analytics: {ex.Message}";
                IsLoading = false;
                IsEmpty = true;
                IsDataState = false;
            });
        }
    }

    private void BuildHistoricalGraph(
        List<DailyUsageRecord> dailyList,
        List<HourlyUsageRecord> hourlyList,
        DateTime start,
        DateTime end)
    {
        HistoricalChartPoints.Clear();

        if (SelectedPeriod == HistoryPeriodType.Today)
        {
            var hourlyDict = hourlyList.ToDictionary(h => h.Hour, h => h);
            for (int h = 0; h < 24; h++)
            {
                hourlyDict.TryGetValue(h, out var rec);
                HistoricalChartPoints.Add(new HistoricalGraphSample
                {
                    Timestamp = start.Date.AddHours(h),
                    Label = $"{h:00}:00",
                    DownloadBytes = rec?.BytesDownloaded ?? 0,
                    UploadBytes = rec?.BytesUploaded ?? 0
                });
            }

            XAxisLabel0 = "00:00";
            XAxisLabel1 = "04:00";
            XAxisLabel2 = "08:00";
            XAxisLabel3 = "12:00";
            XAxisLabel4 = "16:00";
            XAxisLabel5 = "20:00";
            XAxisLabel6 = "NOW";
        }
        else if (SelectedPeriod == HistoryPeriodType.Last7Days)
        {
            var dailyDict = dailyList.ToDictionary(d => d.Day.Date, d => d);
            for (int i = 6; i >= 0; i--)
            {
                var day = DateTime.UtcNow.Date.AddDays(-i);
                dailyDict.TryGetValue(day, out var rec);
                HistoricalChartPoints.Add(new HistoricalGraphSample
                {
                    Timestamp = day,
                    Label = day.ToString("ddd, MMM d", CultureInfo.InvariantCulture),
                    DownloadBytes = rec?.BytesDownloaded ?? 0,
                    UploadBytes = rec?.BytesUploaded ?? 0
                });
            }

            var points = HistoricalChartPoints;
            XAxisLabel0 = points[0].Timestamp.ToString("ddd");
            XAxisLabel1 = points[1].Timestamp.ToString("ddd");
            XAxisLabel2 = points[2].Timestamp.ToString("ddd");
            XAxisLabel3 = points[3].Timestamp.ToString("ddd");
            XAxisLabel4 = points[4].Timestamp.ToString("ddd");
            XAxisLabel5 = points[5].Timestamp.ToString("ddd");
            XAxisLabel6 = points[6].Timestamp.ToString("ddd");
        }
        else // Month
        {
            int daysInMonth = DateTime.DaysInMonth(start.Year, start.Month);
            var dailyDict = dailyList.ToDictionary(d => d.Day.Date, d => d);

            for (int d = 1; d <= daysInMonth; d++)
            {
                var day = new DateTime(start.Year, start.Month, d, 0, 0, 0, DateTimeKind.Utc);
                dailyDict.TryGetValue(day, out var rec);
                HistoricalChartPoints.Add(new HistoricalGraphSample
                {
                    Timestamp = day,
                    Label = day.ToString("MMM d", CultureInfo.InvariantCulture),
                    DownloadBytes = rec?.BytesDownloaded ?? 0,
                    UploadBytes = rec?.BytesUploaded ?? 0
                });
            }

            string monthName = start.ToString("MMM");
            XAxisLabel0 = $"{monthName} 1";
            XAxisLabel1 = $"{monthName} 5";
            XAxisLabel2 = $"{monthName} 10";
            XAxisLabel3 = $"{monthName} 15";
            XAxisLabel4 = $"{monthName} 20";
            XAxisLabel5 = $"{monthName} 25";
            XAxisLabel6 = $"{monthName} {daysInMonth}";
        }

        RecomputeGraphGeometry();
    }

    public void UpdateChartDimensions(double width, double height)
    {
        if (width > 50 && height > 50)
        {
            ChartWidth = width;
            ChartHeight = height;
            RecomputeGraphGeometry();
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
            HoverY = Math.Min(closest.DownloadY, closest.UploadY);
            HoverTimestampText = closest.Label;
            HoverDownloadText = closest.DownloadFormatted;
            HoverUploadText = closest.UploadFormatted;
            HoverTotalText = closest.TotalFormatted;
        }
    }

    public void ClearHover()
    {
        IsHoverActive = false;
    }

    private void RecomputeGraphGeometry()
    {
        int count = HistoricalChartPoints.Count;
        if (count == 0)
        {
            HasHistoricalGraphData = false;
            HistoricalDownloadLineGeometry = null;
            HistoricalDownloadAreaGeometry = null;
            HistoricalUploadLineGeometry = null;
            HistoricalUploadAreaGeometry = null;
            return;
        }

        double maxObserved = HistoricalChartPoints.Max(p => Math.Max(p.DownloadBytes, p.UploadBytes));
        double yMax = CalculateStableYMax(maxObserved);

        YAxisTopText = ByteFormatter.FormatBytes((long)yMax);
        YAxisMidHighText = ByteFormatter.FormatBytes((long)(yMax * 0.75));
        YAxisMidText = ByteFormatter.FormatBytes((long)(yMax * 0.50));
        YAxisMidLowText = ByteFormatter.FormatBytes((long)(yMax * 0.25));
        YAxisMinText = "0 B";

        double canvasWidth = Math.Max(ChartWidth, 200.0);
        double usableHeight = Math.Max(ChartHeight - 16.0, 50.0);
        double yBase = usableHeight + 8.0;

        var dlPoints = new List<Point>(count);
        var ulPoints = new List<Point>(count);

        for (int i = 0; i < count; i++)
        {
            var p = HistoricalChartPoints[i];
            double x = count == 1 ? canvasWidth : (double)i / (count - 1) * canvasWidth;

            double dlRatio = Math.Clamp((double)p.DownloadBytes / yMax, 0.0, 1.0);
            double ulRatio = Math.Clamp((double)p.UploadBytes / yMax, 0.0, 1.0);

            double dlY = yBase - (dlRatio * usableHeight);
            double ulY = yBase - (ulRatio * usableHeight);

            p.CanvasX = x;
            p.DownloadY = dlY;
            p.UploadY = ulY;

            dlPoints.Add(new Point(x, dlY));
            ulPoints.Add(new Point(x, ulY));
        }

        var (dlLine, dlArea) = BuildCurveGeometry(dlPoints, yBase, canvasWidth);
        var (ulLine, ulArea) = BuildCurveGeometry(ulPoints, yBase, canvasWidth);

        HistoricalDownloadLineGeometry = dlLine;
        HistoricalDownloadAreaGeometry = dlArea;
        HistoricalUploadLineGeometry = ulLine;
        HistoricalUploadAreaGeometry = ulArea;
        HasHistoricalGraphData = true;
    }

    private static double CalculateStableYMax(double maxObserved)
    {
        if (maxObserved <= 0) return 1024 * 1024; // 1 MB fallback

        double target = maxObserved * 1.20;
        double[] steps = [
            100 * 1024,
            250 * 1024,
            500 * 1024,
            1024 * 1024,
            5 * 1024 * 1024,
            10 * 1024 * 1024,
            25 * 1024 * 1024,
            50 * 1024 * 1024,
            100 * 1024 * 1024,
            250 * 1024 * 1024,
            500 * 1024 * 1024,
            1024L * 1024 * 1024,
            2L * 1024 * 1024 * 1024,
            5L * 1024 * 1024 * 1024,
            10L * 1024 * 1024 * 1024,
            25L * 1024 * 1024 * 1024,
            50L * 1024 * 1024 * 1024,
            100L * 1024 * 1024 * 1024
        ];

        foreach (var step in steps)
        {
            if (target <= step) return step;
        }

        return target;
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
        lineGeometry.Figures!.Add(lineFigure);

        var areaGeometry = new PathGeometry();
        var areaFigure = new PathFigure
        {
            StartPoint = new Point(points[0].X, yBase),
            IsClosed = true,
            IsFilled = true
        };
        areaFigure.Segments!.Add(new LineSegment { Point = points[0] });
        areaGeometry.Figures!.Add(areaFigure);

        if (points.Count == 1)
        {
            lineFigure.Segments!.Add(new LineSegment { Point = new Point(canvasWidth, points[0].Y) });
            areaFigure.Segments.Add(new LineSegment { Point = new Point(canvasWidth, points[0].Y) });
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
                areaFigure.Segments.Add(segment);
            }
            areaFigure.Segments.Add(new LineSegment { Point = new Point(points.Last().X, yBase) });
        }

        return (lineGeometry, areaGeometry);
    }

    // ── Search & Sorting Filters ───────────────────────────────────────────────

    private void ApplyFilters()
    {
        // 1. Filter Network Sessions
        FilteredNetworkSessions.Clear();
        var sessionQuery = NetworkSessions.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string query = SearchText.Trim();
            sessionQuery = sessionQuery.Where(s =>
                s.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.SubtitleText.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var s in sessionQuery)
            FilteredNetworkSessions.Add(s);

        HasNetworkSessions = FilteredNetworkSessions.Count > 0;

        // 2. Filter & Sort Applications
        FilteredApplications.Clear();
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
            FilteredApplications.Add(appList[i]);
        }

        HasApplications = FilteredApplications.Count > 0;
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
