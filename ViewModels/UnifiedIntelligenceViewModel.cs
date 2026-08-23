using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataSense.Models;
using DataSense.Services;

namespace DataSense.ViewModels;

public partial class UnifiedIntelligenceViewModel : ViewModelBase
{
    private readonly IUnifiedAnalyticsIntelligenceService _intelligenceService;
    
    public override string Title => "Unified Intelligence Center";

    [ObservableProperty] private UnifiedSystemSummary _systemSummary = new();
    
    public ObservableCollection<UnifiedInsight> AllInsights { get; } = new();
    public ObservableCollection<UnifiedInsight> FilteredInsights { get; } = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _categoryFilter = "All";
    [ObservableProperty] private string _severityFilter = "All";

    public List<string> Categories { get; } = new() { "All", "Application", "Network", "Usage", "Budget", "Forecast", "Anomaly", "Performance", "CrossDomain" };
    public List<string> Severities { get; } = new() { "All", "Info", "Success", "Warning", "Critical" };

    public UnifiedIntelligenceViewModel(IUnifiedAnalyticsIntelligenceService intelligenceService)
    {
        _intelligenceService = intelligenceService;
        _ = LoadDataAsync();
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            SystemSummary = await _intelligenceService.GetSystemSummaryAsync();
            var insights = await _intelligenceService.GetUnifiedInsightsAsync();
            
            AllInsights.Clear();
            foreach (var insight in insights)
            {
                AllInsights.Add(insight);
            }
            ApplyFilters();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void RefreshData()
    {
        _intelligenceService.InvalidateCache();
        _ = LoadDataAsync();
    }

    private void ApplyFilters()
    {
        FilteredInsights.Clear();
        foreach (var insight in AllInsights)
        {
            bool categoryMatch = CategoryFilter == "All" || insight.Category.ToString() == CategoryFilter;
            bool severityMatch = SeverityFilter == "All" || insight.Severity.ToString() == SeverityFilter;
            
            if (categoryMatch && severityMatch)
            {
                FilteredInsights.Add(insight);
            }
        }
    }
    
    partial void OnCategoryFilterChanged(string value) => ApplyFilters();
    partial void OnSeverityFilterChanged(string value) => ApplyFilters();
}
