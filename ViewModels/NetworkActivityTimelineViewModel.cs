using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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

public partial class NetworkActivityTimelineViewModel : ViewModelBase
{
    private readonly ISessionIntelligenceService _sessionService;
    private readonly INetworkUsageRepository _repository;
    private readonly IExportService _exportService;

    public override string Title => "Network Activity Timeline";

    // ── Filtering Controls ───────────────────────────────────────────────────
    [ObservableProperty] private string _selectedTimeRange = "30 Days";
    [ObservableProperty] private string _selectedNetworkFilter = "All Networks";
    [ObservableProperty] private string _selectedConnectionTypeFilter = "All";
    [ObservableProperty] private string _selectedMinUsageFilter = "0 MB";
    [ObservableProperty] private string _selectedMinDurationFilter = "0 min";

    public ObservableCollection<string> NetworkFilterOptions { get; } = new() { "All Networks" };
    public ObservableCollection<string> ConnectionTypeOptions { get; } = new() { "All", "Wi-Fi", "Ethernet", "Mobile" };
    public ObservableCollection<string> MinUsageOptions { get; } = new() { "0 MB", "10 MB", "100 MB", "1 GB" };
    public ObservableCollection<string> MinDurationOptions { get; } = new() { "0 min", "5 min", "15 min", "1 hour" };

    // ── Timeline List ────────────────────────────────────────────────────────
    public ObservableCollection<NetworkSessionItem> Sessions { get; } = new();
    [ObservableProperty] private NetworkSessionItem? _selectedSession;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _noSessionsFound;

    // ── Selected Session Detail Cards ────────────────────────────────────────
    public ObservableCollection<SessionProcessAttribution> SelectedSessionProcesses { get; } = new();
    public ObservableCollection<SessionIntelligenceInsight> SelectedSessionInsights { get; } = new();
    [ObservableProperty] private SessionComparisonResult? _comparisonResult;
    [ObservableProperty] private NetworkSessionPattern? _networkPattern;
    [ObservableProperty] private bool _processTelemetryUnavailable;
    [ObservableProperty] private string _exportStatusMessage = string.Empty;

    // ── Network Switch Timeline ──────────────────────────────────────────────
    public ObservableCollection<NetworkSwitchItem> NetworkSwitches { get; } = new();

