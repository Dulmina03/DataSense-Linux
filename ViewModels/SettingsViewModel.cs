using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataSense.Models;
using DataSense.Services;

namespace DataSense.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IForecastService _forecastService;

    public override string Title => "Settings";

    // ── Budget configuration properties ─────────────────────────────────────

    [ObservableProperty] private bool   _budgetEnabled          = false;
    [ObservableProperty] private double _monthlyLimitGb         = 100;
    [ObservableProperty] private bool   _dailyBudgetEnabled     = false;
    [ObservableProperty] private double _dailyLimitGb           = 5;
    [ObservableProperty] private int    _warningThreshold       = 75;
    [ObservableProperty] private int    _criticalThreshold      = 90;
    [ObservableProperty] private string _saveStatusText         = "";
    [ObservableProperty] private bool   _isSaving               = false;

    public SettingsViewModel(IForecastService forecastService)
    {
        _forecastService = forecastService;
        _ = LoadBudgetAsync();
    }

    private async Task LoadBudgetAsync()
    {
        try
        {
            var budget = await _forecastService.GetBudgetAsync();
            Dispatcher.UIThread.Post(() =>
            {
                BudgetEnabled      = budget.Enabled;
                MonthlyLimitGb     = budget.MonthlyLimitBytes > 0
                    ? Math.Round(budget.MonthlyLimitBytes / (1024.0 * 1024 * 1024), 1)
                    : 100;
                DailyBudgetEnabled = budget.DailyLimitBytes > 0;
                DailyLimitGb       = budget.DailyLimitBytes > 0
                    ? Math.Round(budget.DailyLimitBytes / (1024.0 * 1024 * 1024), 1)
                    : 5;
                WarningThreshold   = budget.WarningThresholdPct;
                CriticalThreshold  = budget.CriticalThresholdPct;
            });
        }
        catch { /* silently ignore on load */ }
    }

    [RelayCommand]
    private async Task SaveBudgetAsync()
    {
        IsSaving = true;
        SaveStatusText = "";

        try
        {
            var budget = new DataBudget
            {
                Enabled              = BudgetEnabled,
                MonthlyLimitBytes    = BudgetEnabled
                    ? (long)(MonthlyLimitGb * 1024 * 1024 * 1024)
                    : 0,
                DailyLimitBytes      = DailyBudgetEnabled
                    ? (long)(DailyLimitGb * 1024 * 1024 * 1024)
                    : 0,
                WarningThresholdPct  = WarningThreshold,
                CriticalThresholdPct = CriticalThreshold,
            };
            budget.Validate();
            await _forecastService.SaveBudgetAsync(budget);
            SaveStatusText = "✅ Budget settings saved.";
        }
        catch (Exception ex)
        {
            SaveStatusText = $"⚠️ Save failed: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task DisableBudgetAsync()
    {
        BudgetEnabled      = false;
        DailyBudgetEnabled = false;
        await SaveBudgetAsync();
        SaveStatusText = "Budget disabled.";
    }
}
