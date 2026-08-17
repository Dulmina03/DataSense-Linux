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

        // Database
        services.AddSingleton<INetworkUsageRepository, SqliteNetworkUsageRepository>();
        services.AddSingleton<IAnalyticsService, AnalyticsService>();

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
        services.AddTransient<SpeedTestViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<AboutViewModel>();
        services.AddTransient<ApplicationAnalyticsViewModel>();


        // Views
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }
}
