using System;
using System.Collections.Concurrent;

namespace DataSense.Services;

/// <summary>
/// Simple in-memory metric tracking for operational durations and counts.
/// Lightweight and thread-safe.
/// </summary>
public class MetricSummary
{
    public string OperationName { get; }
    public long Count => _count;
    public double TotalMilliseconds => _totalMilliseconds;
    public double AverageMilliseconds => _count > 0 ? _totalMilliseconds / _count : 0;
    public double MaxMilliseconds => _maxMilliseconds;

    private long _count;
    private double _totalMilliseconds;
    private double _maxMilliseconds;

    public MetricSummary(string operationName)
    {
        OperationName = operationName;
    }

    public void Record(double durationMs)
    {
        System.Threading.Interlocked.Increment(ref _count);
        
        // Simple update without locks for performance
        _totalMilliseconds += durationMs;
        if (durationMs > _maxMilliseconds)
        {
            _maxMilliseconds = durationMs;
        }
    }
}

public interface IPerformanceMonitor
{
    bool IsEnabled { get; set; }
    IDisposable Measure(string operationName);
    void RecordMetric(string operationName, double durationMs);
    System.Collections.Generic.IReadOnlyDictionary<string, MetricSummary> GetMetrics();
}

public class PerformanceMonitor : IPerformanceMonitor
{
    public bool IsEnabled { get; set; } = true;
    private readonly ConcurrentDictionary<string, MetricSummary> _metrics = new();

    public IDisposable Measure(string operationName)
    {
        if (!IsEnabled) return StructDisposable.Instance;
        return new MeasureScope(this, operationName);
    }

    public void RecordMetric(string operationName, double durationMs)
    {
        if (!IsEnabled) return;
        var summary = _metrics.GetOrAdd(operationName, name => new MetricSummary(name));
        summary.Record(durationMs);
    }

    public System.Collections.Generic.IReadOnlyDictionary<string, MetricSummary> GetMetrics()
    {
        return _metrics;
    }

    private struct MeasureScope : IDisposable
    {
        private readonly PerformanceMonitor _monitor;
        private readonly string _name;
        private readonly long _startTimestamp;

        public MeasureScope(PerformanceMonitor monitor, string name)
        {
            _monitor = monitor;
            _name = name;
            _startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            long elapsedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - _startTimestamp;
            double elapsedMs = (double)elapsedTicks / System.Diagnostics.Stopwatch.Frequency * 1000.0;
            _monitor.RecordMetric(_name, elapsedMs);
        }
    }

    private class StructDisposable : IDisposable
    {
        public static readonly StructDisposable Instance = new();
        public void Dispose() { }
    }
}
