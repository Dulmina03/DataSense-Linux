using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using DataSense.Models;

namespace DataSense.Services;

public class LiveTrafficSample
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public double DownloadRateBytesPerSec { get; set; }
    public double UploadRateBytesPerSec { get; set; }
    public double CombinedRateBytesPerSec => DownloadRateBytesPerSec + UploadRateBytesPerSec;
}

public class LiveProcessRankItem : ObservableObject
{
    private string _processName = string.Empty;
    public string ProcessName { get => _processName; set => SetProperty(ref _processName, value); }

    private int _pid;
    public int Pid { get => _pid; set => SetProperty(ref _pid, value); }

    private string _executablePath = string.Empty;
    public string ExecutablePath { get => _executablePath; set => SetProperty(ref _executablePath, value); }

    private string _userName = string.Empty;
    public string UserName { get => _userName; set => SetProperty(ref _userName, value); }

    private string _dataSource = "Nethogs";
    public string DataSource { get => _dataSource; set => SetProperty(ref _dataSource, value); }

    private double _downloadRateBytesPerSec;
    public double DownloadRateBytesPerSec { get => _downloadRateBytesPerSec; set => SetProperty(ref _downloadRateBytesPerSec, value); }

    private double _uploadRateBytesPerSec;
    public double UploadRateBytesPerSec { get => _uploadRateBytesPerSec; set => SetProperty(ref _uploadRateBytesPerSec, value); }

    public double CombinedRateBytesPerSec => DownloadRateBytesPerSec + UploadRateBytesPerSec;

    private double _percentageOfTotalTraffic;
    public double PercentageOfTotalTraffic { get => _percentageOfTotalTraffic; set => SetProperty(ref _percentageOfTotalTraffic, value); }

    private string _downloadRateText = "—";
    public string DownloadRateText { get => _downloadRateText; set => SetProperty(ref _downloadRateText, value); }

    private string _uploadRateText = "—";
    public string UploadRateText { get => _uploadRateText; set => SetProperty(ref _uploadRateText, value); }

    private string _combinedRateText = "—";
    public string CombinedRateText { get => _combinedRateText; set => SetProperty(ref _combinedRateText, value); }

    public void UpdateFrom(LiveProcessRankItem other)
    {
        ProcessName = other.ProcessName;
        Pid = other.Pid;
        ExecutablePath = other.ExecutablePath;
        UserName = other.UserName;
        DataSource = other.DataSource;
        DownloadRateBytesPerSec = other.DownloadRateBytesPerSec;
        UploadRateBytesPerSec = other.UploadRateBytesPerSec;
        PercentageOfTotalTraffic = other.PercentageOfTotalTraffic;
        DownloadRateText = other.DownloadRateText;
        UploadRateText = other.UploadRateText;
        CombinedRateText = other.CombinedRateText;
        OnPropertyChanged(nameof(CombinedRateBytesPerSec));
    }
}

public class TrafficSpikeInfo
{
    public bool IsSpikeDetected { get; set; }
    public double CurrentRateBytesPerSec { get; set; }
    public double BaselineRateBytesPerSec { get; set; }
    public double DeviationPercentage { get; set; }
    public string CurrentRateText { get; set; } = "—";
    public string BaselineRateText { get; set; } = "—";
}

public enum ProcessSortMode
{
    HighestTotal,
    HighestDownload,
    HighestUpload
}

public enum ProcessRankCount
{
    Top5 = 5,
    Top10 = 10,
    AllActive = 100
}

public enum GraphWindowTime
{
    ThirtySeconds = 30,
    SixtySeconds = 60,
    FiveMinutes = 300
}

public class LiveMonitoringDiagnosticsInfo
{
    public int ActiveProcessCount { get; set; }
    public DateTime? LastLiveSampleTimestamp { get; set; }
    public string MonitorState { get; set; } = "Unknown";
    public string NethogsState { get; set; } = "Unknown";
    public int RestartCount { get; set; }
    public string CurrentStreamStatus { get; set; } = "Unknown";
    public string LastProcessingError { get; set; } = string.Empty;
}

public interface ILiveMonitoringEngine
{
    void AddSample(double downloadRateBytesPerSec, double uploadRateBytesPerSec);
    IReadOnlyList<LiveTrafficSample> GetRollingSamples(GraphWindowTime window);
    TrafficSpikeInfo CheckTrafficSpike();
    
    void UpdateLiveProcesses(IEnumerable<ProcessNetworkUsage> processes);
    IReadOnlyList<LiveProcessRankItem> GetRankedProcesses(ProcessSortMode sortMode, ProcessRankCount rankCount);
    
    IEnumerable<string> GenerateSmartInsights(IEnumerable<NetworkInterfaceStats>? interfaces = null);
    
    int SampleCount { get; }
    bool IsPaused { get; }
    void Pause();
    void Resume();
    void Clear();

    // Live Application Activity Methods
    IReadOnlyList<LiveApplicationTraffic> GetLiveApplications();
    LiveApplicationTraffic? GetLiveApplication(string processName, int pid, long startTimeTicks);
    IReadOnlyList<LiveTrafficSample> GetProcessSparkline(string processIdentity);
    LiveApplicationTraffic? GetTopConsumer();
    double TotalLiveDownloadSpeed { get; }
    double TotalLiveUploadSpeed { get; }
    int ActiveApplicationCount { get; }
    LiveMonitoringDiagnosticsInfo GetDiagnosticsInfo();
}
