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
    private bool _isAboutActive;

    [ObservableProperty]
    private bool _isSpeedTestActive;

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
    private void NavigateToAbout()
    {
        SetCurrentPage(App.Services?.GetRequiredService<AboutViewModel>() ?? new AboutViewModel());
    }

    [RelayCommand]
    private void NavigateToSpeedTest()
    {
        SetCurrentPage(App.Services?.GetRequiredService<SpeedTestViewModel>() ?? throw new InvalidOperationException("SpeedTestViewModel resolution failed"));
    }

    public void NavigateToApplicationAnalytics(string processName)
    {
        if (App.Services != null)
        {
            var vm = App.Services.GetRequiredService<ApplicationAnalyticsViewModel>();
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

    private void SetCurrentPage(ViewModelBase page)
    {
        CurrentPage = page;
        
        IsDashboardActive = page is DashboardViewModel;
        IsHistoryActive    = page is HistoryViewModel;
        IsExplorerActive   = page is HistoricalExplorerViewModel;
        IsSpeedTestActive = page is SpeedTestViewModel;
        IsSettingsActive  = page is SettingsViewModel;
        IsAboutActive     = page is AboutViewModel;
    }
}
