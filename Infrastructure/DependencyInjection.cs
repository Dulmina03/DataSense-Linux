using System;
using Microsoft.Extensions.DependencyInjection;
using DataSense.ViewModels;
using DataSense.Views;
using DataSense.Services;

namespace DataSense.Infrastructure;

public static class DependencyInjection
{
    public static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Services
        services.AddSingleton<INetworkMonitorService, LinuxNetworkMonitorService>();
        services.AddSingleton<INetworkMonitorWorker, NetworkMonitorWorker>();

        // ViewModels
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
