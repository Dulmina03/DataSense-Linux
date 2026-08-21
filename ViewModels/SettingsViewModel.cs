using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataSense.Database;
using DataSense.Models;
using DataSense.Services;

namespace DataSense.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IForecastService _forecastService;
    private readonly ILinuxStartupService? _startupService;
    private readonly INetworkUsageRepository? _repository;

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

    // ── Linux Desktop Integration & Notification Settings ─────────────────────
    [ObservableProperty] private bool   _startAtLogin           = false;
    [ObservableProperty] private bool   _enableNotifications    = true;
    [ObservableProperty] private bool   _notifyBudgetAlerts     = true;
    [ObservableProperty] private bool   _notifyNetworkAlerts    = true;
    [ObservableProperty] private bool   _notifyDiagnosticsAlerts = true;
    [ObservableProperty] private bool   _notifyBackupAlerts     = true;
    [ObservableProperty] private bool   _notifyAnomalyAlerts    = true;

    public SettingsViewModel(
        IForecastService forecastService,
        ILinuxStartupService? startupService = null,
        INetworkUsageRepository? repository = null)
    {
        _forecastService = forecastService ?? throw new ArgumentNullException(nameof(forecastService));
        _startupService  = startupService;
        _repository       = repository;

        _ = LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            var budget = await _forecastService.GetBudgetAsync();
            
            bool autostart = false;
            if (_startupService != null)
            {
                autostart = await _startupService.IsAutostartEnabledAsync();
            }

            bool enableNotifs = true;
            bool budgetNotifs = true;
            bool netNotifs = true;
            bool diagNotifs = true;
            bool backupNotifs = true;
            bool anomalyNotifs = true;

            if (_repository != null)
            {
                string? notifStr = await _repository.GetSettingAsync("EnableDesktopNotifications");
                if (bool.TryParse(notifStr, out bool notifVal)) enableNotifs = notifVal;

                string? bStr = await _repository.GetSettingAsync("NotifyBudgetAlerts");
                if (bool.TryParse(bStr, out bool bVal)) budgetNotifs = bVal;

                string? nStr = await _repository.GetSettingAsync("NotifyNetworkAlerts");
                if (bool.TryParse(nStr, out bool nVal)) netNotifs = nVal;

                string? dStr = await _repository.GetSettingAsync("NotifyDiagnosticsAlerts");
                if (bool.TryParse(dStr, out bool dVal)) diagNotifs = dVal;

                string? bkStr = await _repository.GetSettingAsync("NotifyBackupAlerts");
                if (bool.TryParse(bkStr, out bool bkVal)) backupNotifs = bkVal;

                string? aStr = await _repository.GetSettingAsync("NotifyAnomalyAlerts");
                if (bool.TryParse(aStr, out bool aVal)) anomalyNotifs = aVal;
            }

            Dispatcher.UIThread.Post(() =>
            {
                BudgetEnabled           = budget.Enabled;
                MonthlyLimitGb          = budget.MonthlyLimitBytes > 0
                    ? Math.Round(budget.MonthlyLimitBytes / (1024.0 * 1024 * 1024), 1)
                    : 100;
                DailyBudgetEnabled      = budget.DailyLimitBytes > 0;
                DailyLimitGb            = budget.DailyLimitBytes > 0
                    ? Math.Round(budget.DailyLimitBytes / (1024.0 * 1024 * 1024), 1)
                    : 5;
                WarningThreshold        = budget.WarningThresholdPct;
                CriticalThreshold       = budget.CriticalThresholdPct;

                StartAtLogin            = autostart;
                EnableNotifications     = enableNotifs;
                NotifyBudgetAlerts      = budgetNotifs;
                NotifyNetworkAlerts     = netNotifs;
                NotifyDiagnosticsAlerts = diagNotifs;
                NotifyBackupAlerts      = backupNotifs;
                NotifyAnomalyAlerts     = anomalyNotifs;
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

            if (_startupService != null)
            {
                await _startupService.SetAutostartEnabledAsync(StartAtLogin);
            }

            if (_repository != null)
            {
                await _repository.SaveSettingAsync("EnableDesktopNotifications", EnableNotifications.ToString());
                await _repository.SaveSettingAsync("NotifyBudgetAlerts", NotifyBudgetAlerts.ToString());
                await _repository.SaveSettingAsync("NotifyNetworkAlerts", NotifyNetworkAlerts.ToString());
                await _repository.SaveSettingAsync("NotifyDiagnosticsAlerts", NotifyDiagnosticsAlerts.ToString());
                await _repository.SaveSettingAsync("NotifyBackupAlerts", NotifyBackupAlerts.ToString());
                await _repository.SaveSettingAsync("NotifyAnomalyAlerts", NotifyAnomalyAlerts.ToString());
            }

            SaveStatusText = "✅ Application settings saved successfully.";
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
