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

        // Database & Infrastructure
        services.AddSingleton<IPerformanceMonitor, PerformanceMonitor>();
        services.AddSingleton<INetworkUsageRepository, SqliteNetworkUsageRepository>();
        services.AddSingleton<IAnalyticsService, AnalyticsService>();
        services.AddSingleton<IIntelligenceService, IntelligenceService>();
        services.AddSingleton<IForecastService, ForecastService>();
        services.AddSingleton<IPatternAnalysisService, PatternAnalysisService>();
        services.AddSingleton<IApplicationIntelligenceService, ApplicationIntelligenceService>();
        services.AddSingleton<IUnifiedIntelligenceService, UnifiedIntelligenceService>();
        services.AddSingleton<IHistoricalAnalyticsService, HistoricalAnalyticsService>();

        // Services
        services.AddSingleton<INetworkMonitorService, LinuxNetworkMonitorService>();
        services.AddSingleton<INetworkMonitorWorker, NetworkMonitorWorker>();
        services.AddSingleton<INetworkPersistenceService, NetworkPersistenceService>();
        services.AddSingleton<INetworkConnectionService, LinuxNetworkConnectionService>();
        services.AddSingleton<NetworkSessionManager>();
        services.AddSingleton<ISpeedTestService, CloudflareSpeedTestService>();
        // Process-level monitoring services
        services.AddSingleton<IProcessNetworkMonitor, NethogsProcessNetworkMonitor>();
        services.AddSingleton<ProcessNetworkMonitorWorker>();

        // ViewModels
        // DashboardViewModel is Singleton: it subscribes to the monitor worker event
        // and holds live card state. Analytics load once on construction; the user
        // can force a reload via the Refresh button.
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<TrayIconViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<HistoricalExplorerViewModel>();
        services.AddTransient<SpeedTestViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<AboutViewModel>();
        services.AddTransient<ApplicationAnalyticsViewModel>();
        services.AddTransient<NetworkAnalyticsViewModel>();


        // Views
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }
}
