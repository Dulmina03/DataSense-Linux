using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataSense.Models;
using DataSense.Services;

namespace DataSense.ViewModels;

public partial class DiagnosticsViewModel : ViewModelBase
{
    private readonly IDiagnosticsService _diagnosticsService;

    public override string Title => "Diagnostics & Troubleshooting";

    [ObservableProperty] private bool   _isLoading = false;
    [ObservableProperty] private string _overallStatusText = "Optimal";
    [ObservableProperty] private string _overallStatusColor = "#00E676";

    public ObservableCollection<DiagnosticComponent> Components { get; } = new();

    public DiagnosticsViewModel(IDiagnosticsService diagnosticsService)
    {
        _diagnosticsService = diagnosticsService ?? throw new ArgumentNullException(nameof(diagnosticsService));
        _ = LoadDiagnosticsAsync();
    }

    [RelayCommand]
    private async Task RefreshDiagnosticsAsync()
    {
        await LoadDiagnosticsAsync();
    }

    private async Task LoadDiagnosticsAsync()
    {
        IsLoading = true;
        try
        {
            var components = await _diagnosticsService.GetDiagnosticsAsync();
            
            Dispatcher.UIThread.Post(() =>
            {
                Components.Clear();
                bool hasError = false;
                bool hasDegraded = false;

                foreach (var c in components)
                {
                    Components.Add(c);
                    if (c.Status == SubsystemState.Error) hasError = true;
                    if (c.Status == SubsystemState.Degraded || c.Status == SubsystemState.Unavailable) hasDegraded = true;
                }

                if (hasError)
                {
                    OverallStatusText  = "Degraded / Attention Required";
                    OverallStatusColor = "#FF5252";
                }
                else if (hasDegraded)
                {
                    OverallStatusText  = "Operational (Some features unavailable)";
                    OverallStatusColor = "#FF9800";
                }
                else
                {
                    OverallStatusText  = "All Subsystems Healthy";
                    OverallStatusColor = "#00E676";
                }
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                OverallStatusText  = $"Diagnostic query failed: {ex.Message}";
                OverallStatusColor = "#FF5252";
            });
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsLoading = false);
        }
    }
}
