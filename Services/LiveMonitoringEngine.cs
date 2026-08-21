using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DataSense.Helpers;
using DataSense.Models;

namespace DataSense.Services;

public class LiveMonitoringEngine : ILiveMonitoringEngine
{
    private readonly IEventService? _eventService;
    private readonly List<LiveTrafficSample> _samples = new();
    private readonly ConcurrentDictionary<string, LiveProcessRankItem> _currentProcesses = new();
    private readonly object _lock = new();
    private const int MaxSamples = 300;
    private bool _isPaused;

    public int SampleCount
    {
        get
        {
            lock (_lock) return _samples.Count;
        }
    }

    public bool IsPaused => _isPaused;

    public LiveMonitoringEngine(IEventService? eventService = null)
    {
        _eventService = eventService;
    }

    public void AddSample(double downloadRateBytesPerSec, double uploadRateBytesPerSec)
    {
        if (_isPaused) return;

        var sample = new LiveTrafficSample
        {
            Timestamp = DateTime.UtcNow,
            DownloadRateBytesPerSec = Math.Max(0, downloadRateBytesPerSec),
            UploadRateBytesPerSec = Math.Max(0, uploadRateBytesPerSec)
        };

        lock (_lock)
        {
            _samples.Add(sample);
            if (_samples.Count > MaxSamples)
            {
                _samples.RemoveAt(0); // Bounded memory eviction
            }
        }

        // Check for traffic spike
        var spike = CheckTrafficSpike();
        if (spike.IsSpikeDetected && _eventService != null)
        {
            _eventService.PublishEvent(new DataSenseEvent
            {
                EventType = DataSenseEventType.TrafficSpikeDetected,
                Severity = EventSeverity.Warning,
                Title = "Network Traffic Spike Detected",
                Description = $"Current traffic ({spike.CurrentRateText}) is {spike.DeviationPercentage:F0}% above rolling baseline ({spike.BaselineRateText}).",
                Source = "LiveMonitor",
                Fingerprint = "LiveTrafficSpike"
            });
        }
    }

    public IReadOnlyList<LiveTrafficSample> GetRollingSamples(GraphWindowTime window)
    {
        lock (_lock)
        {
            if (_samples.Count == 0) return Array.Empty<LiveTrafficSample>();
            DateTime cutoff = DateTime.UtcNow.AddSeconds(-(int)window);
            return _samples.Where(s => s.Timestamp >= cutoff).ToList();
        }
    }

    public TrafficSpikeInfo CheckTrafficSpike()
    {
        lock (_lock)
        {
            if (_samples.Count < 10)
            {
                return new TrafficSpikeInfo { IsSpikeDetected = false };
            }

            var recentSamples = _samples.Take(_samples.Count - 1).Select(s => s.CombinedRateBytesPerSec).ToList();
            if (recentSamples.Count == 0) return new TrafficSpikeInfo { IsSpikeDetected = false };

            double mean = recentSamples.Average();
            double sumSquares = recentSamples.Sum(d => (d - mean) * (d - mean));
            double stdDev = Math.Sqrt(sumSquares / Math.Max(1, recentSamples.Count - 1));

            var currentSample = _samples.Last();
            double currentRate = currentSample.CombinedRateBytesPerSec;

            // Deterministic spike rule: current > mean + 2 * stdDev AND current > 100 KB/s AND mean > 10 KB/s
            double threshold = mean + 2 * stdDev;
            bool isSpike = currentRate > threshold && currentRate > 102400 && mean > 10240;

            double devPct = mean > 0 ? ((currentRate - mean) / mean) * 100.0 : 0;

            return new TrafficSpikeInfo
            {
                IsSpikeDetected = isSpike,
                CurrentRateBytesPerSec = currentRate,
                BaselineRateBytesPerSec = mean,
                DeviationPercentage = Math.Max(0, devPct),
                CurrentRateText = ByteFormatter.FormatSpeed(currentRate),
                BaselineRateText = ByteFormatter.FormatSpeed(mean)
            };
        }
    }