    public NetworkActivityTimelineViewModel(
        ISessionIntelligenceService sessionService,
        INetworkUsageRepository repository,
        IExportService exportService)
    {
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));

        _ = InitializeAsync();
    }

    public async Task InitializeAsync()
    {
        await LoadAvailableNetworksAsync();
        await RefreshTimelineAsync();
    }

    private async Task LoadAvailableNetworksAsync()
    {
        try
        {
            var networks = await _repository.GetAvailableNetworksAsync();
            Dispatcher.UIThread.Post(() =>
            {
                NetworkFilterOptions.Clear();
                NetworkFilterOptions.Add("All Networks");
                foreach (var net in networks)
                {
                    if (!string.IsNullOrEmpty(net) && net != "—")
                        NetworkFilterOptions.Add(net);
                }
            });
        }
        catch { }
    }

    [RelayCommand]
    public async Task RefreshTimelineAsync()
    {
        IsLoading = true;
        try
        {
            var (start, end) = GetDateRange(SelectedTimeRange);
            long minBytes = GetMinBytes(SelectedMinUsageFilter);
            TimeSpan? minDuration = GetMinDuration(SelectedMinDurationFilter);
            string? netFilter = SelectedNetworkFilter == "All Networks" ? null : SelectedNetworkFilter;

            var timeline = (await _sessionService.GetSessionTimelineAsync(start, end, netFilter, SelectedConnectionTypeFilter, minBytes, minDuration)).ToList();
            var switches = (await _sessionService.GetNetworkSwitchTimelineAsync(start, end)).ToList();

            Dispatcher.UIThread.Post(() =>
            {
                Sessions.Clear();
                foreach (var s in timeline) Sessions.Add(s);

                NetworkSwitches.Clear();
                foreach (var sw in switches) NetworkSwitches.Add(sw);

                NoSessionsFound = Sessions.Count == 0;

                if (SelectedSession == null && Sessions.Count > 0)
                {
                    SelectedSession = Sessions[0];
                }
                else if (SelectedSession != null)
                {
                    var updated = Sessions.FirstOrDefault(s => s.Session.Id == SelectedSession.Session.Id);
                    if (updated != null) SelectedSession = updated;
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RefreshTimelineAsync failed: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedTimeRangeChanged(string value) => _ = RefreshTimelineAsync();
    partial void OnSelectedNetworkFilterChanged(string value) => _ = RefreshTimelineAsync();
    partial void OnSelectedConnectionTypeFilterChanged(string value) => _ = RefreshTimelineAsync();
    partial void OnSelectedMinUsageFilterChanged(string value) => _ = RefreshTimelineAsync();
    partial void OnSelectedMinDurationFilterChanged(string value) => _ = RefreshTimelineAsync();

    partial void OnSelectedSessionChanged(NetworkSessionItem? value)
    {
        if (value == null) return;
        _ = LoadSessionDetailsAsync(value);
    }

    private async Task LoadSessionDetailsAsync(NetworkSessionItem item)
    {
        try
        {
            var processes = (await _sessionService.GetSessionProcessAttributionAsync(item.Session)).ToList();
            var comparison = await _sessionService.CompareSessionAsync(item.Session);
            var pattern = await _sessionService.GetNetworkPatternAsync(item.NetworkName);
            var insights = (await _sessionService.GenerateSessionInsightsAsync(item.Session)).ToList();

            Dispatcher.UIThread.Post(() =>
            {
                SelectedSessionProcesses.Clear();
                foreach (var p in processes) SelectedSessionProcesses.Add(p);
                ProcessTelemetryUnavailable = SelectedSessionProcesses.Count == 0;

                ComparisonResult = comparison;
                NetworkPattern = pattern;

                SelectedSessionInsights.Clear();
                foreach (var ins in insights) SelectedSessionInsights.Add(ins);
            });
        }
        catch { }
    }

    [RelayCommand]
    private void SelectSession(NetworkSessionItem session)
    {
        SelectedSession = session;
    }

    [RelayCommand]
    private void SelectRange(string rangeStr)
    {
        SelectedTimeRange = rangeStr;
    }

    [RelayCommand]
    public void ResetFilters()
    {
        SelectedTimeRange = "30 Days";
        SelectedNetworkFilter = "All Networks";
        SelectedConnectionTypeFilter = "All";
        SelectedMinUsageFilter = "0 MB";
        SelectedMinDurationFilter = "0 min";
    }

    [RelayCommand]
    private async Task ExportSessionReportAsync()
    {
        try
        {
            var options = new ExportOptions
            {
                DataType = ExportDataType.NetworkSessions,
                Format = ExportFormat.CSV,
                StartDate = DateTime.UtcNow.AddDays(-30),
                EndDate = DateTime.UtcNow
            };

            var result = await _exportService.ExportDataAsync(options);
            ExportStatusMessage = result.Success ? $"✅ Saved report to {Path.GetFileName(result.FilePath)}" : $"⚠️ Export failed: {result.ErrorMessage}";
        }
        catch (Exception ex)
        {
            ExportStatusMessage = $"⚠️ Export failed: {ex.Message}";
        }
    }

    private static (DateTime Start, DateTime End) GetDateRange(string rangeStr)
    {
        var now = DateTime.UtcNow;
        return rangeStr switch
        {
            "Today" => (now.Date, now),
            "Yesterday" => (now.Date.AddDays(-1), now.Date.AddTicks(-1)),
            "7 Days" => (now.Date.AddDays(-7), now),
            "This Month" => (new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc), now),
            _ => (now.Date.AddDays(-30), now)
        };
    }

    private static long GetMinBytes(string minUsageStr)
    {
        return minUsageStr switch
        {
            "10 MB" => 10L * 1024 * 1024,
            "100 MB" => 100L * 1024 * 1024,
            "1 GB" => 1024L * 1024 * 1024,
            _ => 0L
        };
    }

    private static TimeSpan? GetMinDuration(string minDurationStr)
    {
        return minDurationStr switch
        {
            "5 min" => TimeSpan.FromMinutes(5),
            "15 min" => TimeSpan.FromMinutes(15),
            "1 hour" => TimeSpan.FromHours(1),
            _ => null
        };
    }
}
