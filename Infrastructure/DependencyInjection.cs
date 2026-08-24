using System;
using Microsoft.Extensions.DependencyInjection;
using DataSense.Database;
using DataSense.Services;
using DataSense.ViewModels;
using DataSense.Views;

namespace DataSense.Infrastructure;

public static class DependencyInjection
{
    public static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Platform & Desktop Integration Services
        services.AddSingleton<ILinuxPlatformService, LinuxPlatformService>();
        services.AddSingleton<ILinuxStorageService, LinuxStorageService>();
        services.AddSingleton<ILinuxCapabilityService, LinuxCapabilityService>();
        services.AddSingleton<INativeNotificationService, NativeNotificationService>();
        services.AddSingleton<ILinuxStartupService, LinuxStartupService>();
        services.AddSingleton<IAppIconService, LinuxApplicationIconService>();
        services.AddSingleton<IApplicationChartColorProvider, ApplicationChartColorProvider>();

        // Database & Infrastructure
        services.AddSingleton<IPerformanceMonitor, PerformanceMonitor>();
        services.AddSingleton<ISystemHealthRegistry, SystemHealthRegistry>();
        services.AddSingleton<IPerformanceMonitorService, PerformanceMonitorService>();
        services.AddSingleton<IDiagnosticsService, DiagnosticsService>();
        services.AddSingleton<IExportService, ExportService>();
        services.AddSingleton<IEventService, EventService>();
        services.AddSingleton<IDatabaseMaintenanceService, DatabaseMaintenanceService>();
        services.AddSingleton<IImportRestoreService, ImportRestoreService>();
        services.AddSingleton<IBackupRecoveryService, BackupRecoveryService>();
        services.AddSingleton<INetworkUsageRepository, SqliteNetworkUsageRepository>();
        services.AddSingleton<IAnalyticsService, AnalyticsService>();
        services.AddSingleton<INetworkIntelligenceService, NetworkIntelligenceService>();
        services.AddSingleton<IForecastService, ForecastService>();
        services.AddSingleton<IPatternAnalysisService, PatternAnalysisService>();
        services.AddSingleton<IApplicationIntelligenceService, ApplicationIntelligenceService>();
        services.AddSingleton<IIntelligenceService, IntelligenceService>();
        services.AddSingleton<IUnifiedIntelligenceService, UnifiedIntelligenceService>();
        services.AddSingleton<IHistoricalAnalyticsService, HistoricalAnalyticsService>();
        services.AddSingleton<IApplicationAnalyticsService, ApplicationAnalyticsService>();
        services.AddSingleton<IProcessNetworkIntelligenceService, ProcessNetworkIntelligenceService>();
        services.AddSingleton<IApplicationNetworkCorrelationService, ApplicationNetworkCorrelationService>();
        services.AddSingleton<IApplicationSessionService, ApplicationSessionService>();
        services.AddSingleton<IUnifiedAnalyticsIntelligenceService, UnifiedAnalyticsIntelligenceService>();
        services.AddSingleton<IChartDataService, ChartDataService>();

        // Monitoring Services
        services.AddSingleton<INetworkMonitorService, LinuxNetworkMonitorService>();
        services.AddSingleton<INetworkMonitorWorker, NetworkMonitorWorker>();
        services.AddSingleton<INetworkPersistenceService, NetworkPersistenceService>();
        services.AddSingleton<INetworkConnectionService, LinuxNetworkConnectionService>();
        services.AddSingleton<INetworkIdentityService, NetworkIdentityService>();
        services.AddSingleton<NetworkSessionManager>();
        services.AddSingleton<ISpeedTestService, CloudflareSpeedTestService>();

        // Process-level monitoring services
        services.AddSingleton<ILinuxProcessResolver, LinuxProcessResolver>();
        services.AddSingleton<IProcessNetworkMonitor, CompositeProcessNetworkMonitor>();
        services.AddSingleton<ProcessNetworkMonitorWorker>();
        services.AddSingleton<ILinuxInterfaceStatsService, LinuxInterfaceStatsService>();
        services.AddSingleton<ILiveMonitoringEngine, LiveMonitoringEngine>();
        services.AddSingleton<ISessionIntelligenceService, SessionIntelligenceService>();

        // ViewModels
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<TrayIconViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<HistoricalExplorerViewModel>();
        services.AddTransient<SpeedTestViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<PrivacyViewModel>();
        services.AddTransient<DiagnosticsViewModel>();
        services.AddTransient<PerformanceViewModel>();
        services.AddTransient<ExportViewModel>();
        services.AddTransient<EventCenterViewModel>();
        services.AddTransient<ImportRestoreViewModel>();
        services.AddTransient<BackupRecoveryViewModel>();
        services.AddTransient<AboutViewModel>();
        services.AddTransient<ApplicationAnalyticsViewModel>();
        services.AddTransient<NetworkAnalyticsViewModel>();
        services.AddTransient<LiveMonitoringViewModel>();
        services.AddTransient<NetworkActivityTimelineViewModel>();
        services.AddTransient<UnifiedIntelligenceViewModel>();

        // Views
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }
}
