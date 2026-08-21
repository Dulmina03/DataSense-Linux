using System;

namespace DataSense.Models;

public enum EventSeverity
{
    Info,
    Success,
    Warning,
    Critical
}

public enum DataSenseEventType
{
    BudgetWarning,
    BudgetCritical,
    ForecastWarning,
    UsageAnomaly,
    ApplicationAnomaly,
    NetworkAnomaly,
    NetworkChanged,
    SpeedTestCompleted,
    SpeedTestFailed,
    MonitoringUnavailable,
    MonitoringRecovered,
    DiagnosticWarning,
    PerformanceWarning,
    BackupCompleted,
    BackupFailed,
    RestoreCompleted,
    ProcessMonitorUnavailable,
    ProcessMonitorRecovered,
    ProcessMonitorPermissionDenied,
    ProcessMonitorBackendRestarted,
    TrafficSpikeDetected,
    ProcessTrafficSpike,
    InterfaceChanged,
    LiveMonitoringUnavailable,
    LiveMonitoringRecovered,
    LongSessionDetected,
    HighUsageSession,
    UnusualUploadSession,
    NetworkSwitch,
    InterruptedSession
}

public class DataSenseEvent
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DataSenseEventType EventType { get; init; }
    public EventSeverity Severity { get; init; } = EventSeverity.Info;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Source { get; init; } = "System";
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public bool IsRead { get; set; }
    public bool IsDismissed { get; set; }
    public string Fingerprint { get; init; } = string.Empty;
    public string? ActionText { get; init; }
    public string? NavigationTarget { get; init; }
}
