using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataSense.Database;
using DataSense.Helpers;

using DataSense.Services;

namespace DataSense.ViewModels;

public enum HistoryDatePreset
{
    Today,
    Last7Days,
    Last30Days,
    Custom
}

public partial class HistoryViewModel : ViewModelBase, IDisposable
{
    private readonly INetworkUsageRepository _repository;
    private readonly INetworkMonitorWorker   _networkMonitorWorker;
    private bool _initialising = true;
    private bool _disposed;
    private int  _tickCount = 4; // Start at 4 so first tick triggers immediately if needed

    public HistoryViewModel(INetworkUsageRepository repository, INetworkMonitorWorker networkMonitorWorker)
    {
        _repository           = repository ?? throw new ArgumentNullException(nameof(repository));
        _networkMonitorWorker = networkMonitorWorker ?? throw new ArgumentNullException(nameof(networkMonitorWorker));

        // Set backing fields directly so OnChanged callbacks don't fire during construction
        _selectedPreset    = HistoryDatePreset.Last7Days;
        _selectedInterface = "All";
        _initialising      = false;

        _networkMonitorWorker.NetworkUsageUpdated += OnNetworkUsageUpdated;

        _ = LoadAsync();
    }

    // ── Observable Collections ─────────────────────────────────────────────────

    /// <summary>One row per calendar day, most-recent first.</summary>
    public ObservableCollection<DailyUsageViewModel> DailyUsage { get; } = new();

    /// <summary>Interface names available in the DB (populated on first load).</summary>
    public ObservableCollection<string> Interfaces { get; } = new();

    /// <summary>All date-range preset values for the first ComboBox.</summary>
    public HistoryDatePreset[] DatePresets { get; } =
        (HistoryDatePreset[])Enum.GetValues(typeof(HistoryDatePreset));

    // ── State ──────────────────────────────────────────────────────────────────

    [ObservableProperty] private bool    _isLoading;
    [ObservableProperty] private bool    _isEmpty;
    [ObservableProperty] private bool    _isDataState;    // true when data is loaded and non-empty
    [ObservableProperty] private string? _errorMessage;

    // ── Filter state ───────────────────────────────────────────────────────────

    [ObservableProperty] private HistoryDatePreset _selectedPreset;
    [ObservableProperty] private string            _selectedInterface = "All";
    [ObservableProperty] private DateTimeOffset? _customStart = DateTimeOffset.Now;
    [ObservableProperty] private DateTimeOffset? _customEnd   = DateTimeOffset.Now;

    // ── Summary totals ─────────────────────────────────────────────────────────

    [ObservableProperty] private string _totalDownloadedText = "—";
    [ObservableProperty] private string _totalUploadedText   = "—";
    [ObservableProperty] private string _totalUsageText      = "—";
    [ObservableProperty] private string _avgDailyText        = "—";
    [ObservableProperty] private string _dayCountText        = "0";

    // ── ViewModelBase ──────────────────────────────────────────────────────────

    public override string Title => "History Log";

    // ── Commands ───────────────────────────────────────────────────────────────

    // Refresh command removed - auto-updates every 5 seconds

    // ── Property-change callbacks ──────────────────────────────────────────────

    partial void OnSelectedPresetChanged(HistoryDatePreset value)
    {
        if (!_initialising) _ = LoadAsync();
    }

    partial void OnSelectedInterfaceChanged(string value)
    {
        if (!_initialising) _ = LoadAsync();
    }

    partial void OnCustomStartChanged(DateTimeOffset? value)
    {
        if (!_initialising && SelectedPreset == HistoryDatePreset.Custom) _ = LoadAsync();
    }

    partial void OnCustomEndChanged(DateTimeOffset? value)
    {
        if (!_initialising && SelectedPreset == HistoryDatePreset.Custom) _ = LoadAsync();
    }

    // ── Core load logic ────────────────────────────────────────────────────────

