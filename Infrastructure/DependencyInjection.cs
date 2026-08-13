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

        // Services
        services.AddSingleton<INetworkMonitorService, LinuxNetworkMonitorService>();
        services.AddSingleton<INetworkMonitorWorker, NetworkMonitorWorker>();
        services.AddSingleton<INetworkPersistenceService, NetworkPersistenceService>();

        // ViewModels
        // DashboardViewModel is Singleton: it subscribes to the monitor worker event
        // and holds live card state. Analytics load once on construction; the user
        // can force a reload via the Refresh button.
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<AboutViewModel>();


        // Views
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }
}
