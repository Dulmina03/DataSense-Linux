using System;
using System.Collections.ObjectModel;
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

public enum ExplorerDatePreset
{
    Last7Days,
    Last30Days,
    Last3Months,
    Last6Months,
    Last12Months,
    Custom
}

/// <summary>One row in the monthly overview bar chart.</summary>
public partial class MonthBarViewModel : ObservableObject
{
    public string MonthLabel    { get; set; } = string.Empty;
    public string TotalText     { get; set; } = string.Empty;
    public long   TotalBytes    { get; set; }
    public double BarHeight     { get; set; }
    public double DownloadHeight{ get; set; }
    public double UploadHeight  { get; set; }
    public bool   IsCurrentMonth{ get; set; }
}

/// <summary>One row in the application breakdown list.</summary>
public partial class AppUsageRowViewModel : ObservableObject
{
    public string ProcessName    { get; set; } = string.Empty;
    public string TotalText      { get; set; } = string.Empty;
    public string DownloadText   { get; set; } = string.Empty;
    public string UploadText     { get; set; } = string.Empty;
    public string PercentText    { get; set; } = string.Empty;
    public double PercentValue   { get; set; }
    public double BarWidth       { get; set; }
}

/// <summary>One spike row for the spike list.</summary>
public partial class SpikeRowViewModel : ObservableObject
{
    public string DateLabel      { get; set; } = string.Empty;
    public string TotalText      { get; set; } = string.Empty;
    public string MultiplierText { get; set; } = string.Empty;
    public string Description    { get; set; } = string.Empty;
}

public partial class HistoricalExplorerViewModel : ViewModelBase
{
    private readonly IHistoricalAnalyticsService _historicalService;
    private readonly INetworkUsageRepository     _repository;
    private bool _initialising = true;

    // ── Filter State ─────────────────────────────────────────────────────────
    [ObservableProperty] private ExplorerDatePreset _selectedPreset = ExplorerDatePreset.Last30Days;
    [ObservableProperty] private string             _selectedInterface = "All";
    [ObservableProperty] private DateTimeOffset?    _customStart = DateTimeOffset.Now.AddDays(-30);
    [ObservableProperty] private DateTimeOffset?    _customEnd   = DateTimeOffset.Now;

    // ── UI State ─────────────────────────────────────────────────────────────
    [ObservableProperty] private bool    _isLoading   = false;
    [ObservableProperty] private bool    _hasData     = false;
    [ObservableProperty] private bool    _isEmpty     = false;
    [ObservableProperty] private string? _errorMessage;

    // ── Summary Cards ────────────────────────────────────────────────────────
    [ObservableProperty] private string _totalText      = "—";
    [ObservableProperty] private string _downloadText   = "—";
    [ObservableProperty] private string _uploadText     = "—";
    [ObservableProperty] private string _avgDailyText   = "—";
    [ObservableProperty] private string _peakDayText    = "—";
    [ObservableProperty] private string _activeDaysText = "—";
    [ObservableProperty] private string _periodLabel    = string.Empty;

    // ── Comparison ───────────────────────────────────────────────────────────
    [ObservableProperty] private string _compareCurrentLabel  = "—";
    [ObservableProperty] private string _comparePreviousLabel = "—";
    [ObservableProperty] private string _compareCurrentTotal  = "—";
    [ObservableProperty] private string _comparePreviousTotal = "—";
    [ObservableProperty] private string _compareChangeText    = "—";
    [ObservableProperty] private string _compareChangeColor   = "#888899";
    [ObservableProperty] private bool   _hasComparison        = false;

    // ── Drill Level ──────────────────────────────────────────────────────────
    [ObservableProperty] private HistoricalDrillLevel _currentDrillLevel = HistoricalDrillLevel.Month;
    [ObservableProperty] private string _drillHeaderText = string.Empty;
    [ObservableProperty] private bool   _canDrillUp      = false;
    [ObservableProperty] private bool   _showHourly      = false;
    [ObservableProperty] private bool   _showMonthly     = true;
    private DateTime _drillDate; // current day being drilled into

