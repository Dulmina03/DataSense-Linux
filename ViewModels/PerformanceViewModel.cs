using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataSense.Helpers;
using DataSense.Models;
using DataSense.Services;

namespace DataSense.ViewModels;

public partial class PerformanceViewModel : ViewModelBase
{
    private readonly IPerformanceMonitorService _perfService;

    public override string Title => "Performance & Resource Monitor";

    [ObservableProperty] private bool   _isMonitoring = true;
    [ObservableProperty] private string _cpuText      = "—";
    [ObservableProperty] private string _memoryText   = "—";
    [ObservableProperty] private string _threadsText  = "—";
    [ObservableProperty] private string _managedHeapText = "—";
    [ObservableProperty] private string _statusText  = "Optimal";
    [ObservableProperty] private string _statusColor = "#00E676";
    [ObservableProperty] private string _reportText  = string.Empty;
    [ObservableProperty] private bool   _showReportDialog = false;

    public ObservableCollection<PerformanceMetric> Metrics { get; } = new();
    public ObservableCollection<PerformanceRecommendation> Recommendations { get; } = new();

    public PerformanceViewModel(IPerformanceMonitorService perfService)
    {
        _perfService = perfService ?? throw new ArgumentNullException(nameof(perfService));
        _ = LoadMetricsLoopAsync();
    }

    private async Task LoadMetricsLoopAsync()
    {
        while (true)
        {
            try
            {
                var snap = _perfService.GetCurrentSnapshot();
                var ops  = _perfService.GetOperationMetrics();
                var recs = _perfService.GetRecommendations();

                Dispatcher.UIThread.Post(() =>
                {
                    CpuText         = $"{snap.ProcessCpuPercentage:F1}%";
                    MemoryText      = ByteFormatter.FormatBytes(snap.WorkingSetBytes);
                    ManagedHeapText = ByteFormatter.FormatBytes(snap.ManagedMemoryBytes);
                    ThreadsText     = snap.ThreadCount.ToString();
                    IsMonitoring    = _perfService.IsMonitoringEnabled;

                    if (snap.ProcessCpuPercentage > 70)
                    {
                        StatusText  = "Elevated CPU";
                        StatusColor = "#FF5252";
                    }
                    else if (snap.WorkingSetBytes > 500 * 1024 * 1024)
                    {
                        StatusText  = "High Memory";
                        StatusColor = "#FF9800";
                    }
                    else
                    {
                        StatusText  = "Optimal";
                        StatusColor = "#00E676";
                    }

                    Metrics.Clear();
                    foreach (var o in ops) Metrics.Add(o);

                    Recommendations.Clear();
                    foreach (var r in recs) Recommendations.Add(r);
                });
            }
            catch { /* Ignore UI polling errors */ }

            await Task.Delay(2000);
        }
    }

    [RelayCommand]
    private void ToggleMonitoring()
    {
        if (_perfService.IsMonitoringEnabled)
            _perfService.PauseMonitoring();
        else
            _perfService.ResumeMonitoring();

        IsMonitoring = _perfService.IsMonitoringEnabled;
    }

    [RelayCommand]
    private void ClearHistory()
    {
        _perfService.ClearHistory();
        Metrics.Clear();
        Recommendations.Clear();
    }

    [RelayCommand]
    private void ExportReport()
    {
        ReportText = _perfService.GenerateReportSummary();
        ShowReportDialog = true;
    }

    [RelayCommand]
    private void CloseReportDialog()
    {
        ShowReportDialog = false;
    }
}
