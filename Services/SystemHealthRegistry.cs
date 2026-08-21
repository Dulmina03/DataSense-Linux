using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DataSense.Models;

namespace DataSense.Services;

public enum SubsystemState
{
    Healthy,
    Degraded,
    Unavailable,
    Error
}

public class SubsystemHealthReport
{
    public string Name { get; init; } = string.Empty;
    public SubsystemState State { get; set; } = SubsystemState.Healthy;
    public string Message { get; set; } = "Operational";
    public DateTime LastStatusUpdate { get; set; } = DateTime.UtcNow;
    public Exception? LastError { get; set; }
}

public interface ISystemHealthRegistry
{
    void RegisterSubsystem(string name);
    void ReportHealth(string name, SubsystemState state, string message, Exception? ex = null);
    SubsystemHealthReport GetReport(string name);
    IReadOnlyCollection<SubsystemHealthReport> GetAllReports();
    DataSenseHealthStatus OverallHealth { get; }
}

public class SystemHealthRegistry : ISystemHealthRegistry
{
    private readonly ConcurrentDictionary<string, SubsystemHealthReport> _subsystems = new();

    public SystemHealthRegistry()
    {
        RegisterSubsystem("NetworkMonitor");
        RegisterSubsystem("ProcessMonitor");
        RegisterSubsystem("SQLiteDatabase");
        RegisterSubsystem("ForecastService");
        RegisterSubsystem("SpeedTest");
    }

    public void RegisterSubsystem(string name)
    {
        _subsystems.TryAdd(name, new SubsystemHealthReport { Name = name });
    }

    public void ReportHealth(string name, SubsystemState state, string message, Exception? ex = null)
    {
        var report = _subsystems.GetOrAdd(name, n => new SubsystemHealthReport { Name = n });
        report.State = state;
        report.Message = message;
        report.LastStatusUpdate = DateTime.UtcNow;
        if (ex != null) report.LastError = ex;
    }

    public SubsystemHealthReport GetReport(string name)
    {
        if (_subsystems.TryGetValue(name, out var report))
            return report;

        return new SubsystemHealthReport { Name = name, State = SubsystemState.Unavailable, Message = "Unregistered" };
    }

    public IReadOnlyCollection<SubsystemHealthReport> GetAllReports()
    {
        return _subsystems.Values.ToList();
    }

    public DataSenseHealthStatus OverallHealth
    {
        get
        {
            var states = _subsystems.Values.Select(r => r.State).ToList();
            if (states.Any(s => s == SubsystemState.Error))
                return DataSenseHealthStatus.Degraded;
            if (states.Any(s => s == SubsystemState.Degraded || s == SubsystemState.Unavailable))
                return DataSenseHealthStatus.Operational;
            return DataSenseHealthStatus.Optimal;
        }
    }
}
