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
    private void NavigateToSettings()
    {
        SetCurrentPage(App.Services?.GetRequiredService<SettingsViewModel>() ?? new SettingsViewModel());
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

    private void SetCurrentPage(ViewModelBase page)
    {
        CurrentPage = page;
        
        IsDashboardActive = page is DashboardViewModel;
        IsHistoryActive = page is HistoryViewModel;
        IsSpeedTestActive = page is SpeedTestViewModel;
        IsSettingsActive = page is SettingsViewModel;
        IsAboutActive = page is AboutViewModel;
    }
}
