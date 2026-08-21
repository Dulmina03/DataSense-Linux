using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

public interface IPerformanceMonitorService : IDisposable
{
    bool IsMonitoringEnabled { get; set; }
    PerformanceSnapshot GetCurrentSnapshot();
    IReadOnlyList<PerformanceSnapshot> GetSnapshotHistory();
    IReadOnlyList<PerformanceMetric> GetOperationMetrics();
    IReadOnlyList<PerformanceRecommendation> GetRecommendations();
    
    void RecordOperationDuration(string name, string category, double durationMs, double slowThresholdMs = 200.0);
    void PauseMonitoring();
    void ResumeMonitoring();
    void ClearHistory();
    string GenerateReportSummary();
}

public class PerformanceMonitorService : IPerformanceMonitorService
{
    private readonly ConcurrentQueue<PerformanceSnapshot> _history = new();
    private readonly ConcurrentDictionary<string, PerformanceMetric> _operationMetrics = new();
    private readonly ISystemHealthRegistry _healthRegistry;
    private readonly CancellationTokenSource _cts = new();

    private bool _isMonitoringEnabled = true;
    public bool IsMonitoringEnabled
    {
        get => _isMonitoringEnabled;
        set => _isMonitoringEnabled = value;
    }

    private TimeSpan _prevProcessTime = TimeSpan.Zero;
    private DateTime _prevTime = DateTime.MinValue;

    public PerformanceMonitorService(ISystemHealthRegistry healthRegistry)
    {
        _healthRegistry = healthRegistry ?? throw new ArgumentNullException(nameof(healthRegistry));
        _ = Task.Run(() => StartSamplingLoopAsync(_cts.Token));
    }