    private (DateTime start, DateTime end) ComputeDateRange()
    {
        var utcNow = DateTime.UtcNow;
        return SelectedPreset switch
        {
            HistoryDatePreset.Today      => (utcNow.Date, utcNow.Date.AddDays(1).AddTicks(-1)),
            HistoryDatePreset.Last7Days  => (utcNow.Date.AddDays(-6), utcNow.Date.AddDays(1).AddTicks(-1)),
            HistoryDatePreset.Last30Days => (utcNow.Date.AddDays(-29), utcNow.Date.AddDays(1).AddTicks(-1)),
            HistoryDatePreset.Custom     =>
                // User picks a local calendar date via CalendarDatePicker (DateTimeOffset?)
                // Fall back to UTC today if either picker is null
                (CustomStart.HasValue
                    ? CustomStart.Value.LocalDateTime.Date.ToUniversalTime()
                    : utcNow.Date,
                 CustomEnd.HasValue
                    ? CustomEnd.Value.LocalDateTime.Date.ToUniversalTime().AddDays(1).AddTicks(-1)
                    : utcNow.Date.AddDays(1).AddTicks(-1)),
            _ => (utcNow.Date.AddDays(-6), utcNow.Date.AddDays(1).AddTicks(-1))
        };
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

    private async Task LoadAsync(bool showLoading = true)
    {
        if (showLoading)
        {
            IsLoading    = true;
        }
        IsDataState  = false;
        ErrorMessage = null;
        DailyUsage.Clear();

        try
        {
            // 1. Populate interface list from DB (once is enough but harmless to repeat)
            var ifaces    = await _repository.GetInterfaceNamesAsync();
            var ifaceList = ifaces.ToList();

            var currentInterface = SelectedInterface; // capture before we clear
            Interfaces.Clear();
            Interfaces.Add("All");
            foreach (var iface in ifaceList)
                Interfaces.Add(iface);

            // Restore selection if it still exists, otherwise fall back to "All"
            if (!Interfaces.Contains(currentInterface))
            {
                _initialising     = true; // suppress callback
                SelectedInterface = "All";
                _initialising     = false;
            }

            // 2. Query aggregated daily data
            var (start, end) = ComputeDateRange();
            string? ifaceFilter = SelectedInterface == "All" ? null : SelectedInterface;

            var daily = (await _repository.GetDailyUsageAsync(start, end, ifaceFilter)).ToList();

            foreach (var row in daily)
                DailyUsage.Add(new DailyUsageViewModel(row));

            // 3. Compute period totals from the daily aggregates (not from raw counters)
            long totalDl = daily.Sum(r => r.BytesDownloaded);
            long totalUl = daily.Sum(r => r.BytesUploaded);
            long total   = totalDl + totalUl;

            // 4. Avg / day (exclude days with zero usage to avoid diluting the average)
            var activeDays = daily.Where(r => r.TotalBytes > 0).ToList();
            long avgBytes  = activeDays.Count > 0
                ? (long)activeDays.Average(r => r.TotalBytes)
                : 0;

            TotalDownloadedText = daily.Any() ? ByteFormatter.FormatBytes(totalDl) : "—";
            TotalUploadedText   = daily.Any() ? ByteFormatter.FormatBytes(totalUl) : "—";
            TotalUsageText      = daily.Any() ? ByteFormatter.FormatBytes(total)   : "—";
            AvgDailyText        = avgBytes > 0 ? ByteFormatter.FormatBytes(avgBytes) : "—";
            DayCountText        = daily.Count.ToString();

            IsEmpty    = !daily.Any();
            IsDataState = daily.Any();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load history: {ex.Message}";
            IsEmpty      = true;
            IsDataState  = false;
        }
        finally
        {
            if (showLoading)
            {
                IsLoading = false;
            }
        }
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
                _networkMonitorWorker.NetworkUsageUpdated -= OnNetworkUsageUpdated;
            _disposed = true;
        }
    }
}
