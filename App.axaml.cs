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
        // Start background process network monitoring worker
        var processWorker = Services.GetRequiredService<ProcessNetworkMonitorWorker>();
        processWorker.Start();

        // Start background network persistence service
        var persistenceService = Services.GetRequiredService<INetworkPersistenceService>();
        persistenceService.Start();

        // Start background session manager
        var sessionManager = Services.GetRequiredService<NetworkSessionManager>();
        sessionManager.Start();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.DataContext = Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = mainWindow;

            var trayViewModel = Services.GetRequiredService<TrayIconViewModel>();
            var trayIcon = new Avalonia.Controls.TrayIcon
            {
                Command = trayViewModel.ShowAppCommand,
                ToolTipText = trayViewModel.SpeedText,
                Menu = new Avalonia.Controls.NativeMenu
                {
                    Items =
                    {
                        new Avalonia.Controls.NativeMenuItem { Header = "Open DataSense", Command = trayViewModel.ShowAppCommand },
                        new Avalonia.Controls.NativeMenuItem { Header = "Exit", Command = trayViewModel.ExitAppCommand }
                    }
                }
            };
            
            trayViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(TrayIconViewModel.SpeedText))
                {
                    trayIcon.ToolTipText = trayViewModel.SpeedText;
                }
                else if (e.PropertyName == nameof(TrayIconViewModel.TrayIconImage))
                {
                    trayIcon.Icon = trayViewModel.TrayIconImage;
                }
            };

            var icons = new Avalonia.Controls.TrayIcons { trayIcon };
            Avalonia.Controls.TrayIcon.SetIcons(this, icons);

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
