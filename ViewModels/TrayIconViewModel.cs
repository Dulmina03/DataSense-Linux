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
    
    [ObservableProperty]
    private string _speedText = "↓ 0.0 B/s ↑ 0.0 B/s";

    [ObservableProperty]
    private WindowIcon? _trayIconImage;

    public TrayIconViewModel(INetworkMonitorWorker networkMonitorWorker)
    {
        _networkMonitorWorker = networkMonitorWorker;
        _networkMonitorWorker.NetworkUsageUpdated += OnNetworkUsageUpdated;
    }

    private void OnNetworkUsageUpdated(NetworkUsage usage)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            SpeedText = $"↓ {ByteFormatter.FormatSpeed(usage.DownloadSpeed)}  ↑ {ByteFormatter.FormatSpeed(usage.UploadSpeed)}";
            
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
    private void ShowApp()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow != null)
            {
                if (desktop.MainWindow.WindowState == Avalonia.Controls.WindowState.Minimized)
                {
                    desktop.MainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
                }
                desktop.MainWindow.Show();
                desktop.MainWindow.Activate();
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
    }
}