    private async Task StartSamplingLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_isMonitoringEnabled)
                {
                    var snapshot = CollectProcessSnapshot();
                    _history.Enqueue(snapshot);

                    // Maintain 10 minutes rolling history (200 samples @ 3s interval)
                    while (_history.Count > 200)
                    {
                        _history.TryDequeue(out _);
                    }

                    // Register with health registry if CPU is elevated (> 80%)
                    if (snapshot.ProcessCpuPercentage > 80.0)
                    {
                        _healthRegistry.ReportHealth("PerformanceMonitor", SubsystemState.Degraded,
                            $"Elevated Process CPU usage: {snapshot.ProcessCpuPercentage:F1}%");
                    }
                    else
                    {
                        _healthRegistry.ReportHealth("PerformanceMonitor", SubsystemState.Healthy, "Operational");
                    }
                }
            }
            catch (Exception ex)
            {
                _healthRegistry.ReportHealth("PerformanceMonitor", SubsystemState.Degraded, $"Sampling error: {ex.Message}");
            }

            try
            {
                await timer.WaitForNextTickAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private PerformanceSnapshot CollectProcessSnapshot()
    {
        var proc = Process.GetCurrentProcess();
        proc.Refresh();

        double cpuPercent = 0.0;
        var now = DateTime.UtcNow;
        var totalCpuTime = proc.TotalProcessorTime;

        if (_prevTime != DateTime.MinValue)
        {
            var timePassed = (now - _prevTime).TotalMilliseconds;
            var cpuPassed = (totalCpuTime - _prevProcessTime).TotalMilliseconds;
            if (timePassed > 0)
            {
                cpuPercent = (cpuPassed / (timePassed * Environment.ProcessorCount)) * 100.0;
                if (cpuPercent < 0) cpuPercent = 0;
            }
        }

        _prevTime = now;
        _prevProcessTime = totalCpuTime;

        return new PerformanceSnapshot
        {
            Timestamp = now,
            ProcessCpuPercentage = Math.Round(cpuPercent, 1),
            WorkingSetBytes = proc.WorkingSet64,
            ManagedMemoryBytes = GC.GetTotalMemory(false),
            PrivateMemoryBytes = proc.PrivateMemorySize64,
            ThreadCount = proc.Threads.Count,
            GcGen0Collections = GC.CollectionCount(0),
            GcGen1Collections = GC.CollectionCount(1),
            GcGen2Collections = GC.CollectionCount(2)
        };
    }

    public PerformanceSnapshot GetCurrentSnapshot()
    {
        if (_history.TryPeek(out var latest)) return latest;
        return CollectProcessSnapshot();
    }

    public IReadOnlyList<PerformanceSnapshot> GetSnapshotHistory()
    {
        return _history.ToList();
    }

    public IReadOnlyList<PerformanceMetric> GetOperationMetrics()
    {
        return _operationMetrics.Values.OrderByDescending(m => m.AverageDurationMs).ToList();
    }

    public void RecordOperationDuration(string name, string category, double durationMs, double slowThresholdMs = 200.0)
    {
        if (!_isMonitoringEnabled) return;

        var metric = _operationMetrics.GetOrAdd(name, n => new PerformanceMetric
        {
            Name = n,
            Category = category,
            MinDurationMs = durationMs,
            MaxDurationMs = durationMs,
            AverageDurationMs = durationMs
        });

        lock (metric)
        {
            metric.LastDurationMs = durationMs;
            metric.LastExecutedAt = DateTime.UtcNow;
            metric.InvocationCount++;

            if (durationMs > metric.MaxDurationMs) metric.MaxDurationMs = durationMs;
            if (durationMs < metric.MinDurationMs) metric.MinDurationMs = durationMs;

            // EWMA calculation for average duration
            metric.AverageDurationMs = (metric.AverageDurationMs * 0.8) + (durationMs * 0.2);

            if (durationMs > slowThresholdMs)
            {
                metric.SlowOperationCount++;
            }
        }
    }

    public IReadOnlyList<PerformanceRecommendation> GetRecommendations()
    {
        var recs = new List<PerformanceRecommendation>();
        var snapshot = GetCurrentSnapshot();

        if (snapshot.ProcessCpuPercentage > 70.0)
        {
            recs.Add(new PerformanceRecommendation
            {
                Title = "High Process CPU Usage",
                Description = $"DataSense CPU consumption is currently at {snapshot.ProcessCpuPercentage:F1}%.",
                Severity = "Warning",
                Evidence = $"Measured {snapshot.ProcessCpuPercentage:F1}% CPU across {snapshot.ThreadCount} active threads.",
                Recommendation = "Consider reducing active chart refresh rate or checking process monitor nethogs activity."
            });
        }

        var slowOps = _operationMetrics.Values.Where(m => m.SlowOperationCount > 0).ToList();
        foreach (var op in slowOps)
        {
            recs.Add(new PerformanceRecommendation
            {
                Title = $"Slow Operation Detected: {op.Name}",
                Description = $"Operation '{op.Name}' exceeded slow threshold {op.SlowOperationCount} times.",
                Severity = "Info",
                Evidence = $"Average duration: {op.AverageDurationMs:F1} ms (Max: {op.MaxDurationMs:F1} ms).",
                Recommendation = "Inspect query indexing or reduce history fetch window size."
            });
        }

        if (recs.Count == 0)
        {
            recs.Add(new PerformanceRecommendation
            {
                Title = "Optimal System Performance",
                Description = "All DataSense subsystems and background tasks are running cleanly.",
                Severity = "Info",
                Evidence = $"Working Set: {snapshot.WorkingSetBytes / (1024 * 1024)} MB | Threads: {snapshot.ThreadCount}",
                Recommendation = "No optimization action required."
            });
        }

        return recs;
    }

    public void PauseMonitoring() => _isMonitoringEnabled = false;
    public void ResumeMonitoring() => _isMonitoringEnabled = true;

    public void ClearHistory()
    {
        while (_history.TryDequeue(out _)) { }
        _operationMetrics.Clear();
    }

    public string GenerateReportSummary()
    {
        var snap = GetCurrentSnapshot();
        return $"[DataSense Performance Summary]\n" +
               $"Timestamp: {snap.Timestamp:u}\n" +
               $"Process CPU: {snap.ProcessCpuPercentage:F1}%\n" +
               $"Working Set: {snap.WorkingSetBytes / (1024 * 1024)} MB\n" +
               $"Managed Heap: {snap.ManagedMemoryBytes / (1024 * 1024)} MB\n" +
               $"Threads: {snap.ThreadCount}\n" +
               $"GC Collections (Gen0/1/2): {snap.GcGen0Collections}/{snap.GcGen1Collections}/{snap.GcGen2Collections}\n" +
               $"Tracked Operations: {_operationMetrics.Count}";
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
