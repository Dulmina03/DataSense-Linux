using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataSense.Helpers;
using DataSense.Models;
using DataSense.Services;

namespace DataSense.ViewModels;

public partial class TrayIconViewModel : ViewModelBase, IDisposable
{
    private readonly INetworkMonitorWorker _networkMonitorWorker;
    private readonly INetworkConnectionService _connectionService;
    private readonly IEventService _eventService;
    private readonly ILiveMonitoringEngine? _liveMonitoringEngine;

    [ObservableProperty] private string _speedText = "↓ 0.0 B/s ↑ 0.0 B/s";
    [ObservableProperty] private string _tooltipText = "DataSense — Network Monitoring";
    [ObservableProperty] private WindowIcon? _trayIconImage;
    [ObservableProperty] private bool _isMonitoring = true;

    public TrayIconViewModel(
        INetworkMonitorWorker networkMonitorWorker,
        INetworkConnectionService connectionService,
        IEventService eventService,
        ILiveMonitoringEngine? liveMonitoringEngine = null)
    {
        _networkMonitorWorker = networkMonitorWorker ?? throw new ArgumentNullException(nameof(networkMonitorWorker));
        _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
        _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
        _liveMonitoringEngine = liveMonitoringEngine;

        _networkMonitorWorker.NetworkUsageUpdated += OnNetworkUsageUpdated;
        _eventService.EventsUpdated += OnEventsUpdated;
    }

    private void OnEventsUpdated(object? sender, EventArgs e)
    {
        UpdateTooltip();
    }

    private async void OnNetworkUsageUpdated(NetworkUsage usage)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!IsMonitoring)
            {
                SpeedText = "⏸ Monitoring Paused";
                TooltipText = "DataSense\nMonitoring Paused";
                return;
            }

            SpeedText = $"↓ {ByteFormatter.FormatSpeed(usage.DownloadSpeed)}  ↑ {ByteFormatter.FormatSpeed(usage.UploadSpeed)}";
            UpdateTooltip();

            try
            {
                TrayIconImage = GenerateIcon(usage.DownloadSpeed, usage.UploadSpeed);
            }
            catch
            {
                // Ignore icon generation errors (e.g. headless)
            }
        });
    }

    private async void UpdateTooltip()
    {
        var activeIface = _networkMonitorWorker.ActiveInterface;
        var unread = _eventService.UnreadCount;
        string unreadStr = unread > 0 ? $"\nAlerts: {unread} unread" : "";

        string topProcStr = "";
        if (_liveMonitoringEngine != null)
        {
            var top = _liveMonitoringEngine.GetRankedProcesses(ProcessSortMode.HighestTotal, ProcessRankCount.Top5).FirstOrDefault();
            if (top != null && top.CombinedRateBytesPerSec > 1024)
            {
                topProcStr = $"\nTop: {top.ProcessName} ({top.CombinedRateText})";
            }
        }

        TooltipText = $"DataSense\nAdapter: {activeIface}\n{SpeedText}{topProcStr}{unreadStr}";
    }

    private WindowIcon GenerateIcon(double dlSpeed, double ulSpeed)
    {
        var rtb = new RenderTargetBitmap(new PixelSize(160, 24), new Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext())
        {
            var text = $"↓ {ByteFormatter.FormatSpeed(dlSpeed)}   ↑ {ByteFormatter.FormatSpeed(ulSpeed)}";
            var formattedText = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Inter, Arial, sans-serif", FontStyle.Normal, FontWeight.Bold),
                12,
                Brushes.White
            );
            
            ctx.DrawText(formattedText, new Point(0, 4));
        }
        
        using var ms = new System.IO.MemoryStream();
        rtb.Save(ms);
        ms.Position = 0;
        return new WindowIcon(ms);
    }

    [RelayCommand]
    private void ToggleMonitoring()
    {
        IsMonitoring = !IsMonitoring;
        if (IsMonitoring)
        {
            _networkMonitorWorker.Start();
        }
        else
        {
            _networkMonitorWorker.Stop();
        }
        UpdateTooltip();
    }

    [RelayCommand]
    private void ShowApp()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow != null)
            {
                if (desktop.MainWindow.WindowState == WindowState.Minimized)
                {
                    desktop.MainWindow.WindowState = WindowState.Normal;
                }
                desktop.MainWindow.Show();
                desktop.MainWindow.Activate();
            }
        }
    }

    [RelayCommand]
    private void NavigateTo(string viewName)
    {
        ShowApp();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow?.DataContext is MainWindowViewModel mainVm)
        {
            switch (viewName)
            {
                case "Dashboard": mainVm.NavigateToDashboardCommand.Execute(null); break;
                case "EventCenter": mainVm.NavigateToEventCenterCommand.Execute(null); break;
                case "Diagnostics": mainVm.NavigateToDiagnosticsCommand.Execute(null); break;
                case "Performance": mainVm.NavigateToPerformanceCommand.Execute(null); break;
            }
        }
    }

    [RelayCommand]
    private void ExitApp()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    public void Dispose()
    {
        _networkMonitorWorker.NetworkUsageUpdated -= OnNetworkUsageUpdated;
        _eventService.EventsUpdated -= OnEventsUpdated;
    }
}