    // ── Collections ──────────────────────────────────────────────────────────
    public ObservableCollection<string>             Interfaces       { get; } = new();
    public ObservableCollection<MonthBarViewModel>  MonthBars        { get; } = new();
    public ObservableCollection<DailyUsageViewModel> DailyRows       { get; } = new();
    public ObservableCollection<HourlyChartBarViewModel> HourlyBars  { get; } = new();
    public ObservableCollection<AppUsageRowViewModel>    AppRows      { get; } = new();
    public ObservableCollection<NetworkSession>          Sessions     { get; } = new();
    public ObservableCollection<SpikeRowViewModel>       Spikes       { get; } = new();
    public ExplorerDatePreset[] DatePresets { get; } = (ExplorerDatePreset[])Enum.GetValues(typeof(ExplorerDatePreset));

    public override string Title => "Historical Explorer";

    public HistoricalExplorerViewModel(
        IHistoricalAnalyticsService historicalService,
        INetworkUsageRepository     repository)
    {
        _historicalService = historicalService ?? throw new ArgumentNullException(nameof(historicalService));
        _repository        = repository        ?? throw new ArgumentNullException(nameof(repository));
        _initialising      = false;
        _ = LoadAsync();
    }

    // ── Property Change Callbacks ─────────────────────────────────────────────
    partial void OnSelectedPresetChanged(ExplorerDatePreset value)
    {
        if (!_initialising) _ = LoadAsync();
    }
    partial void OnSelectedInterfaceChanged(string value)
    {
        if (!_initialising) _ = LoadAsync();
    }
    partial void OnCustomStartChanged(DateTimeOffset? value)
    {
        if (!_initialising && SelectedPreset == ExplorerDatePreset.Custom) _ = LoadAsync();
    }
    partial void OnCustomEndChanged(DateTimeOffset? value)
    {
        if (!_initialising && SelectedPreset == ExplorerDatePreset.Custom) _ = LoadAsync();
    }

    // ── Commands ─────────────────────────────────────────────────────────────
    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    [RelayCommand]
    private async Task DrillIntoDay(DailyUsageViewModel row)
    {
        _drillDate = row.Day;
        CurrentDrillLevel = HistoricalDrillLevel.Hour;
        CanDrillUp        = true;
        ShowHourly        = true;
        ShowMonthly       = false;
        DrillHeaderText   = row.Day.ToString("dddd, MMMM d, yyyy");
        await LoadHourlyAsync(row.Day);
    }

    [RelayCommand]
    private async Task DrillUpAsync()
    {
        CurrentDrillLevel = HistoricalDrillLevel.Month;
        CanDrillUp        = false;
        ShowHourly        = false;
        ShowMonthly       = true;
        DrillHeaderText   = string.Empty;
        await LoadAsync();
    }

    [RelayCommand]
    private void NavigateBack()
    {
        var mainVm = App.Services?.GetService(typeof(MainWindowViewModel)) as MainWindowViewModel;
        mainVm?.NavigateToDashboardCommand.Execute(null);
    }

    // ── Core Load ────────────────────────────────────────────────────────────
    private (DateTime start, DateTime end) ComputeDateRange()
    {
        var now = DateTime.UtcNow;
        return SelectedPreset switch
        {
            ExplorerDatePreset.Last7Days    => (now.Date.AddDays(-6),   now.Date.AddDays(1).AddTicks(-1)),
            ExplorerDatePreset.Last30Days   => (now.Date.AddDays(-29),  now.Date.AddDays(1).AddTicks(-1)),
            ExplorerDatePreset.Last3Months  => (now.Date.AddMonths(-3), now.Date.AddDays(1).AddTicks(-1)),
            ExplorerDatePreset.Last6Months  => (now.Date.AddMonths(-6), now.Date.AddDays(1).AddTicks(-1)),
            ExplorerDatePreset.Last12Months => (now.Date.AddMonths(-12),now.Date.AddDays(1).AddTicks(-1)),
            ExplorerDatePreset.Custom =>
                (CustomStart.HasValue
                    ? CustomStart.Value.LocalDateTime.Date.ToUniversalTime()
                    : now.Date,
                 CustomEnd.HasValue
                    ? CustomEnd.Value.LocalDateTime.Date.ToUniversalTime().AddDays(1).AddTicks(-1)
                    : now.Date.AddDays(1).AddTicks(-1)),
            _ => (now.Date.AddDays(-29), now.Date.AddDays(1).AddTicks(-1))
        };
    }

