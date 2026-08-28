using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataSense.Database;
using DataSense.Helpers;
using DataSense.Models;
using DataSense.Services;
using Microsoft.Extensions.DependencyInjection;

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

public partial class SettingsViewModel : ViewModelBase, IDisposable
{
    private readonly IForecastService _forecastService;
    private readonly ILinuxStartupService? _startupService;
    private readonly INetworkUsageRepository? _repository;
    private readonly IThemeService _themeService;
    private readonly ITopBarSpeedMeterService? _topBarSpeedMeterService;
    private CancellationTokenSource? _saveStatusCancellation;

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
    [ObservableProperty] private bool _showTopConsumers = true;
    [ObservableProperty] private bool _showLiveProcessTraffic = true;
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

    // ── Top Bar Speed Meter ─────────────────────────────────────────────────
    [ObservableProperty] private bool _showNetworkSpeedMeter = false;
    [ObservableProperty] private bool _showMeterDownload = true;
    [ObservableProperty] private bool _showMeterUpload = true;
    [ObservableProperty] private bool _showMeterIcons = true;
    [ObservableProperty] private bool _meterCompactMode = true;
    [ObservableProperty] private string _meterUnits = "Auto";
    [ObservableProperty] private string _meterPrecision = "1 decimal";
    [ObservableProperty] private string _meterRefreshRate = "1 second";
    [ObservableProperty] private string _meterColorMode = "Theme colors";
    [ObservableProperty] private string _meterSingleColor = "#d8e4f2";
    [ObservableProperty] private string _meterDownloadColor = "#62d2a2";
    [ObservableProperty] private string _meterUploadColor = "#f4b860";
    [ObservableProperty] private string _meterSize = "Medium";
    [ObservableProperty] private string _meterFontWeight = "Normal";
    [ObservableProperty] private string _meterPosition = "Right area";
    [ObservableProperty] private string _meterClickAction = "Open Dashboard";
    [ObservableProperty] private bool _meterShowDetailsOnHover = true;

    public IReadOnlyList<string> MeterUnitOptions { get; } = new[] { "Auto", "B/s", "KB/s", "MB/s", "GB/s", "bits/s", "Kbit/s", "Mbit/s", "Gbit/s" };
    public IReadOnlyList<string> MeterPrecisionOptions { get; } = new[] { "0 decimals", "1 decimal", "2 decimals" };
    public IReadOnlyList<string> MeterRefreshRateOptions { get; } = new[] { "250 ms", "500 ms", "1 second", "2 seconds", "5 seconds" };
    public IReadOnlyList<string> MeterColorModeOptions { get; } = new[] { "Theme colors", "Single color", "Separate colors" };
    public IReadOnlyList<string> MeterSizeOptions { get; } = new[] { "Small", "Medium", "Large" };
    public IReadOnlyList<string> MeterFontWeightOptions { get; } = new[] { "Normal", "Medium", "Bold" };
    public IReadOnlyList<string> MeterPositionOptions { get; } = new[] { "Left area", "Center area", "Right area" };
    public IReadOnlyList<string> MeterClickActionOptions { get; } = new[] { "Open DataSense", "Open Dashboard", "Open Network Analytics", "Do nothing" };

    // ── Save & Status ───────────────────────────────────────────────────────
    [ObservableProperty] private string _saveStatusText = "";
    [ObservableProperty] private bool _isSaving = false;

