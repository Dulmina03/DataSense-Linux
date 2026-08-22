using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Database;
using DataSense.Helpers;
using DataSense.Models;

namespace DataSense.Services;

public class LiveMonitoringEngine : ILiveMonitoringEngine
{
    private readonly ProcessNetworkMonitorWorker? _processWorker;
    private readonly INetworkConnectionService? _connectionService;
    private readonly IPatternAnalysisService? _patternService;
    private readonly IForecastService? _forecastService;
    private readonly IEventService? _eventService;
    private readonly INetworkUsageRepository? _repository;

    private readonly List<LiveTrafficSample> _samples = new();
    private readonly ConcurrentDictionary<string, LiveProcessRankItem> _currentProcesses = new();
    private readonly ConcurrentDictionary<string, LiveApplicationTrafficState> _liveAppStates = new();
    private readonly object _lock = new();
    private const int MaxSamples = 300;
    private bool _isPaused;

    // Network context cache
    private string _currentNetworkName = "Unknown network";
    private string _currentConnectionType = "Unknown";
    private string _currentInterfaceName = "Unknown";
    private bool _isCheckingNetwork;
    private DateTime _lastNetworkCheck = DateTime.MinValue;

    // Budget cache
    private DataBudget? _cachedBudget;
    private DateTime _lastBudgetCheck = DateTime.MinValue;

    // Top consumer tracking
    private string? _currentTopConsumerKey;
    private int _topConsumerConsecutiveSamples;

    private class LiveApplicationTrafficState
    {
        public string ProcessName { get; set; } = string.Empty;
        public int Pid { get; set; }
        public DateTime StartTime { get; set; }
        public long StartTimeTicks { get; set; }
        public string ProcessIdentity { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string DataSource { get; set; } = "Nethogs";
        public string NetworkName { get; set; } = "Unknown network";
        public string ConnectionType { get; set; } = "Unknown";
        public string InterfaceName { get; set; } = "Unknown";

        // Rate calculations
        public double DownloadRateBytesPerSec { get; set; }
        public double UploadRateBytesPerSec { get; set; }
        public long LastCumulativeDownload { get; set; }
        public long LastCumulativeUpload { get; set; }
        public bool HasLastCumulative { get; set; }
        public DateTime LastObservedAt { get; set; }

        // Activity state
        public bool IsActive { get; set; }
        public DateTime LastActiveAt { get; set; }
        public string ActivityState { get; set; } = "Idle"; // Active, Idle, Recently Active, Unavailable

        // Live session tracking
        public DateTime? SessionStart { get; set; }
        public long AccumulatedBytes { get; set; }
        public double PeakDownloadRate { get; set; }
        public double PeakUploadRate { get; set; }

        // Sparkline history (max 60 samples)
        public List<LiveTrafficSample> Sparkline { get; } = new();

        // Cache of historical average rate (baseline)
        public double? HistoricalBaselineRate { get; set; }
        public bool CheckedHistoricalBaseline { get; set; }
        
        // Track transition state for started/stopped events
        public bool WasActiveLastSample { get; set; }
    }

    public int SampleCount
    {
        get
        {
            lock (_lock) return _samples.Count;
        }
    }

    public bool IsPaused => _isPaused;

    public LiveMonitoringEngine(
        ProcessNetworkMonitorWorker? processWorker = null,
        INetworkConnectionService? connectionService = null,
        IPatternAnalysisService? patternService = null,
        IForecastService? forecastService = null,
        IEventService? eventService = null,
        INetworkUsageRepository? repository = null)
    {
        _processWorker = processWorker;
        _connectionService = connectionService;
        _patternService = patternService;
        _forecastService = forecastService;
        _eventService = eventService;
        _repository = repository;

        if (_processWorker != null)
        {
            _processWorker.LiveTrafficUpdated += OnLiveTrafficUpdatedFromWorker;
        }
    }

