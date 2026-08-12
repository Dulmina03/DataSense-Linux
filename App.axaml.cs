using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using DataSense.Database;
using DataSense.Infrastructure;
using DataSense.Services;
using DataSense.ViewModels;
using DataSense.Views;

namespace DataSense;

public partial class App : Application
{
    public static IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Services = DependencyInjection.ConfigureServices();

        // Initialize database repository asynchronously
        var repository = Services.GetRequiredService<INetworkUsageRepository>();
        Task.Run(async () =>
        {
            try
            {
                await repository.InitializeAsync();
                await repository.PurgeOldRecordsAsync(TimeSpan.FromDays(30));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Database initialization error: {ex}");
            }
        });

        // Start background network monitoring worker
        var worker = Services.GetRequiredService<INetworkMonitorWorker>();
        worker.Start();

        // Start background network persistence service
        var persistenceService = Services.GetRequiredService<INetworkPersistenceService>();
        persistenceService.Start();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.DataContext = Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = mainWindow;

            desktop.Exit += (sender, e) =>
            {
                if (Services is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