    private async Task LoadAsync()
    {
        Dispatcher.UIThread.Post(() => IsLoading = true);
        try
        {
            // Interfaces
            var ifaces = (await _repository.GetInterfaceNamesAsync()).ToList();
            string? ifaceFilter = SelectedInterface == "All" ? null : SelectedInterface;

            Dispatcher.UIThread.Post(() =>
            {
                var current = SelectedInterface;
                Interfaces.Clear();
                Interfaces.Add("All");
                foreach (var i in ifaces) Interfaces.Add(i);
                if (!Interfaces.Contains(current)) SelectedInterface = "All";
            });

            var (start, end) = ComputeDateRange();

            // Parallel queries
            var explorerTask = _historicalService.GetExplorerResultAsync(
                start, end, HistoricalDrillLevel.Month, ifaceFilter);
            var monthlyTask  = _historicalService.GetMonthlyOverviewAsync(12);

            await Task.WhenAll(explorerTask, monthlyTask);

            var result  = explorerTask.Result;
            var monthly = monthlyTask.Result;

            // Build chart data off-thread
            var monthBars  = BuildMonthBars(monthly);
            var dailyRows  = result.DailyBreakdown.OrderByDescending(d => d.Day)
                                   .Select(d => new DailyUsageViewModel(d)).ToList();
            var appRows    = BuildAppRows(result.TopApps.ToList());
            var spikeRows  = result.Spikes
                                   .Select(s => new SpikeRowViewModel
                                   {
                                       DateLabel      = s.Date.ToString("MMM d, yyyy"),
                                       TotalText      = ByteFormatter.FormatBytes(s.TotalBytes),
                                       MultiplierText = $"{s.SpikeMultiplier:F1}×",
                                       Description    = s.Description
                                   }).ToList();

            Dispatcher.UIThread.Post(() =>
            {
                // Summary
                var activeDays = result.DailyBreakdown.Where(d => d.TotalBytes > 0).ToList();
                long avg       = activeDays.Count > 0 ? (long)activeDays.Average(d => (double)d.TotalBytes) : 0;
                var  peak      = activeDays.OrderByDescending(d => d.TotalBytes).FirstOrDefault();

                TotalText      = ByteFormatter.FormatBytes(result.TotalBytes);
                DownloadText   = ByteFormatter.FormatBytes(result.TotalDownloaded);
                UploadText     = ByteFormatter.FormatBytes(result.TotalUploaded);
                AvgDailyText   = avg > 0 ? ByteFormatter.FormatBytes(avg) : "—";
                ActiveDaysText = $"{activeDays.Count} days";
                PeakDayText    = peak != null
                    ? $"{peak.Day:MMM d} · {ByteFormatter.FormatBytes(peak.TotalBytes)}"
                    : "—";
                PeriodLabel    = result.PeriodLabel;

                // Comparison
                var cmp = result.Comparison;
                if (cmp != null && (cmp.PeriodATotal > 0 || cmp.PeriodBTotal > 0))
                {
                    HasComparison        = true;
                    CompareCurrentLabel  = cmp.PeriodALabel;
                    ComparePreviousLabel = cmp.PeriodBLabel;
                    CompareCurrentTotal  = ByteFormatter.FormatBytes(cmp.PeriodATotal);
                    ComparePreviousTotal = ByteFormatter.FormatBytes(cmp.PeriodBTotal);
                    var pct              = cmp.TotalChangePct;
                    CompareChangeText    = pct >= 0 ? $"+{pct:F1}%" : $"{pct:F1}%";
                    CompareChangeColor   = pct >= 0 ? "#FF6B6B" : "#00E676";
                }
                else HasComparison = false;

                // Month bars
                MonthBars.Clear();
                foreach (var b in monthBars) MonthBars.Add(b);

                // Daily rows
                DailyRows.Clear();
                foreach (var r in dailyRows) DailyRows.Add(r);

                // Apps
                AppRows.Clear();
                foreach (var a in appRows) AppRows.Add(a);

                // Sessions
                Sessions.Clear();
                foreach (var s in result.Sessions) Sessions.Add(s);

                // Spikes
                Spikes.Clear();
                foreach (var s in spikeRows) Spikes.Add(s);

                HasData  = result.TotalBytes > 0;
                IsEmpty  = result.TotalBytes == 0;
                ShowMonthly = true;
                ShowHourly  = false;
                CanDrillUp  = false;
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                ErrorMessage = $"Failed to load history: {ex.Message}";
                HasData      = false;
                IsEmpty      = true;
            });
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsLoading = false);
        }
    }

    private async Task LoadHourlyAsync(DateTime day)
    {
        IsLoading = true;
        try
        {
            string? ifaceFilter = SelectedInterface == "All" ? null : SelectedInterface;
            var hourly = await _historicalService.GetHourlyBreakdownAsync(day, ifaceFilter);
            var bars   = BuildHourlyBars(hourly.ToList());
            HourlyBars.Clear();
            foreach (var b in bars) HourlyBars.Add(b);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load hourly data: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    // ── Chart Builders ────────────────────────────────────────────────────────
    private const double ChartMaxHeight = 120.0;

    private static List<MonthBarViewModel> BuildMonthBars(IList<MonthlyUsageSummary> months)
    {
        long max = months.Max(m => m.TotalBytes);
        if (max <= 0) max = 1;
        var now  = DateTime.UtcNow;
        return months.Select(m => new MonthBarViewModel
        {
            MonthLabel     = m.MonthLabel,
            TotalText      = m.TotalBytes > 0 ? ByteFormatter.FormatBytes(m.TotalBytes) : "—",
            TotalBytes     = m.TotalBytes,
            BarHeight      = (double)m.TotalBytes / max * ChartMaxHeight,
            DownloadHeight = (double)m.BytesDownloaded / max * ChartMaxHeight,
            UploadHeight   = (double)m.BytesUploaded   / max * ChartMaxHeight,
            IsCurrentMonth = m.Year == now.Year && m.Month == now.Month
        }).Reverse().ToList();
    }

    private static List<AppUsageRowViewModel> BuildAppRows(List<HistoricalApplicationSummary> apps)
    {
        double maxPct = apps.Count > 0 ? apps.Max(a => a.PercentOfTotal) : 1;
        if (maxPct <= 0) maxPct = 1;
        return apps.Select(a => new AppUsageRowViewModel
        {
            ProcessName  = a.ProcessName,
            TotalText    = ByteFormatter.FormatBytes(a.TotalBytes),
            DownloadText = ByteFormatter.FormatBytes(a.DownloadBytes),
            UploadText   = ByteFormatter.FormatBytes(a.UploadBytes),
            PercentText  = $"{a.PercentOfTotal:F1}%",
            PercentValue = a.PercentOfTotal,
            BarWidth     = a.PercentOfTotal / maxPct * 200.0
        }).ToList();
    }

    private static List<HourlyChartBarViewModel> BuildHourlyBars(List<HourlyUsageRecord> hourly)
    {
        if (!hourly.Any()) return new();
        long max = hourly.Max(h => h.TotalBytes);
        if (max <= 0) max = 1;
        double barW = 18.0;
        return hourly.Select((h, i) => new HourlyChartBarViewModel
        {
            Hour              = h.Hour,
            BytesDownloaded   = h.BytesDownloaded,
            BytesUploaded     = h.BytesUploaded,
            TotalBytes        = h.TotalBytes,
            DownloadedText    = ByteFormatter.FormatBytes(h.BytesDownloaded),
            UploadedText      = ByteFormatter.FormatBytes(h.BytesUploaded),
            TotalText         = ByteFormatter.FormatBytes(h.TotalBytes),
            BarX              = i * (barW + 3),
            BarWidth          = barW,
            DownloadBarHeight = (double)h.BytesDownloaded / max * ChartMaxHeight,
            UploadBarHeight   = (double)h.BytesUploaded   / max * ChartMaxHeight,
            DownloadBarY      = ChartMaxHeight - ((double)h.TotalBytes / max * ChartMaxHeight),
            UploadBarY        = ChartMaxHeight - ((double)h.BytesUploaded / max * ChartMaxHeight),
            LabelY            = ChartMaxHeight + 4
        }).ToList();
    }
}
