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
        SetCurrentPage(App.Services?.GetRequiredService<HistoryViewModel>() ?? new HistoryViewModel());
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

    private void SetCurrentPage(ViewModelBase page)
    {
        CurrentPage = page;
        
        IsDashboardActive = page is DashboardViewModel;
        IsHistoryActive = page is HistoryViewModel;
        IsSettingsActive = page is SettingsViewModel;
        IsAboutActive = page is AboutViewModel;
    }
}
