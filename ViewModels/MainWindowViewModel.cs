using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace DataSense.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViewModelBase _currentPage;

    [ObservableProperty]
    private bool _isDashboardActive;

    [ObservableProperty]
    private bool _isHistoryActive;

    [ObservableProperty]
    private bool _isExplorerActive;

    [ObservableProperty]
    private bool _isSettingsActive;

    [ObservableProperty]
    private bool _isPrivacyActive;

    [ObservableProperty]
    private bool _isDiagnosticsActive;

    [ObservableProperty]
    private bool _isPerformanceActive;

    [ObservableProperty]
    private bool _isExportActive;

    [ObservableProperty]
    private bool _isEventCenterActive;

    [ObservableProperty]
    private bool _isImportRestoreActive;

    [ObservableProperty]
    private bool _isBackupRecoveryActive;

    [ObservableProperty]
    private bool _isAboutActive;

    [ObservableProperty]
    private bool _isSpeedTestActive;

    [ObservableProperty]
    private bool _isLiveMonitoringActive;

    [ObservableProperty]
    private bool _isTimelineActive;
    
    [ObservableProperty]
    private bool _isUnifiedIntelligenceActive;

    [ObservableProperty]
    private bool _isSidebarExpanded = true;

    public MainWindowViewModel()
    {
        // Default to Dashboard
        _currentPage = null!;
        NavigateToDashboard();
    }

    [RelayCommand]
    private void NavigateToDashboard()
    {
        if (App.Services != null)
        {
            SetCurrentPage(App.Services.GetRequiredService<DashboardViewModel>());
        }
    }

    [RelayCommand]
    private void NavigateToHistory()
    {
        SetCurrentPage(App.Services?.GetRequiredService<HistoryViewModel>() ?? throw new InvalidOperationException("HistoryViewModel resolution failed"));
    }

    [RelayCommand]
    private void NavigateToExplorer()
    {
        SetCurrentPage(App.Services?.GetRequiredService<HistoricalExplorerViewModel>() ?? throw new InvalidOperationException("HistoricalExplorerViewModel resolution failed"));
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        SetCurrentPage(App.Services?.GetRequiredService<SettingsViewModel>() ?? throw new InvalidOperationException("SettingsViewModel resolution failed"));
    }

    [RelayCommand]
    private void NavigateToPrivacy()
    {
        SetCurrentPage(App.Services?.GetRequiredService<PrivacyViewModel>() ?? throw new InvalidOperationException("PrivacyViewModel resolution failed"));
    }

    [RelayCommand]
    private void NavigateToDiagnostics()
    {
        SetCurrentPage(App.Services?.GetRequiredService<DiagnosticsViewModel>() ?? throw new InvalidOperationException("DiagnosticsViewModel resolution failed"));
    }

    [RelayCommand]
    private void NavigateToPerformance()
    {
        SetCurrentPage(App.Services?.GetRequiredService<PerformanceViewModel>() ?? throw new InvalidOperationException("PerformanceViewModel resolution failed"));
    }

    [RelayCommand]
    private void NavigateToExport()
    {
        SetCurrentPage(App.Services?.GetRequiredService<ExportViewModel>() ?? throw new InvalidOperationException("ExportViewModel resolution failed"));
    }

    [RelayCommand]
    private void NavigateToEventCenter()
    {
        SetCurrentPage(App.Services?.GetRequiredService<EventCenterViewModel>() ?? throw new InvalidOperationException("EventCenterViewModel resolution failed"));
    }

    [RelayCommand]
    private void NavigateToImportRestore()
    {
        SetCurrentPage(App.Services?.GetRequiredService<ImportRestoreViewModel>() ?? throw new InvalidOperationException("ImportRestoreViewModel resolution failed"));
    }

    [RelayCommand]
    private void NavigateToBackupRecovery()
    {
        SetCurrentPage(App.Services?.GetRequiredService<BackupRecoveryViewModel>() ?? throw new InvalidOperationException("BackupRecoveryViewModel resolution failed"));
    }

    [RelayCommand]
    private void NavigateToAbout()
    {
        SetCurrentPage(App.Services?.GetRequiredService<AboutViewModel>() ?? new AboutViewModel());
    }

    [RelayCommand]
    private void NavigateToSpeedTest()
    {
        SetCurrentPage(App.Services?.GetRequiredService<SpeedTestViewModel>() ?? throw new InvalidOperationException("SpeedTestViewModel resolution failed"));
    }

    [RelayCommand]
    private void NavigateToLiveMonitoring()
    {
        SetCurrentPage(App.Services?.GetRequiredService<LiveMonitoringViewModel>() ?? throw new InvalidOperationException("LiveMonitoringViewModel resolution failed"));
    }

    [RelayCommand]
    private void NavigateToTimeline()
    {
        SetCurrentPage(App.Services?.GetRequiredService<NetworkActivityTimelineViewModel>() ?? throw new InvalidOperationException("NetworkActivityTimelineViewModel resolution failed"));
    }

    public void NavigateToApplicationAnalytics(string processName, int pid = 0, long startTimeTicks = 0)
    {
        if (App.Services != null)
        {
            var vm = App.Services.GetRequiredService<ApplicationAnalyticsViewModel>();
            if (pid > 0 && startTimeTicks > 0)
                vm.Initialize(processName, pid, startTimeTicks);
            else
                vm.Initialize(processName);
            SetCurrentPage(vm);
        }
    }

    public void NavigateToNetworkAnalytics(string? networkName = null)
    {
        if (App.Services != null)
        {
            var vm = App.Services.GetRequiredService<NetworkAnalyticsViewModel>();
            vm.Initialize(networkName);
            SetCurrentPage(vm);
        }
    }

    [RelayCommand]
    public void NavigateToUnifiedIntelligence()
    {
        if (App.Services != null)
        {
            var vm = App.Services.GetRequiredService<UnifiedIntelligenceViewModel>();
            SetCurrentPage(vm);
        }
    }

    private void SetCurrentPage(ViewModelBase page)
    {
        CurrentPage = page;
        
        IsDashboardActive   = page is DashboardViewModel;
        IsHistoryActive     = page is HistoryViewModel;
        IsUnifiedIntelligenceActive = page is UnifiedIntelligenceViewModel;
        IsExplorerActive    = page is HistoricalExplorerViewModel;
        IsSpeedTestActive   = page is SpeedTestViewModel;
        IsLiveMonitoringActive = page is LiveMonitoringViewModel;
        IsTimelineActive    = page is NetworkActivityTimelineViewModel;
        IsSettingsActive    = page is SettingsViewModel;
        IsPrivacyActive     = page is PrivacyViewModel;
        IsDiagnosticsActive = page is DiagnosticsViewModel;
        IsPerformanceActive = page is PerformanceViewModel;
        IsExportActive        = page is ExportViewModel;
        IsEventCenterActive   = page is EventCenterViewModel;
        IsImportRestoreActive  = page is ImportRestoreViewModel;
        IsBackupRecoveryActive = page is BackupRecoveryViewModel;
        IsAboutActive          = page is AboutViewModel;
    }
}
