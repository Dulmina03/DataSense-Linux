using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

public class LiveTrafficSample
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public double DownloadRateBytesPerSec { get; set; }
    public double UploadRateBytesPerSec { get; set; }
    public double CombinedRateBytesPerSec => DownloadRateBytesPerSec + UploadRateBytesPerSec;
}

public class LiveProcessRankItem
{
    public string ProcessName { get; set; } = string.Empty;
    public int Pid { get; set; }
    public string ExecutablePath { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string DataSource { get; set; } = "Nethogs";
    public double DownloadRateBytesPerSec { get; set; }
    public double UploadRateBytesPerSec { get; set; }
    public double CombinedRateBytesPerSec => DownloadRateBytesPerSec + UploadRateBytesPerSec;
    public double PercentageOfTotalTraffic { get; set; }
    public string DownloadRateText { get; set; } = "—";
    public string UploadRateText { get; set; } = "—";
    public string CombinedRateText { get; set; } = "—";
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