    public void UpdateLiveProcesses(IEnumerable<ProcessNetworkUsage> processes)
    {
        if (_isPaused || processes == null) return;

        double totalSystemRate = processes.Sum(p => p.DownloadRateBytesPerSec + p.UploadRateBytesPerSec);
        if (totalSystemRate <= 0) totalSystemRate = 1.0;

        _currentProcesses.Clear();

        foreach (var proc in processes)
        {
            string key = proc.Pid > 0 ? $"{proc.ProcessIdentifier}_{proc.Pid}" : proc.ProcessIdentifier;
            double combined = proc.DownloadRateBytesPerSec + proc.UploadRateBytesPerSec;
            double pct = (combined / totalSystemRate) * 100.0;

            var item = new LiveProcessRankItem
            {
                ProcessName = proc.ProcessIdentifier,
                Pid = proc.Pid,
                ExecutablePath = proc.ExecutablePath,
                UserName = proc.User,
                DataSource = string.IsNullOrEmpty(proc.DataSource) ? "Nethogs" : proc.DataSource,
                DownloadRateBytesPerSec = proc.DownloadRateBytesPerSec,
                UploadRateBytesPerSec = proc.UploadRateBytesPerSec,
                PercentageOfTotalTraffic = Math.Clamp(pct, 0.0, 100.0),
                DownloadRateText = ByteFormatter.FormatSpeed(proc.DownloadRateBytesPerSec),
                UploadRateText = ByteFormatter.FormatSpeed(proc.UploadRateBytesPerSec),
                CombinedRateText = ByteFormatter.FormatSpeed(combined)
            };

            _currentProcesses[key] = item;
        }
    }

    public IReadOnlyList<LiveProcessRankItem> GetRankedProcesses(ProcessSortMode sortMode, ProcessRankCount rankCount)
    {
        var list = _currentProcesses.Values.ToList();

        var sorted = sortMode switch
        {
            ProcessSortMode.HighestDownload => list.OrderByDescending(p => p.DownloadRateBytesPerSec),
            ProcessSortMode.HighestUpload => list.OrderByDescending(p => p.UploadRateBytesPerSec),
            _ => list.OrderByDescending(p => p.CombinedRateBytesPerSec)
        };

        return sorted.Take((int)rankCount).ToList();
    }

    public IEnumerable<string> GenerateSmartInsights(IEnumerable<NetworkInterfaceStats>? interfaces = null)
    {
        var insights = new List<string>();

        // 1. Process Insights
        var topProcess = GetRankedProcesses(ProcessSortMode.HighestTotal, ProcessRankCount.Top5).FirstOrDefault();
        if (topProcess != null && topProcess.CombinedRateBytesPerSec > 1024)
        {
            insights.Add($"{topProcess.ProcessName} is currently responsible for {topProcess.PercentageOfTotalTraffic:F0}% of active network traffic ({topProcess.CombinedRateText}).");
        }

        // 2. Traffic Spike Insights
        var spike = CheckTrafficSpike();
        if (spike.IsSpikeDetected)
        {
            insights.Add($"Network traffic is currently {spike.DeviationPercentage:F0}% above the recent baseline ({spike.CurrentRateText} vs {spike.BaselineRateText}).");
        }
        else if (SampleCount >= 10)
        {
            insights.Add($"Network traffic is currently operating within normal baseline range.");
        }

        // 3. Interface Insights
        if (interfaces != null)
        {
            var activeIfaces = interfaces.Where(i => i.IsUp).ToList();
            if (activeIfaces.Count > 1)
            {
                var lowestDropIface = activeIfaces.OrderBy(i => i.PacketDropRatePercentage).First();
                insights.Add($"{lowestDropIface.InterfaceName} ({lowestDropIface.ConnectionType}) currently has the lowest packet drop rate ({lowestDropIface.PacketDropRatePercentage:F2}%).");
            }
        }

        if (insights.Count == 0)
        {
            insights.Add("Monitoring live network telemetry in real-time.");
        }

        return insights;
    }

    public void Pause()
    {
        _isPaused = true;
    }

    public void Resume()
    {
        _isPaused = false;
    }

    public void Clear()
    {
        lock (_lock)
        {
            _samples.Clear();
        }
        _currentProcesses.Clear();
    }
}
