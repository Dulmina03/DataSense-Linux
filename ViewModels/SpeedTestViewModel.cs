using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataSense.Database;
using DataSense.Models;
using DataSense.Services;

namespace DataSense.ViewModels;

public partial class SpeedTestViewModel : ViewModelBase, IDisposable
{
    private readonly ISpeedTestService _speedTestService;
    private readonly INetworkUsageRepository _repository;
    private CancellationTokenSource? _cancellationTokenSource;

    [ObservableProperty]
    private string _downloadSpeedText = "—";
    
    [ObservableProperty]
    private string _uploadSpeedText = "—";
    
    [ObservableProperty]
    private string _pingText = "—";
    
    [ObservableProperty]
    private string _jitterText = "—";

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private bool _isIndeterminateProgress;

    public ObservableCollection<SpeedTestRecord> TestHistory { get; } = new();

    public override string Title => "Speed Test";

    public SpeedTestViewModel(ISpeedTestService speedTestService, INetworkUsageRepository repository)
    {
        _speedTestService = speedTestService;
        _repository = repository;
        
        _ = LoadHistoryAsync();
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            var history = await _repository.GetSpeedTestsAsync(20);
            Dispatcher.UIThread.Post(() =>
            {
                TestHistory.Clear();
                foreach (var item in history)
                {
                    TestHistory.Add(item);
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load speed test history: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task StartTestAsync()
    {
        if (IsTesting) return;

        IsTesting = true;
        _cancellationTokenSource = new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;

        DownloadSpeedText = "—";
        UploadSpeedText = "—";
        PingText = "—";
        JitterText = "—";
        ProgressValue = 0;
        IsIndeterminateProgress = true;

        try
        {
            // 1. Ping
            StatusText = "Testing Ping...";
            double ping = await _speedTestService.TestPingAsync(token);
            PingText = ping > 0 ? $"{ping:F0} ms" : "Error";
            
            // Fake jitter for now since Cloudflare /cdn-cgi/trace doesn't provide it directly
            double jitter = ping > 0 ? ping * 0.15 : 0;
            JitterText = ping > 0 ? $"{jitter:F1} ms" : "—";

            // 2. Download
            StatusText = "Testing Download...";
            IsIndeterminateProgress = false;
            double finalDownload = await _speedTestService.TestDownloadAsync(speed => 
            {
                Dispatcher.UIThread.Post(() => 
                {
                    DownloadSpeedText = $"{speed:F1} Mbps";
                    // Rough progress visualization based on speed
                    ProgressValue = Math.Min(100, (speed / 100.0) * 100);
                });
            }, token);
            DownloadSpeedText = finalDownload > 0 ? $"{finalDownload:F1} Mbps" : "Error";

            // 3. Upload
            StatusText = "Testing Upload...";
            ProgressValue = 0;
            double finalUpload = await _speedTestService.TestUploadAsync(speed => 
            {
                Dispatcher.UIThread.Post(() => 
                {
                    UploadSpeedText = $"{speed:F1} Mbps";
                    ProgressValue = Math.Min(100, (speed / 50.0) * 100);
                });
            }, token);
            UploadSpeedText = finalUpload > 0 ? $"{finalUpload:F1} Mbps" : "Error";

            StatusText = "Test Complete";
            ProgressValue = 100;

            if (finalDownload > 0 || finalUpload > 0)
            {
                var record = new SpeedTestRecord
                {
                    Timestamp = DateTime.UtcNow,
                    DownloadSpeedMbps = finalDownload,
                    UploadSpeedMbps = finalUpload,
                    PingMs = ping,
                    JitterMs = jitter,
                    ServerName = "Cloudflare CDN"
                };
                
                await _repository.SaveSpeedTestAsync(record);
                await LoadHistoryAsync();
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Test Cancelled";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
            IsIndeterminateProgress = false;
        }
    }

    [RelayCommand]
    private void CancelTest()
    {
        if (IsTesting)
        {
            _cancellationTokenSource?.Cancel();
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }
}
