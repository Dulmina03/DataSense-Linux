using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataSense.Database;
using DataSense.Helpers;
using DataSense.Models;

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

    public HistoryViewModel(INetworkUsageRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        // defaults
        SelectedPreset = HistoryDatePreset.Last7Days;
        SelectedInterface = "All";
        PageSize = 50;
        CurrentPage = 0;
        // initial load (fire‑and‑forget, UI will react to IsLoading)
        _ = LoadHistoryAsync();
    }

    // Observable collections & flags
    public ObservableCollection<HistoryItemViewModel> Records { get; } = new();

    // Collections for ComboBoxes
    public IEnumerable<HistoryDatePreset> DatePresets => Enum.GetValues(typeof(HistoryDatePreset)).Cast<HistoryDatePreset>();
    public ObservableCollection<string> Interfaces { get; } = new();

    [ObservableProperty]
    private string _title = "History Log";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private string? _errorMessage;

    // Pagination / filter state
    public int PageSize { get; }

    [ObservableProperty]
    private int _currentPage;

    [ObservableProperty]
    private int _totalPages;

    [ObservableProperty]
    private int _totalRecords;

    [ObservableProperty]
    private HistoryDatePreset _selectedPreset;

    [ObservableProperty]
    private DateTime _customStart = DateTime.Today;

    [ObservableProperty]
    private DateTime _customEnd = DateTime.Today;

    [ObservableProperty]
    private string _selectedInterface;

    // Summary texts
    [ObservableProperty]
    private string _totalDownloadedText = "0 B";

    [ObservableProperty]
    private string _totalUploadedText = "0 B";

    [ObservableProperty]
    private string _peakDownloadSpeedText = "0 B/s";

    [ObservableProperty]
    private string _sampleCountText = "0";

    // Commands
    public IRelayCommand RefreshCommand => new RelayCommand(async () => await RefreshAsync());
    public IRelayCommand NextPageCommand => new RelayCommand(async () => await ChangePageAsync(CurrentPage + 1), () => CurrentPage + 1 < TotalPages);
    public IRelayCommand PrevPageCommand => new RelayCommand(async () => await ChangePageAsync(CurrentPage - 1), () => CurrentPage > 0);

    private async Task RefreshAsync()
    {
        CurrentPage = 0;
        await LoadHistoryAsync();
    }

    private async Task ChangePageAsync(int newPage)
    {
        if (newPage < 0 || newPage >= TotalPages) return;
        CurrentPage = newPage;
        await LoadHistoryAsync();
    }

    private (DateTime start, DateTime end) ComputeDateRange()
    {
        var utcNow = DateTime.UtcNow;
        return SelectedPreset switch
        {
            HistoryDatePreset.Today => (utcNow.Date, utcNow.Date.AddDays(1).AddTicks(-1)),
            HistoryDatePreset.Last7Days => (utcNow.AddDays(-7), utcNow),
            HistoryDatePreset.Last30Days => (utcNow.AddDays(-30), utcNow),
            HistoryDatePreset.Custom => (CustomStart.ToUniversalTime(), CustomEnd.ToUniversalTime()),
            _ => (utcNow.AddDays(-7), utcNow)
        };
    }

    private async Task LoadHistoryAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        Records.Clear();
        // Populate interface list (reset)
        Interfaces.Clear();
        Interfaces.Add("All");
        try
        {
            var (start, end) = ComputeDateRange();
            string? iface = SelectedInterface == "All" ? null : SelectedInterface;
            var (records, totalCount) = await _repository.GetHistoryPagedAsync(start, end, iface, CurrentPage, PageSize);

            foreach (var rec in records)
            {
                Records.Add(new HistoryItemViewModel(rec));
            }

            // Update interface collection based on loaded records
            foreach (var ifaceName in records.Select(r => r.InterfaceName).Distinct())
            {
                if (!Interfaces.Contains(ifaceName)) Interfaces.Add(ifaceName);
            }

            TotalRecords = totalCount;
            TotalPages = (int)Math.Ceiling((double)TotalRecords / PageSize);
            IsEmpty = !Records.Any();

            if (records.Any())
            {
                var totalDown = records.Sum(r => r.BytesReceived);
                var totalUp = records.Sum(r => r.BytesSent);
                var peakDown = records.Max(r => r.DownloadSpeed);

                TotalDownloadedText = ByteFormatter.FormatBytes(totalDown);
                TotalUploadedText = ByteFormatter.FormatBytes(totalUp);
                PeakDownloadSpeedText = ByteFormatter.FormatSpeed(peakDown);
                SampleCountText = totalCount.ToString();
            }
            else
            {
                TotalDownloadedText = "0 B";
                TotalUploadedText = "0 B";
                PeakDownloadSpeedText = "0 B/s";
                SampleCountText = "0";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load history: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            (NextPageCommand as RelayCommand)?.NotifyCanExecuteChanged();
            (PrevPageCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }
}