    public SettingsViewModel(
        IForecastService forecastService,
        IThemeService? themeService = null,
        ILinuxStartupService? startupService = null,
        INetworkUsageRepository? repository = null,
        ITopBarSpeedMeterService? topBarSpeedMeterService = null)
    {
        _forecastService = forecastService ?? throw new ArgumentNullException(nameof(forecastService));
        _themeService = themeService ?? new ThemeService(repository);
        _startupService = startupService;
        _repository = repository;
        _topBarSpeedMeterService = topBarSpeedMeterService;

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

    partial void OnShowSummaryCardsChanged(bool value) => ApplyDashboardPreferencesToRuntime();
    partial void OnShowNetworkChartChanged(bool value) => ApplyDashboardPreferencesToRuntime();
    partial void OnShowTopConsumersChanged(bool value) => ApplyDashboardPreferencesToRuntime();
    partial void OnShowLiveProcessTrafficChanged(bool value) => ApplyDashboardPreferencesToRuntime();
    partial void OnShowApplicationUsageChanged(bool value) => ApplyDashboardPreferencesToRuntime();
    partial void OnShowNetworkInfoChanged(bool value) => ApplyDashboardPreferencesToRuntime();
    partial void OnCardLayoutChanged(string value) => ApplyDashboardPreferencesToRuntime();
    partial void OnDataUnitChanged(string value) => ApplyDashboardPreferencesToRuntime();
    partial void OnTransferRateUnitChanged(string value) => ApplyDashboardPreferencesToRuntime();
    partial void OnDefaultDashboardPeriodChanged(string value) => ApplyDashboardPeriodToRuntime(value);

    partial void OnSmoothGraphRenderingChanged(bool value) => ApplyDashboardPreferencesToRuntime();
    partial void OnShowChartTooltipsChanged(bool value) => ApplyDashboardPreferencesToRuntime();

    private void ApplyDashboardPreferencesToRuntime()
    {
        if (App.Services?.GetService<DashboardViewModel>() is DashboardViewModel dashboard)
        {
            dashboard.ApplyDashboardPreferences(
                ShowSummaryCards,
                ShowNetworkChart,
                ShowTopConsumers,
                ShowLiveProcessTraffic,
                ShowApplicationUsage,
                ShowNetworkInfo,
                CardLayout,
                DataUnit,
                TransferRateUnit,
                SmoothGraphRendering,
                ShowChartTooltips);
        }
    }

    private void ApplyDashboardPeriodToRuntime(string value)
    {
        if (App.Services?.GetService<DashboardViewModel>() is DashboardViewModel dashboard)
        {
            _ = dashboard.ApplyDefaultPeriodAsync(value);
        }
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

            bool sHero = true, sNet = true, sTop = true, sProc = true, sApp = true, sInfo = true;
            bool enableChartAnimations = EnableChartAnimations;
            bool smoothGraphRendering = SmoothGraphRendering;
            bool showChartTooltips = ShowChartTooltips;
            bool enableNetworkMonitoring = EnableNetworkMonitoring;
            bool monitorAppTraffic = MonitorAppTraffic;
            bool detectNetworkChanges = DetectNetworkChanges;
            bool notificationSound = NotificationSound;
            bool notifyWhileMinimized = NotifyWhileMinimized;
            bool enableNotifications = EnableNotifications;
            bool showTrayIcon = ShowTrayIcon;
            bool showLiveSpeedInTray = ShowLiveSpeedInTray;
            string cardLayout = CardLayout;
            if (_repository != null)
            {
                totalRecords = await _repository.GetTotalRecordCountAsync();
                var todaySummary = await _repository.GetTodaySummaryAsync();
                todayDl = todaySummary.BytesDownloaded;
                todayUl = todaySummary.BytesUploaded;

                var monthSummary = await _repository.GetMonthSummaryAsync();
                monthDl = monthSummary.BytesDownloaded;
                monthUl = monthSummary.BytesUploaded;

                var sHeroVal = await _repository.GetSettingAsync("ShowSummaryCards");
                if (bool.TryParse(sHeroVal, out bool bHero)) sHero = bHero;

                var sNetVal = await _repository.GetSettingAsync("ShowNetworkChart");
                if (bool.TryParse(sNetVal, out bool bNet)) sNet = bNet;

                var sTopVal = await _repository.GetSettingAsync("ShowTopConsumers");
                if (bool.TryParse(sTopVal, out bool bTop)) sTop = bTop;

                var sProcVal = await _repository.GetSettingAsync("ShowLiveProcessTraffic");
                if (bool.TryParse(sProcVal, out bool bProc)) sProc = bProc;

                var sAppVal = await _repository.GetSettingAsync("ShowApplicationUsage");
                if (bool.TryParse(sAppVal, out bool bApp)) sApp = bApp;

                var sInfoVal = await _repository.GetSettingAsync("ShowNetworkInfo");
                if (bool.TryParse(sInfoVal, out bool bInfo)) sInfo = bInfo;

                enableChartAnimations = await ReadBoolSettingAsync("EnableChartAnimations", enableChartAnimations);
                smoothGraphRendering = await ReadBoolSettingAsync("SmoothGraphRendering", smoothGraphRendering);
                showChartTooltips = await ReadBoolSettingAsync("ShowChartTooltips", showChartTooltips);
                enableNetworkMonitoring = await ReadBoolSettingAsync("EnableNetworkMonitoring", enableNetworkMonitoring);
                monitorAppTraffic = await ReadBoolSettingAsync("MonitorAppTraffic", monitorAppTraffic);
                detectNetworkChanges = await ReadBoolSettingAsync("DetectNetworkChanges", detectNetworkChanges);
                notificationSound = await ReadBoolSettingAsync("NotificationSound", notificationSound);
                notifyWhileMinimized = await ReadBoolSettingAsync("NotifyWhileMinimized", notifyWhileMinimized);
                enableNotifications = await ReadBoolSettingAsync("EnableDesktopNotifications", enableNotifications);
                showTrayIcon = await ReadBoolSettingAsync("ShowTrayIcon", showTrayIcon);
                showLiveSpeedInTray = await ReadBoolSettingAsync("ShowLiveSpeedInTray", showLiveSpeedInTray);
                cardLayout = await ReadStringSettingAsync("CardLayout", cardLayout);

                ShowNetworkSpeedMeter = await ReadBoolSettingAsync("ShowNetworkSpeedMeter", false);
                ShowMeterDownload = await ReadBoolSettingAsync("ShowMeterDownload", true);
                ShowMeterUpload = await ReadBoolSettingAsync("ShowMeterUpload", true);
                ShowMeterIcons = await ReadBoolSettingAsync("ShowMeterIcons", true);
                MeterCompactMode = await ReadBoolSettingAsync("MeterCompactMode", true);
                MeterShowDetailsOnHover = await ReadBoolSettingAsync("MeterShowDetailsOnHover", true);
                MeterUnits = await ReadStringSettingAsync("MeterUnits", "Auto");
                MeterPrecision = await ReadStringSettingAsync("MeterPrecision", "1 decimal");
                MeterRefreshRate = await ReadStringSettingAsync("MeterRefreshRate", "1 second");
                MeterColorMode = await ReadStringSettingAsync("MeterColorMode", "Theme colors");
                MeterSingleColor = await ReadStringSettingAsync("MeterSingleColor", "#d8e4f2");
                MeterDownloadColor = await ReadStringSettingAsync("MeterDownloadColor", "#62d2a2");
                MeterUploadColor = await ReadStringSettingAsync("MeterUploadColor", "#f4b860");
                MeterSize = await ReadStringSettingAsync("MeterSize", "Medium");
                MeterFontWeight = await ReadStringSettingAsync("MeterFontWeight", "Normal");
                MeterPosition = await ReadStringSettingAsync("MeterPosition", "Right area");
                MeterClickAction = await ReadStringSettingAsync("MeterClickAction", "Open Dashboard");
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

                ShowSummaryCards = sHero;
                ShowNetworkChart = sNet;
                ShowTopConsumers = sTop;
                ShowLiveProcessTraffic = sProc;
                ShowApplicationUsage = sApp;
                ShowNetworkInfo = sInfo;
                EnableChartAnimations = enableChartAnimations;
                SmoothGraphRendering = smoothGraphRendering;
                ShowChartTooltips = showChartTooltips;
                EnableNetworkMonitoring = enableNetworkMonitoring;
                MonitorAppTraffic = monitorAppTraffic;
                DetectNetworkChanges = detectNetworkChanges;
                NotificationSound = notificationSound;
                NotifyWhileMinimized = notifyWhileMinimized;
                EnableNotifications = enableNotifications;
                ShowTrayIcon = showTrayIcon;
                ShowLiveSpeedInTray = showLiveSpeedInTray;
                CardLayout = cardLayout;
                ApplyDashboardPreferencesToRuntime();

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
                await _repository.SaveSettingAsync("ShowSummaryCards", ShowSummaryCards.ToString());
                await _repository.SaveSettingAsync("ShowNetworkChart", ShowNetworkChart.ToString());
                await _repository.SaveSettingAsync("ShowTopConsumers", ShowTopConsumers.ToString());
                await _repository.SaveSettingAsync("ShowLiveProcessTraffic", ShowLiveProcessTraffic.ToString());
                await _repository.SaveSettingAsync("ShowApplicationUsage", ShowApplicationUsage.ToString());
                await _repository.SaveSettingAsync("ShowNetworkInfo", ShowNetworkInfo.ToString());
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
                await _repository.SaveSettingAsync("EnableChartAnimations", EnableChartAnimations.ToString());
                await _repository.SaveSettingAsync("SmoothGraphRendering", SmoothGraphRendering.ToString());
                await _repository.SaveSettingAsync("ShowChartTooltips", ShowChartTooltips.ToString());
                await _repository.SaveSettingAsync("EnableNetworkMonitoring", EnableNetworkMonitoring.ToString());
                await _repository.SaveSettingAsync("MonitorAppTraffic", MonitorAppTraffic.ToString());
                await _repository.SaveSettingAsync("DetectNetworkChanges", DetectNetworkChanges.ToString());
                await _repository.SaveSettingAsync("NotificationSound", NotificationSound.ToString());
                await _repository.SaveSettingAsync("NotifyWhileMinimized", NotifyWhileMinimized.ToString());
                await _repository.SaveSettingAsync("ShowTrayIcon", ShowTrayIcon.ToString());
                await _repository.SaveSettingAsync("ShowLiveSpeedInTray", ShowLiveSpeedInTray.ToString());
                await _repository.SaveSettingAsync("CardLayout", CardLayout);
                await _repository.SaveSettingAsync("ShowNetworkSpeedMeter", ShowNetworkSpeedMeter.ToString());
                await _repository.SaveSettingAsync("ShowMeterDownload", ShowMeterDownload.ToString());
                await _repository.SaveSettingAsync("ShowMeterUpload", ShowMeterUpload.ToString());
                await _repository.SaveSettingAsync("ShowMeterIcons", ShowMeterIcons.ToString());
                await _repository.SaveSettingAsync("MeterCompactMode", MeterCompactMode.ToString());
                await _repository.SaveSettingAsync("MeterShowDetailsOnHover", MeterShowDetailsOnHover.ToString());
                await _repository.SaveSettingAsync("MeterUnits", MeterUnits);
                await _repository.SaveSettingAsync("MeterPrecision", MeterPrecision);
                await _repository.SaveSettingAsync("MeterRefreshRate", MeterRefreshRate);
                await _repository.SaveSettingAsync("MeterColorMode", MeterColorMode);
                await _repository.SaveSettingAsync("MeterSingleColor", MeterSingleColor);
                await _repository.SaveSettingAsync("MeterDownloadColor", MeterDownloadColor);
                await _repository.SaveSettingAsync("MeterUploadColor", MeterUploadColor);
                await _repository.SaveSettingAsync("MeterSize", MeterSize);
                await _repository.SaveSettingAsync("MeterFontWeight", MeterFontWeight);
                await _repository.SaveSettingAsync("MeterPosition", MeterPosition);
                await _repository.SaveSettingAsync("MeterClickAction", MeterClickAction);
            }

            var extensionSynchronized = true;
            if (_topBarSpeedMeterService != null)
            {
                extensionSynchronized = await _topBarSpeedMeterService.RefreshConfigurationAsync();
            }

            if (App.Services?.GetService(typeof(DashboardViewModel)) is DashboardViewModel dashboard)
            {
                await dashboard.LoadDashboardPreferencesAsync();
            }

            SaveStatusText = extensionSynchronized
                ? "Settings saved successfully."
                : "Settings saved, but the GNOME Speed Meter is unavailable.";
            _saveStatusCancellation?.Cancel();
            _saveStatusCancellation?.Dispose();
            _saveStatusCancellation = new CancellationTokenSource();
            var statusToken = _saveStatusCancellation.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), statusToken);
                    if (!statusToken.IsCancellationRequested)
                    {
                        RunOnUI(() => SaveStatusText = "");
                    }
                }
                catch (OperationCanceledException) { }
            }, statusToken);
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

    public void Dispose()
    {
        _saveStatusCancellation?.Cancel();
        _saveStatusCancellation?.Dispose();
        _saveStatusCancellation = null;
    }

    private async Task<bool> ReadBoolSettingAsync(string key, bool fallback)
    {
        var value = await _repository!.GetSettingAsync(key);
        return bool.TryParse(value, out var result) ? result : fallback;
    }

    private async Task<string> ReadStringSettingAsync(string key, string fallback)
    {
        var value = await _repository!.GetSettingAsync(key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
