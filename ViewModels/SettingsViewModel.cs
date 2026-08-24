using System;
using System.Collections.Generic;
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

public enum SettingsCategory
{
    Appearance,
    Dashboard,
    Analytics,
    Network,
    Data,
    Notifications,
    DataLimits,
    System
}

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IForecastService _forecastService;
    private readonly ILinuxStartupService? _startupService;
    private readonly INetworkUsageRepository? _repository;
    private readonly IThemeService _themeService;

    public override string Title => "Settings";

    // ── Navigation ──────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAppearanceSelected))]
    [NotifyPropertyChangedFor(nameof(IsDashboardSelected))]
    [NotifyPropertyChangedFor(nameof(IsAnalyticsSelected))]
    [NotifyPropertyChangedFor(nameof(IsNetworkSelected))]
    [NotifyPropertyChangedFor(nameof(IsDataSelected))]
    [NotifyPropertyChangedFor(nameof(IsNotificationsSelected))]
    [NotifyPropertyChangedFor(nameof(IsDataLimitsSelected))]
    [NotifyPropertyChangedFor(nameof(IsSystemSelected))]
    [NotifyPropertyChangedFor(nameof(CategoryTitle))]
    [NotifyPropertyChangedFor(nameof(CategoryDescription))]
    private SettingsCategory _selectedCategory = SettingsCategory.Appearance;

    public bool IsAppearanceSelected => SelectedCategory == SettingsCategory.Appearance;
    public bool IsDashboardSelected => SelectedCategory == SettingsCategory.Dashboard;
    public bool IsAnalyticsSelected => SelectedCategory == SettingsCategory.Analytics;
    public bool IsNetworkSelected => SelectedCategory == SettingsCategory.Network;
    public bool IsDataSelected => SelectedCategory == SettingsCategory.Data;
    public bool IsNotificationsSelected => SelectedCategory == SettingsCategory.Notifications;
    public bool IsDataLimitsSelected => SelectedCategory == SettingsCategory.DataLimits;
    public bool IsSystemSelected => SelectedCategory == SettingsCategory.System;

    public string CategoryTitle => SelectedCategory switch
    {
        SettingsCategory.Appearance => "Appearance",
        SettingsCategory.Dashboard => "Dashboard",
        SettingsCategory.Analytics => "Analytics",
        SettingsCategory.Network => "Network",
        SettingsCategory.Data => "Data",
        SettingsCategory.Notifications => "Notifications",
        SettingsCategory.DataLimits => "Data Limits",
        SettingsCategory.System => "System",
        _ => "Settings"
    };

    public string CategoryDescription => SelectedCategory switch
    {
        SettingsCategory.Appearance => "Customize the look and feel of DataSense",
        SettingsCategory.Dashboard => "Customize dashboard widgets and layout",
        SettingsCategory.Analytics => "Configure charts and usage presentation",
        SettingsCategory.Network => "Configure network monitoring",
        SettingsCategory.Data => "Manage usage history and stored telemetry",
        SettingsCategory.Notifications => "Control DataSense alerts",
        SettingsCategory.DataLimits => "Configure daily and monthly usage limits",
        SettingsCategory.System => "Configure DataSense system behavior",
        _ => "Customize DataSense and manage application behavior"
    };

    // ── 1. Appearance ───────────────────────────────────────────────────────
    public IReadOnlyList<ThemeOption> AvailableThemes => _themeService.AvailableThemes;

    [ObservableProperty]
    private ThemeOption? _selectedTheme;

    [ObservableProperty] private bool _startAtLogin = false;
    [ObservableProperty] private bool _minimizeToTray = true;
    [ObservableProperty] private bool _launchOnStartup = false;
    [ObservableProperty] private bool _enableAnimations = true;

    // ── 2. Dashboard ────────────────────────────────────────────────────────
    [ObservableProperty] private bool _showSummaryCards = true;
    [ObservableProperty] private bool _showNetworkChart = true;
    [ObservableProperty] private bool _showApplicationUsage = true;
    [ObservableProperty] private bool _showNetworkInfo = true;

    public IReadOnlyList<string> PeriodOptions { get; } = new[] { "Today", "7 Days", "Month" };
    [ObservableProperty] private string _defaultDashboardPeriod = "Today";

    public IReadOnlyList<string> LayoutOptions { get; } = new[] { "Standard (Default)", "Compact Grid", "Expanded Flow" };
    [ObservableProperty] private string _cardLayout = "Standard (Default)";

    // ── 3. Analytics ────────────────────────────────────────────────────────
    [ObservableProperty] private bool _enableChartAnimations = true;
    [ObservableProperty] private bool _smoothGraphRendering = true;
    [ObservableProperty] private bool _showChartTooltips = true;

    public IReadOnlyList<string> DataUnitOptions { get; } = new[] { "Dynamic (Auto)", "Gigabytes (GB)", "Megabytes (MB)" };
    [ObservableProperty] private string _dataUnit = "Dynamic (Auto)";

    public IReadOnlyList<string> TransferRateUnitOptions { get; } = new[] { "MB/s", "KB/s", "Mbps" };
    [ObservableProperty] private string _transferRateUnit = "MB/s";

    // ── 4. Network ──────────────────────────────────────────────────────────
    [ObservableProperty] private bool _enableNetworkMonitoring = true;
    [ObservableProperty] private bool _monitorAppTraffic = true;
    [ObservableProperty] private bool _detectNetworkChanges = true;

    [ObservableProperty] private bool _autoDetectNetworkNames = true;
    [ObservableProperty] private bool _useWifiSsid = true;

    // ── 5. Data ─────────────────────────────────────────────────────────────
    public IReadOnlyList<string> RetentionOptions { get; } = new[] { "7 Days", "30 Days", "90 Days", "180 Days", "1 Year", "Forever" };
    [ObservableProperty] private string _historyRetention = "30 Days";

    public IReadOnlyList<string> RefreshIntervalOptions { get; } = new[] { "1 Second", "2 Seconds", "5 Seconds", "10 Seconds" };
    [ObservableProperty] private string _refreshInterval = "2 Seconds";

    [ObservableProperty] private string _databasePath = "";
    [ObservableProperty] private string _databaseSizeFormatted = "0 B";
    [ObservableProperty] private string _storedRecordsCountFormatted = "0 Records";

    [ObservableProperty] private bool _isConfirmingClearHistory = false;
    [ObservableProperty] private string _clearHistoryStatusText = "";

    // ── 6. Notifications ────────────────────────────────────────────────────
    [ObservableProperty] private bool _enableNotifications = true;
    [ObservableProperty] private bool _notificationSound = false;
    [ObservableProperty] private bool _notifyWhileMinimized = true;

    [ObservableProperty] private bool _notifyDailyLimit = true;
    [ObservableProperty] private bool _notifyMonthlyLimit = true;

    public IReadOnlyList<string> ThresholdOptions { get; } = new[] { "50%", "70%", "75%", "80%", "85%", "90%", "95%" };
    [ObservableProperty] private string _alertThreshold = "80%";

    // ── 7. Data Limits ──────────────────────────────────────────────────────
    [ObservableProperty] private bool _dailyBudgetEnabled = false;
    [ObservableProperty] private double _dailyLimitGb = 5;
    [ObservableProperty] private string _dailyAlertThreshold = "80%";
    [ObservableProperty] private string _todayUsageText = "0 B";
    [ObservableProperty] private string _dailyLimitText = "5 GB";
    [ObservableProperty] private double _dailyUsagePercent = 0.0;

    [ObservableProperty] private bool _budgetEnabled = false;
    [ObservableProperty] private double _monthlyLimitGb = 100;
    [ObservableProperty] private string _monthlyAlertThreshold = "80%";
    [ObservableProperty] private string _monthUsageText = "0 B";
    [ObservableProperty] private string _monthlyLimitText = "100 GB";
    [ObservableProperty] private double _monthlyUsagePercent = 0.0;

    [ObservableProperty] private int _warningThreshold = 80;
    [ObservableProperty] private int _criticalThreshold = 95;

    // ── 8. System ───────────────────────────────────────────────────────────
    [ObservableProperty] private bool _startWithSystem = false;
    [ObservableProperty] private bool _startMinimized = false;

    [ObservableProperty] private bool _showTrayIcon = true;
    [ObservableProperty] private bool _showLiveSpeedInTray = true;

    // ── Save & Status ───────────────────────────────────────────────────────
    [ObservableProperty] private string _saveStatusText = "";
    [ObservableProperty] private bool _isSaving = false;

    public SettingsViewModel(
        IForecastService forecastService,
        IThemeService? themeService = null,
        ILinuxStartupService? startupService = null,
        INetworkUsageRepository? repository = null)
    {
        _forecastService = forecastService ?? throw new ArgumentNullException(nameof(forecastService));
        _themeService = themeService ?? new ThemeService(repository);
        _startupService = startupService;
        _repository = repository;

        _selectedTheme = _themeService.CurrentTheme;

        _ = LoadSettingsAsync();
    }

    [RelayCommand]
    public void SelectCategory(SettingsCategory category)
    {
        SelectedCategory = category;
        IsConfirmingClearHistory = false;
    }

    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        if (value != null && value.Id != _themeService.CurrentThemeId)
        {
            _themeService.ApplyTheme(value.Id);
        }
    }

    partial void OnStartAtLoginChanged(bool value)
    {
        StartWithSystem = value;
        LaunchOnStartup = value;
    }

    partial void OnStartWithSystemChanged(bool value)
    {
        StartAtLogin = value;
        LaunchOnStartup = value;
    }

    partial void OnDailyLimitGbChanged(double value)
    {
        UpdateDataLimitDisplay();
    }

    partial void OnMonthlyLimitGbChanged(double value)
    {
        UpdateDataLimitDisplay();
    }

    public async Task LoadSettingsAsync()
    {
        try
        {
            string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DataSense");
            string dbPath = Path.Combine(appDataFolder, "datasense.db");
            DatabasePath = dbPath;

            long dbSizeBytes = 0;
            if (File.Exists(dbPath))
            {
                var fi = new FileInfo(dbPath);
                dbSizeBytes = fi.Length;
            }

            int totalRecords = 0;
            long todayDl = 0, todayUl = 0;
            long monthDl = 0, monthUl = 0;

            if (_repository != null)
            {
                totalRecords = await _repository.GetTotalRecordCountAsync();
                var todaySummary = await _repository.GetTodaySummaryAsync();
                todayDl = todaySummary.BytesDownloaded;
                todayUl = todaySummary.BytesUploaded;

                var monthSummary = await _repository.GetMonthSummaryAsync();
                monthDl = monthSummary.BytesDownloaded;
                monthUl = monthSummary.BytesUploaded;
            }

            var budget = await _forecastService.GetBudgetAsync();

            bool autostart = false;
            if (_startupService != null)
            {
                autostart = await _startupService.IsAutostartEnabledAsync();
            }

            string savedTheme = _themeService.CurrentThemeId;
            var matchedTheme = AvailableThemes.FirstOrDefault(t => t.Id == savedTheme) ?? AvailableThemes[0];

            RunOnUI(() =>
            {
                SelectedTheme = matchedTheme;

                DatabaseSizeFormatted = ByteFormatter.FormatBytes(dbSizeBytes);
                StoredRecordsCountFormatted = $"{totalRecords:N0} Records";

                BudgetEnabled = budget.Enabled;
                MonthlyLimitGb = budget.MonthlyLimitBytes > 0
                    ? Math.Round(budget.MonthlyLimitBytes / (1024.0 * 1024 * 1024), 1)
                    : 100;
                DailyBudgetEnabled = budget.DailyLimitBytes > 0;
                DailyLimitGb = budget.DailyLimitBytes > 0
                    ? Math.Round(budget.DailyLimitBytes / (1024.0 * 1024 * 1024), 1)
                    : 5;
                WarningThreshold = budget.WarningThresholdPct;
                CriticalThreshold = budget.CriticalThresholdPct;
                AlertThreshold = $"{budget.WarningThresholdPct}%";
                DailyAlertThreshold = $"{budget.WarningThresholdPct}%";
                MonthlyAlertThreshold = $"{budget.WarningThresholdPct}%";

                StartAtLogin = autostart;
                StartWithSystem = autostart;
                LaunchOnStartup = autostart;

                long todayUsage = todayDl + todayUl;
                long monthUsage = monthDl + monthUl;

                TodayUsageText = ByteFormatter.FormatBytes(todayUsage);
                MonthUsageText = ByteFormatter.FormatBytes(monthUsage);

                UpdateDataLimitDisplay(todayUsage, monthUsage);
            });
        }
        catch { }
    }

    private long _todayUsageBytes = 0;
    private long _monthUsageBytes = 0;

    private void UpdateDataLimitDisplay(long? todayUsageBytes = null, long? monthUsageBytes = null)
    {
        if (todayUsageBytes.HasValue) _todayUsageBytes = todayUsageBytes.Value;
        if (monthUsageBytes.HasValue) _monthUsageBytes = monthUsageBytes.Value;

        double dailyLimitBytes = DailyLimitGb * 1024.0 * 1024 * 1024;
        DailyLimitText = $"{DailyLimitGb:F1} GB";
        DailyUsagePercent = dailyLimitBytes > 0 ? Math.Min(100.0, ((double)_todayUsageBytes / dailyLimitBytes) * 100.0) : 0.0;

        double monthlyLimitBytes = MonthlyLimitGb * 1024.0 * 1024 * 1024;
        MonthlyLimitText = $"{MonthlyLimitGb:F1} GB";
        MonthlyUsagePercent = monthlyLimitBytes > 0 ? Math.Min(100.0, ((double)_monthUsageBytes / monthlyLimitBytes) * 100.0) : 0.0;
    }

    private static void RunOnUI(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    [RelayCommand]
    public void RequestClearHistory()
    {
        IsConfirmingClearHistory = true;
        ClearHistoryStatusText = "";
    }

    [RelayCommand]
    public void CancelClearHistory()
    {
        IsConfirmingClearHistory = false;
        ClearHistoryStatusText = "";
    }

    [RelayCommand]
    public async Task ConfirmClearHistoryAsync()
    {
        try
        {
            if (_repository != null)
            {
                await _repository.ClearAllHistoryAsync();
                ClearHistoryStatusText = "✅ Historical network records cleared successfully.";
                await LoadSettingsAsync();
            }
        }
        catch (Exception ex)
        {
            ClearHistoryStatusText = $"⚠️ Failed to clear history: {ex.Message}";
        }
        finally
        {
            IsConfirmingClearHistory = false;
        }
    }

    [RelayCommand]
    public async Task SaveSettingsAsync()
    {
        IsSaving = true;
        SaveStatusText = "";

        try
        {
            int thresh = int.TryParse(AlertThreshold.TrimEnd('%'), out int tVal) ? tVal : WarningThreshold;

            var budget = new DataBudget
            {
                Enabled = BudgetEnabled,
                MonthlyLimitBytes = BudgetEnabled ? (long)(MonthlyLimitGb * 1024 * 1024 * 1024) : 0,
                DailyLimitBytes = DailyBudgetEnabled ? (long)(DailyLimitGb * 1024 * 1024 * 1024) : 0,
                WarningThresholdPct = thresh,
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
                await _repository.SaveSettingAsync("NotifyDailyLimit", NotifyDailyLimit.ToString());
                await _repository.SaveSettingAsync("NotifyMonthlyLimit", NotifyMonthlyLimit.ToString());
                await _repository.SaveSettingAsync("MinimizeToTray", MinimizeToTray.ToString());
                await _repository.SaveSettingAsync("EnableAnimations", EnableAnimations.ToString());
                await _repository.SaveSettingAsync("DefaultDashboardPeriod", DefaultDashboardPeriod);
                await _repository.SaveSettingAsync("HistoryRetention", HistoryRetention);
                await _repository.SaveSettingAsync("RefreshInterval", RefreshInterval);
                await _repository.SaveSettingAsync("DataUnit", DataUnit);
                await _repository.SaveSettingAsync("TransferRateUnit", TransferRateUnit);
                await _repository.SaveSettingAsync("AutoDetectNetworkNames", AutoDetectNetworkNames.ToString());
                await _repository.SaveSettingAsync("UseWifiSsid", UseWifiSsid.ToString());
            }

            SaveStatusText = "✅ Settings saved successfully.";
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
}
