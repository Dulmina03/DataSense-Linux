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
    private readonly IAnalyticsService _analyticsService;
    private readonly ProcessNetworkMonitorWorker _processMonitorWorker;
    private readonly IApplicationIntelligenceService _appIntelligenceService;
    
    private bool _disposed;
    private string _processName = string.Empty;

    public ApplicationAnalyticsViewModel(
        IAnalyticsService analyticsService,
        ProcessNetworkMonitorWorker processMonitorWorker,
        IApplicationIntelligenceService appIntelligenceService)
    {
        _analyticsService       = analyticsService       ?? throw new ArgumentNullException(nameof(analyticsService));
        _processMonitorWorker   = processMonitorWorker   ?? throw new ArgumentNullException(nameof(processMonitorWorker));
        _appIntelligenceService = appIntelligenceService ?? throw new ArgumentNullException(nameof(appIntelligenceService));

        _processMonitorWorker.LiveTrafficUpdated += OnLiveTrafficUpdated;
    }

    public void Initialize(string processName)
    {
        _processName = processName;
        ProcessNameText = processName;
        // In the future, we could attempt to look up the friendly name and icon here
        ApplicationNameText = processName; 
        
        _ = LoadAnalyticsAsync(showLoading: true);
    }

    [ObservableProperty] private string _processNameText = "—";
    [ObservableProperty] private string _applicationNameText = "—";
    [ObservableProperty] private string _executablePathText = "—";
    [ObservableProperty] private string _userNameText = "—";
    [ObservableProperty] private string _pidText = "—";
    [ObservableProperty] private string _dataSourceText = "Source: Linux nethogs";
    [ObservableProperty] private string _monitoringStateText = "Active";
    
    // ── Live Status ──────────────────────────────────────────────────────────
    [ObservableProperty] private string _liveDownloadSpeed = "—";
    [ObservableProperty] private string _liveUploadSpeed = "—";
    [ObservableProperty] private bool _isCurrentlyActive = false;

    // ── Period Selection ─────────────────────────────────────────────────────
    [ObservableProperty] private AnalyticsPeriod _selectedPeriod = AnalyticsPeriod.Last7Days;

    // ── Summary Cards ────────────────────────────────────────────────────────
    [ObservableProperty] private string _periodTotalDownloadedText = "—";
    [ObservableProperty] private string _periodTotalUploadedText   = "—";
    [ObservableProperty] private string _periodTotalUsageText      = "—";
    
    // ── Activity ─────────────────────────────────────────────────────────────
    [ObservableProperty] private string _firstActiveText = "—";
    [ObservableProperty] private string _lastActiveText = "—";
    [ObservableProperty] private string _daysUsedText = "—";

    // ── Chart Layout Constants ───────────────────────────────────────────────
    public const double ChartHeight = 160.0;
    private const double BarGap = 4.0;
    
    [ObservableProperty] private double _chartWidth = 560.0;
    
    // ── Charts ───────────────────────────────────────────────────────────────
    public ObservableCollection<DailyChartBarViewModel> PeriodChartItems { get; } = new();
    public ObservableCollection<HourlyChartBarViewModel> HourlyChartItems { get; } = new();
    
    [ObservableProperty] private bool _isHourlyChart = false;
    [ObservableProperty] private bool _isChartEmpty = true;
    
    // ── Download vs Upload Ratio ─────────────────────────────────────────────
    [ObservableProperty] private string _downloadRatioText = "—";
    [ObservableProperty] private string _uploadRatioText = "—";
    [ObservableProperty] private string _downloadActualText = "—";
    [ObservableProperty] private string _uploadActualText = "—";
    [ObservableProperty] private bool _hasPeriodData = false;
    
    [ObservableProperty] private GridLength _downloadColumnWidth = new GridLength(1, GridUnitType.Star);
    [ObservableProperty] private GridLength _uploadColumnWidth   = new GridLength(1, GridUnitType.Star);

    // ── History Table ────────────────────────────────────────────────────────
    public ObservableCollection<DailyUsageRecord> DailyHistoryItems { get; } = new();
    [ObservableProperty] private bool _isHistoryTableEmpty = true;

    // ── Application Intelligence & Smart Recommendations ────────────────────
    [ObservableProperty] private ApplicationUsageProfile? _currentProfile;
    public ObservableCollection<ApplicationRecommendation> ProcessRecommendations { get; } = new();
    [ObservableProperty] private bool _hasProcessRecommendations = false;

    // ── Loading State ────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isLoading = false;
    
    [RelayCommand]
    private void NavigateBack()
    {
        var mainWindowVm = App.Services?.GetService(typeof(MainWindowViewModel)) as MainWindowViewModel;
        // The default NavigateToDashboard uses the singleton DashboardViewModel
        mainWindowVm?.NavigateToDashboardCommand.Execute(null);
    }
    
    [RelayCommand]
    private async Task SelectPeriodAsync(string periodString)
    {
        if (Enum.TryParse<AnalyticsPeriod>(periodString, out var period))
        {
            if (SelectedPeriod != period)
            {
                SelectedPeriod = period;
                await LoadAnalyticsAsync(showLoading: true);
            }
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

    private async Task LoadAnalyticsAsync(bool showLoading)
    {
        if (string.IsNullOrEmpty(_processName)) return;
        
        if (showLoading)
        {
            Dispatcher.UIThread.Post(() => IsLoading = true);
        }

        try
        {
            var summary = await _analyticsService.GetProcessSummaryAsync(_processName, SelectedPeriod);
            
            // Build Charts
            if (SelectedPeriod == AnalyticsPeriod.Today)
            {
                var hourlyData = await _analyticsService.GetProcessTodayHourlyAsync(_processName);
                var hourlyItems = BuildHourlyChartItems(hourlyData.ToList(), ChartWidth);

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
                var dailyData = await _analyticsService.GetProcessDailySeriesAsync(_processName, SelectedPeriod);
                var dailyItems = BuildChartItems(dailyData.ToList(), ChartWidth);

                Dispatcher.UIThread.Post(() =>
                {
                    IsHourlyChart = false;
                    PeriodChartItems.Clear();
                    foreach (var item in dailyItems) PeriodChartItems.Add(item);
                    IsChartEmpty = !dailyItems.Any(i => i.HasData);
                    
                    DailyHistoryItems.Clear();
                    foreach (var day in dailyData)
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
            double dlRatio = summary.TotalUsage > 0 ? (double)summary.TotalDownloaded / summary.TotalUsage : 0.5;
            double ulRatio = summary.TotalUsage > 0 ? (double)summary.TotalUploaded / summary.TotalUsage : 0.5;

            Dispatcher.UIThread.Post(() =>
            {
                PeriodTotalDownloadedText = ByteFormatter.FormatBytes(summary.TotalDownloaded);
                PeriodTotalUploadedText   = ByteFormatter.FormatBytes(summary.TotalUploaded);
                PeriodTotalUsageText      = ByteFormatter.FormatBytes(summary.TotalUsage);
                
                FirstActiveText = summary.FirstActive?.ToString("MMM d, HH:mm") ?? "—";
                LastActiveText  = summary.LastActive?.ToString("MMM d, HH:mm") ?? "—";
                DaysUsedText    = summary.DaysUsed > 0 ? summary.DaysUsed.ToString() : "—";
                
                HasPeriodData = summary.TotalUsage > 0;
                DownloadRatioText = HasPeriodData ? $"{dlRatio * 100:F0}%" : "—";
                UploadRatioText   = HasPeriodData ? $"{ulRatio * 100:F0}%" : "—";
                DownloadActualText = HasPeriodData ? ByteFormatter.FormatBytes(summary.TotalDownloaded) : "—";
                UploadActualText   = HasPeriodData ? ByteFormatter.FormatBytes(summary.TotalUploaded) : "—";

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
            });

            // Application Intelligence & Smart Recommendations
            var profile = await _appIntelligenceService.GetApplicationProfileAsync(_processName);
            var recs    = (await _appIntelligenceService.GetProcessRecommendationsAsync(_processName)).ToList();

            Dispatcher.UIThread.Post(() =>
            {
                CurrentProfile = profile;
                ProcessRecommendations.Clear();
                foreach (var rec in recs) ProcessRecommendations.Add(rec);
                HasProcessRecommendations = ProcessRecommendations.Count > 0;

                if (showLoading) IsLoading = false;
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"App analytics failed: {ex.Message}");
            Dispatcher.UIThread.Post(() => { if (showLoading) IsLoading = false; });
        }
    }

    private void OnLiveTrafficUpdated(IEnumerable<ProcessNetworkUsage> currentBatch)
    {
        var active = currentBatch.FirstOrDefault(p => p.ProcessIdentifier.Equals(_processName, StringComparison.OrdinalIgnoreCase));
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

    // Reuse chart geometry logic from DashboardViewModel
    private static List<DailyChartBarViewModel> BuildChartItems(List<DailyUsageRecord> daily, double chartWidth)
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
            double dlFrac = d.TotalBytes > 0 ? (double)d.BytesDownloaded / d.TotalBytes : 0.5;
            double ulFrac = 1.0 - dlFrac;
            double dlBarHeight = totalBarHeight * dlFrac;
            double ulBarHeight = totalBarHeight * ulFrac;
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

    private static List<HourlyChartBarViewModel> BuildHourlyChartItems(List<HourlyUsageRecord> hourly, double chartWidth)
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