    private void OnLiveTrafficUpdatedFromWorker(IEnumerable<ProcessNetworkUsage> currentBatch)
    {
        UpdateLiveProcesses(currentBatch);
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
                Fingerprint = "LiveTrafficSpike",
                Timestamp = DateTime.UtcNow
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

        UpdateNetworkContext();
        UpdateBudgetContext();

        var currentTimestamp = DateTime.UtcNow;
        var seenKeys = new HashSet<string>();

        foreach (var proc in processes)
        {
            if (proc.Pid < 0 || proc.DownloadRateBytesPerSec < 0 || proc.UploadRateBytesPerSec < 0 ||
                proc.DownloadBytes < 0 || proc.UploadBytes < 0 ||
                string.IsNullOrEmpty(proc.ProcessIdentifier) || proc.Timestamp == default || string.IsNullOrEmpty(proc.DataSource))
            {
                continue; // Discard invalid sample
            }

            string key = !string.IsNullOrEmpty(proc.ProcessIdentityKey)
                ? proc.ProcessIdentityKey
                : $"{proc.ProcessIdentifier}_{proc.Pid}_0";

            seenKeys.Add(key);

            var state = _liveAppStates.GetOrAdd(key, k => new LiveApplicationTrafficState
            {
                ProcessName = proc.ProcessIdentifier,
                Pid = proc.Pid,
                ExecutablePath = proc.ExecutablePath,
                UserName = proc.User,
                DataSource = string.IsNullOrEmpty(proc.DataSource) ? "Nethogs" : proc.DataSource,
                LastObservedAt = currentTimestamp,
                LastActiveAt = currentTimestamp,
                ProcessIdentity = k
            });

            // Extract start time
            var parts = key.Split('_');
            if (parts.Length >= 3 && long.TryParse(parts[2], out long ticks))
            {
                state.StartTimeTicks = ticks;
                state.StartTime = new DateTime(ticks, DateTimeKind.Utc);
            }
            else
            {
                state.StartTime = proc.Timestamp;
            }

            // Sync metadata
            if (!string.IsNullOrEmpty(proc.ExecutablePath)) state.ExecutablePath = proc.ExecutablePath;
            if (!string.IsNullOrEmpty(proc.User) && proc.User != "unknown") state.UserName = proc.User;
            state.NetworkName = _currentNetworkName;
            state.ConnectionType = _currentConnectionType;
            state.InterfaceName = _currentInterfaceName;

            double elapsedSeconds = (proc.Timestamp - state.LastObservedAt).TotalSeconds;

            // Rate Calculations
            if (proc.DownloadBytes > 0 || proc.UploadBytes > 0)
            {
                if (!state.HasLastCumulative)
                {
                    state.LastCumulativeDownload = proc.DownloadBytes;
                    state.LastCumulativeUpload = proc.UploadBytes;
                    state.HasLastCumulative = true;
                    state.DownloadRateBytesPerSec = proc.DownloadRateBytesPerSec;
                    state.UploadRateBytesPerSec = proc.UploadRateBytesPerSec;
                }
                else
                {
                    if (elapsedSeconds <= 0)
                    {
                        // Safe handling of division by zero
                    }
                    else if (proc.DownloadBytes < state.LastCumulativeDownload || proc.UploadBytes < state.LastCumulativeUpload)
                    {
                        // Reset counter: clamp to zero
                        state.LastCumulativeDownload = proc.DownloadBytes;
                        state.LastCumulativeUpload = proc.UploadBytes;
                        state.DownloadRateBytesPerSec = 0;
                        state.UploadRateBytesPerSec = 0;
                    }
                    else
                    {
                        state.DownloadRateBytesPerSec = (proc.DownloadBytes - state.LastCumulativeDownload) / elapsedSeconds;
                        state.UploadRateBytesPerSec = (proc.UploadBytes - state.LastCumulativeUpload) / elapsedSeconds;
                        state.LastCumulativeDownload = proc.DownloadBytes;
                        state.LastCumulativeUpload = proc.UploadBytes;
                    }
                }
            }
            else
            {
                state.DownloadRateBytesPerSec = proc.DownloadRateBytesPerSec;
                state.UploadRateBytesPerSec = proc.UploadRateBytesPerSec;
            }

            // Clamps
            state.DownloadRateBytesPerSec = Math.Max(0, state.DownloadRateBytesPerSec);
            state.UploadRateBytesPerSec = Math.Max(0, state.UploadRateBytesPerSec);
            state.LastObservedAt = proc.Timestamp;

            // Active Application Detection
            const double ActiveThreshold = 1024.0; // 1 KB/s
            bool isCurrentlyActive = (state.DownloadRateBytesPerSec > ActiveThreshold) || (state.UploadRateBytesPerSec > ActiveThreshold);

            if (isCurrentlyActive)
            {
                state.IsActive = true;
                state.LastActiveAt = proc.Timestamp;
                state.ActivityState = "Active";

                if (!state.WasActiveLastSample)
                {
                    state.SessionStart = proc.Timestamp;
                    state.AccumulatedBytes = 0;
                    state.PeakDownloadRate = state.DownloadRateBytesPerSec;
                    state.PeakUploadRate = state.UploadRateBytesPerSec;

                    PublishLiveEvent(DataSenseEventType.ApplicationAnomaly,
                        "Application Traffic Started",
                        $"{state.ProcessName} (PID {state.Pid}) started transferring data.",
                        EventSeverity.Info,
                        $"Started_{state.ProcessName}_{state.Pid}");
                }
                else
                {
                    double interval = elapsedSeconds > 0 ? elapsedSeconds : 1.0;
                    state.AccumulatedBytes += (long)((state.DownloadRateBytesPerSec + state.UploadRateBytesPerSec) * interval);
                    state.PeakDownloadRate = Math.Max(state.PeakDownloadRate, state.DownloadRateBytesPerSec);
                    state.PeakUploadRate = Math.Max(state.PeakUploadRate, state.UploadRateBytesPerSec);
                }
            }
            else
            {
                if (state.WasActiveLastSample)
                {
                    PublishLiveEvent(DataSenseEventType.ApplicationAnomaly,
                        "Application Traffic Stopped",
                        $"{state.ProcessName} (PID {state.Pid}) stopped transferring data.",
                        EventSeverity.Info,
                        $"Stopped_{state.ProcessName}_{state.Pid}");
                    
                    state.SessionStart = null;
                }

                state.IsActive = false;
                if ((proc.Timestamp - state.LastActiveAt).TotalSeconds <= 30)
                {
                    state.ActivityState = "Recently Active";
                }
                else
                {
                    state.ActivityState = "Idle";
                }
            }

            state.WasActiveLastSample = isCurrentlyActive;

            // Sparkline bounded history (last 60 samples)
            lock (state.Sparkline)
            {
                state.Sparkline.Add(new LiveTrafficSample
                {
                    Timestamp = proc.Timestamp,
                    DownloadRateBytesPerSec = state.DownloadRateBytesPerSec,
                    UploadRateBytesPerSec = state.UploadRateBytesPerSec
                });
                if (state.Sparkline.Count > 60)
                {
                    state.Sparkline.RemoveAt(0);
                }
            }

            CheckApplicationAnomaly(state);
        }

        // Handle process disappearance
        foreach (var kvp in _liveAppStates)
        {
            if (!seenKeys.Contains(kvp.Key))
            {
                var state = kvp.Value;
                if (state.WasActiveLastSample)
                {
                    PublishLiveEvent(DataSenseEventType.ApplicationAnomaly,
                        "Application Traffic Stopped",
                        $"{state.ProcessName} (PID {state.Pid}) stopped transferring data.",
                        EventSeverity.Info,
                        $"Stopped_{state.ProcessName}_{state.Pid}");
                    state.SessionStart = null;
                    state.WasActiveLastSample = false;
                }
                state.IsActive = false;
                state.ActivityState = "Idle";

                // Cleanup stale processes (2 mins inactive)
                if ((currentTimestamp - state.LastObservedAt).TotalSeconds > 120)
                {
                    _liveAppStates.TryRemove(kvp.Key, out _);
                }
            }
        }

        // Top Consumer and Debounce
        var activeApps = _liveAppStates.Values.Where(a => a.IsActive).ToList();
        var top = activeApps.OrderByDescending(a => a.DownloadRateBytesPerSec + a.UploadRateBytesPerSec).FirstOrDefault();

        if (top != null && (top.DownloadRateBytesPerSec + top.UploadRateBytesPerSec) > 1024.0)
        {
            if (_currentTopConsumerKey != top.ProcessIdentity)
            {
                _currentTopConsumerKey = top.ProcessIdentity;
                _topConsumerConsecutiveSamples = 1;
            }
            else
            {
                _topConsumerConsecutiveSamples++;
                if (_topConsumerConsecutiveSamples == 2)
                {
                    PublishLiveEvent(DataSenseEventType.ProcessTrafficSpike,
                        "New Top Data Consumer",
                        $"{top.ProcessName} is now the top data consumer at {ByteFormatter.FormatSpeed(top.DownloadRateBytesPerSec + top.UploadRateBytesPerSec)}.",
                        EventSeverity.Info,
                        $"TopConsumer_{top.ProcessName}_{top.Pid}");
                }
            }
        }
        else
        {
            _currentTopConsumerKey = null;
            _topConsumerConsecutiveSamples = 0;
        }

        // Sync legacy RankedProcesses compatibility
        double totalSystemRate = processes.Sum(p => p.DownloadRateBytesPerSec + p.UploadRateBytesPerSec);
        if (totalSystemRate <= 0) totalSystemRate = 1.0;

        _currentProcesses.Clear();
        foreach (var kvp in _liveAppStates)
        {
            var state = kvp.Value;
            double combined = state.DownloadRateBytesPerSec + state.UploadRateBytesPerSec;
            double pct = (combined / totalSystemRate) * 100.0;

            _currentProcesses[kvp.Key] = new LiveProcessRankItem
            {
                ProcessName = state.ProcessName,
                Pid = state.Pid,
                ExecutablePath = state.ExecutablePath,
                UserName = state.UserName,
                DataSource = state.DataSource,
                DownloadRateBytesPerSec = state.DownloadRateBytesPerSec,
                UploadRateBytesPerSec = state.UploadRateBytesPerSec,
                PercentageOfTotalTraffic = Math.Clamp(pct, 0.0, 100.0),
                DownloadRateText = ByteFormatter.FormatSpeed(state.DownloadRateBytesPerSec),
                UploadRateText = ByteFormatter.FormatSpeed(state.UploadRateBytesPerSec),
                CombinedRateText = ByteFormatter.FormatSpeed(combined)
            };
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
        var topProcess = GetTopConsumer();
        if (topProcess != null && topProcess.TotalBytesPerSecond > 1024)
        {
            double totalLive = TotalLiveDownloadSpeed + TotalLiveUploadSpeed;
            double pct = totalLive > 0 ? (topProcess.TotalBytesPerSecond / totalLive) * 100.0 : 0;
            insights.Add($"{topProcess.ProcessName} is currently responsible for {pct:F0}% of active network traffic ({ByteFormatter.FormatSpeed(topProcess.TotalBytesPerSecond)}).");
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

        // 4. Budget-aware Live Insights
        if (_cachedBudget != null && _cachedBudget.Enabled && topProcess != null && topProcess.TotalBytesPerSecond > 1024 * 1024)
        {
            insights.Add($"{topProcess.ProcessName} is currently consuming {ByteFormatter.FormatSpeed(topProcess.TotalBytesPerSecond)} and is contributing significantly to today's data usage.");
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
        // Avoid artificial spikes and false event transitions on resume
        foreach (var state in _liveAppStates.Values)
        {
            state.HasLastCumulative = false;
            state.WasActiveLastSample = false;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _samples.Clear();
        }
        _currentProcesses.Clear();
        _liveAppStates.Clear();
    }

    // New ILiveMonitoringEngine API implementations
    public IReadOnlyList<LiveApplicationTraffic> GetLiveApplications()
    {
        return _liveAppStates.Values.Select(MapToTraffic).ToList();
    }

    public LiveApplicationTraffic? GetLiveApplication(string processName, int pid, long startTimeTicks)
    {
        string key = $"{processName}_{pid}_{startTimeTicks}";
        if (_liveAppStates.TryGetValue(key, out var state))
        {
            return MapToTraffic(state);
        }
        // Fallback search
        var match = _liveAppStates.Values.FirstOrDefault(s => s.ProcessName == processName && s.Pid == pid);
        return match != null ? MapToTraffic(match) : null;
    }

    public IReadOnlyList<LiveTrafficSample> GetProcessSparkline(string processIdentity)
    {
        if (_liveAppStates.TryGetValue(processIdentity, out var state))
        {
            lock (state.Sparkline)
            {
                return state.Sparkline.ToList();
            }
        }
        return Array.Empty<LiveTrafficSample>();
    }

    public LiveApplicationTraffic? GetTopConsumer()
    {
        var top = _liveAppStates.Values
            .Where(a => a.IsActive)
            .OrderByDescending(a => a.DownloadRateBytesPerSec + a.UploadRateBytesPerSec)
            .FirstOrDefault();
        return top != null ? MapToTraffic(top) : null;
    }

    public double TotalLiveDownloadSpeed => _liveAppStates.Values.Where(a => a.IsActive).Sum(a => a.DownloadRateBytesPerSec);
    public double TotalLiveUploadSpeed => _liveAppStates.Values.Where(a => a.IsActive).Sum(a => a.UploadRateBytesPerSec);
    public int ActiveApplicationCount => _liveAppStates.Values.Count(a => a.IsActive);

    public LiveMonitoringDiagnosticsInfo GetDiagnosticsInfo()
    {
        return new LiveMonitoringDiagnosticsInfo
        {
            ActiveProcessCount = ActiveApplicationCount,
            LastLiveSampleTimestamp = _processWorker?.LastSuccessfulSample,
            MonitorState = _processWorker?.MonitoringStatus ?? "Unknown",
            NethogsState = _processWorker?.MonitoringStatus ?? "Unknown",
            RestartCount = _processWorker?.RestartAttempts ?? 0,
            CurrentStreamStatus = _processWorker?.IsRunning == true ? "Streaming" : "Stopped",
            LastProcessingError = _processWorker?.LastError ?? string.Empty
        };
    }

    private static LiveApplicationTraffic MapToTraffic(LiveApplicationTrafficState state)
    {
        return new LiveApplicationTraffic
        {
            ProcessName = state.ProcessName,
            Pid = state.Pid,
            StartTime = state.StartTime,
            ProcessIdentity = state.ProcessIdentity,
            ExecutablePath = state.ExecutablePath,
            UserName = state.UserName,
            DataSource = state.DataSource,
            NetworkName = state.NetworkName,
            ConnectionType = state.ConnectionType,
            InterfaceName = state.InterfaceName,
            DownloadBytesPerSecond = state.DownloadRateBytesPerSec,
            UploadBytesPerSecond = state.UploadRateBytesPerSec,
            LastObservedAt = state.LastObservedAt,
            IsActive = state.IsActive,
            ActivityState = state.ActivityState
        };
    }

    private void PublishLiveEvent(DataSenseEventType type, string title, string description, EventSeverity severity, string fingerprint)
    {
        if (_eventService == null) return;
        _eventService.PublishEvent(new DataSenseEvent
        {
            EventType = type,
            Title = title,
            Description = description,
            Severity = severity,
            Source = "LiveMonitor",
            Fingerprint = fingerprint,
            Timestamp = DateTime.UtcNow
        });
    }

    private void UpdateNetworkContext()
    {
        if (_connectionService == null || _isCheckingNetwork) return;
        var now = DateTime.UtcNow;
        if ((now - _lastNetworkCheck).TotalSeconds < 10) return;

        _isCheckingNetwork = true;
        _lastNetworkCheck = now;

        Task.Run(async () =>
        {
            try
            {
                var conn = await _connectionService.GetConnectionDetailsAsync("");
                if (conn != null)
                {
                    _currentNetworkName = !string.IsNullOrEmpty(conn.WifiSsid) && conn.WifiSsid != "—" ? conn.WifiSsid : conn.ConnectionName;
                    _currentConnectionType = conn.ConnectionType;
                    _currentInterfaceName = conn.InterfaceName;
                }
                else
                {
                    _currentNetworkName = "Unknown network";
                    _currentConnectionType = "Unknown";
                    _currentInterfaceName = "Unknown";
                }
            }
            catch
            {
                _currentNetworkName = "Unknown network";
                _currentConnectionType = "Unknown";
                _currentInterfaceName = "Unknown";
            }
            finally
            {
                _isCheckingNetwork = false;
            }
        });
    }

    private void UpdateBudgetContext()
    {
        if (_forecastService == null) return;
        var now = DateTime.UtcNow;
        if ((now - _lastBudgetCheck).TotalSeconds < 10) return;

        _lastBudgetCheck = now;
        Task.Run(async () =>
        {
            try
            {
                _cachedBudget = await _forecastService.GetBudgetAsync();
            }
            catch
            {
                _cachedBudget = null;
            }
        });
    }

    private void CheckApplicationAnomaly(LiveApplicationTrafficState state)
    {
        if (_repository == null) return;

        if (!state.CheckedHistoricalBaseline)
        {
            state.CheckedHistoricalBaseline = true;
            Task.Run(async () =>
            {
                try
                {
                    string connectionString = (_repository as SqliteNetworkUsageRepository)?.ConnectionString ?? "Data Source=datasense.db";
                    using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
                    await connection.OpenAsync();

                    const string sqlCount = "SELECT COUNT(*) FROM ProcessUsageRecords WHERE ProcessName = @Name AND (BytesDownloaded > 0 OR BytesUploaded > 0)";
                    using (var cmdCount = connection.CreateCommand())
                    {
                        cmdCount.CommandText = sqlCount;
                        cmdCount.Parameters.AddWithValue("@Name", state.ProcessName);
                        long count = (long)(await cmdCount.ExecuteScalarAsync() ?? 0L);

                        if (count >= 5)
                        {
                            const string sqlAvg = "SELECT AVG(BytesDownloaded + BytesUploaded) FROM ProcessUsageRecords WHERE ProcessName = @Name AND (BytesDownloaded > 0 OR BytesUploaded > 0)";
                            using var cmdAvg = connection.CreateCommand();
                            cmdAvg.CommandText = sqlAvg;
                            cmdAvg.Parameters.AddWithValue("@Name", state.ProcessName);
                            double avgBytesPerSample = Convert.ToDouble(await cmdAvg.ExecuteScalarAsync() ?? 0.0);
                            state.HistoricalBaselineRate = avgBytesPerSample / 10.0;
                        }
                    }
                }
                catch
                {
                    // Fail silently
                }
            });
        }

        if (state.HistoricalBaselineRate.HasValue && state.HistoricalBaselineRate.Value > 10240.0)
        {
            double currentRate = state.DownloadRateBytesPerSec + state.UploadRateBytesPerSec;
            double baseline = state.HistoricalBaselineRate.Value;
            double deviation = currentRate - baseline;
            double devPct = (deviation / baseline) * 100.0;

            if (currentRate > 3 * baseline && deviation > 512_000)
            {
                PublishLiveEvent(DataSenseEventType.ProcessTrafficSpike,
                    "Unusually High Current Traffic",
                    $"{state.ProcessName} is consuming unusually high current traffic. Current: {ByteFormatter.FormatSpeed(currentRate)}, Historical Baseline: {ByteFormatter.FormatSpeed(baseline)}, Deviation: {ByteFormatter.FormatSpeed(deviation)} ({devPct:F0}%).",
                    EventSeverity.Warning,
                    $"Spike_{state.ProcessName}_{DateTime.UtcNow:yyyyMMddHH}");
            }
        }
    }
}
