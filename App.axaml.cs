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

        // 1. Crash-safe Linux Directory Initialization & Storage Setup
        var storageService = Services.GetService<ILinuxStorageService>();
        if (storageService != null)
        {
            Task.Run(async () =>
            {
                try
                {
                    await storageService.EnsureDirectoriesCreatedAsync();
                    await storageService.LogAsync("DataSense application startup initialized.", "INFO");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Storage initialization warning: {ex.Message}");
                }
            });
        }

        // 2. Crash-safe Repository Initialization
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

        // 3. Crash-safe Background Service Launches
        StartBackgroundWorkers();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.DataContext = Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = mainWindow;

            var trayViewModel = Services.GetRequiredService<TrayIconViewModel>();
            
            var speedMenuItem = new Avalonia.Controls.NativeMenuItem { Header = $"Speed: {trayViewModel.SpeedText}", IsEnabled = false };
            var topProcessMenuItem = new Avalonia.Controls.NativeMenuItem { Header = trayViewModel.TopProcessText, IsEnabled = false };

            var trayIcon = new Avalonia.Controls.TrayIcon
            {
                Command = trayViewModel.ShowAppCommand,
                ToolTipText = trayViewModel.TooltipText,
                Menu = new Avalonia.Controls.NativeMenu
                {
                    Items =
                    {
                        new Avalonia.Controls.NativeMenuItem { Header = "⚡ Open DataSense", Command = trayViewModel.ShowAppCommand },
                        new Avalonia.Controls.NativeMenuItemSeparator(),
                        speedMenuItem,
                        topProcessMenuItem,
                        new Avalonia.Controls.NativeMenuItemSeparator(),
                        new Avalonia.Controls.NativeMenuItem { Header = "📊 Dashboard", Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => trayViewModel.NavigateToCommand.Execute("Dashboard")) },
                        new Avalonia.Controls.NativeMenuItem { Header = "📈 Network Analytics", Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => trayViewModel.NavigateToCommand.Execute("Performance")) },
                        new Avalonia.Controls.NativeMenuItem { Header = "🔔 Event Center", Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => trayViewModel.NavigateToCommand.Execute("EventCenter")) },
                        new Avalonia.Controls.NativeMenuItem { Header = "🛠️ Diagnostics", Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => trayViewModel.NavigateToCommand.Execute("Diagnostics")) },
                        new Avalonia.Controls.NativeMenuItemSeparator(),
                        new Avalonia.Controls.NativeMenuItem { Header = "⏯ Pause / Resume Monitoring", Command = trayViewModel.ToggleMonitoringCommand },
                        new Avalonia.Controls.NativeMenuItemSeparator(),
                        new Avalonia.Controls.NativeMenuItem { Header = "⚙️ Settings", Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => trayViewModel.NavigateToCommand.Execute("Settings")) },
                        new Avalonia.Controls.NativeMenuItemSeparator(),
                        new Avalonia.Controls.NativeMenuItem { Header = "❌ Quit DataSense", Command = trayViewModel.ExitAppCommand }
                    }
                }
            };
            
            trayViewModel.PropertyChanged += (s, e) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (e.PropertyName == nameof(TrayIconViewModel.TooltipText))
                    {
                        trayIcon.ToolTipText = trayViewModel.TooltipText;
                    }
                    else if (e.PropertyName == nameof(TrayIconViewModel.TrayIconImage))
                    {
                        trayIcon.Icon = trayViewModel.TrayIconImage;
                    }
                    else if (e.PropertyName == nameof(TrayIconViewModel.SpeedText))
                    {
                        speedMenuItem.Header = $"Speed: {trayViewModel.SpeedText}";
                    }
                    else if (e.PropertyName == nameof(TrayIconViewModel.TopProcessText))
                    {
                        topProcessMenuItem.Header = trayViewModel.TopProcessText;
                    }
                });
            };

            var icons = new Avalonia.Controls.TrayIcons { trayIcon };
            Avalonia.Controls.TrayIcon.SetIcons(this, icons);

            desktop.Exit += (sender, e) =>
            {
                StopBackgroundWorkers();
                if (Services is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void StartBackgroundWorkers()
    {
        if (Services == null) return;

        try
        {
            var worker = Services.GetService<INetworkMonitorWorker>();
            worker?.Start();
        }
        catch { }

        try
        {
            var processWorker = Services.GetService<ProcessNetworkMonitorWorker>();
            processWorker?.Start();
        }
        catch { }

        try
        {
            var persistenceService = Services.GetService<INetworkPersistenceService>();
            persistenceService?.Start();
        }
        catch { }

        try
        {
            var sessionManager = Services.GetService<NetworkSessionManager>();
            sessionManager?.Start();
        }
        catch { }
    }

    private static void StopBackgroundWorkers()
    {
        if (Services == null) return;

        try { Services.GetService<INetworkMonitorWorker>()?.Stop(); } catch { }
        try { Services.GetService<ProcessNetworkMonitorWorker>()?.Stop(); } catch { }
        try { Services.GetService<INetworkPersistenceService>()?.Stop(); } catch { }
        try { Services.GetService<NetworkSessionManager>()?.Stop(); } catch { }
    }
}
