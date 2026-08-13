using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataSense.Database;
using DataSense.Helpers;

namespace DataSense.ViewModels;

public enum HistoryDatePreset
{
    Today,
    Last7Days,
    Last30Days,
    Custom
}

public partial class HistoryViewModel : ViewModelBase
{
    private readonly INetworkUsageRepository _repository;
    private bool _initialising = true;

    public HistoryViewModel(INetworkUsageRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));

        // Set backing fields directly so OnChanged callbacks don't fire during construction
        _selectedPreset    = HistoryDatePreset.Last7Days;
        _selectedInterface = "All";
        _initialising      = false;

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

    [ObservableProperty] private bool   _isLoading;
    [ObservableProperty] private bool   _isEmpty;
    [ObservableProperty] private string? _errorMessage;

    // ── Filter state ───────────────────────────────────────────────────────────

    [ObservableProperty] private HistoryDatePreset _selectedPreset;
    [ObservableProperty] private string            _selectedInterface = "All";
    [ObservableProperty] private DateTime          _customStart = DateTime.Today;
    [ObservableProperty] private DateTime          _customEnd   = DateTime.Today;

    // ── Summary totals ─────────────────────────────────────────────────────────

    [ObservableProperty] private string _totalDownloadedText = "0 B";
    [ObservableProperty] private string _totalUploadedText   = "0 B";
    [ObservableProperty] private string _totalUsageText      = "0 B";
    [ObservableProperty] private string _dayCountText        = "0";

    // ── ViewModelBase ──────────────────────────────────────────────────────────

    public override string Title => "History Log";

    // ── Commands ───────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task Refresh() => await LoadAsync();

    // ── Property-change callbacks ──────────────────────────────────────────────

    partial void OnSelectedPresetChanged(HistoryDatePreset value)
    {
        if (!_initialising) _ = LoadAsync();
    }

    partial void OnSelectedInterfaceChanged(string value)
    {
        if (!_initialising) _ = LoadAsync();
    }

    // ── Core load logic ────────────────────────────────────────────────────────

    private (DateTime start, DateTime end) ComputeDateRange()
    {
        var utcNow = DateTime.UtcNow;
        return SelectedPreset switch
        {
            HistoryDatePreset.Today       => (utcNow.Date, utcNow.Date.AddDays(1).AddTicks(-1)),
            HistoryDatePreset.Last7Days   => (utcNow.Date.AddDays(-6), utcNow.Date.AddDays(1).AddTicks(-1)),
            HistoryDatePreset.Last30Days  => (utcNow.Date.AddDays(-29), utcNow.Date.AddDays(1).AddTicks(-1)),
            HistoryDatePreset.Custom      => (CustomStart.ToUniversalTime(), CustomEnd.ToUniversalTime().AddDays(1).AddTicks(-1)),
            _                             => (utcNow.Date.AddDays(-6), utcNow.Date.AddDays(1).AddTicks(-1))
        };
    }

    private async Task LoadAsync()
    {
        IsLoading    = true;
        ErrorMessage = null;
        DailyUsage.Clear();

        try
        {
            // 1. Populate interface list from DB (once is enough but harmless to repeat)
            var ifaces = await _repository.GetInterfaceNamesAsync();
            var ifaceList = ifaces.ToList();

            var currentInterface = SelectedInterface; // capture before we clear
            Interfaces.Clear();
            Interfaces.Add("All");
            foreach (var iface in ifaceList)
                Interfaces.Add(iface);

            // Restore selection if it still exists, otherwise fall back to "All"
            if (!Interfaces.Contains(currentInterface))
            {
                _initialising = true;           // suppress callback
                SelectedInterface = "All";
                _initialising = false;
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

            TotalDownloadedText = ByteFormatter.FormatBytes(totalDl);
            TotalUploadedText   = ByteFormatter.FormatBytes(totalUl);
            TotalUsageText      = ByteFormatter.FormatBytes(total);
            DayCountText        = daily.Count.ToString();

            IsEmpty = !daily.Any();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load history: {ex.Message}";
            IsEmpty      = true;
        }
        finally
        {
            IsLoading = false;
        }
    }
}
