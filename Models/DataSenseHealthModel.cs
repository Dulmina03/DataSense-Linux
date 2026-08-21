using System;

namespace DataSense.Models;

public enum DataSenseHealthStatus
{
    Optimal,
    Operational,
    Degraded,
    CollectingTelemetry
}

/// <summary>
/// Deterministic operational health observatory model for DataSense itself.
/// </summary>
public class DataSenseHealthModel
{
    public DataSenseHealthStatus Status { get; set; } = DataSenseHealthStatus.Operational;

    public bool IsNetworkWorkerActive { get; set; }
    public bool IsProcessWorkerActive { get; set; }
    public bool IsDatabaseAccessible { get; set; }

    public long DatabaseRecordCount { get; set; }
    public string ActiveSessionName { get; set; } = "None";

    public bool HasSufficientTelemetry { get; set; }
    public string OperationalSummary { get; set; } = string.Empty;

    public DateTime LastChecked { get; set; } = DateTime.UtcNow;

    public string StatusLabel => Status switch
    {
        DataSenseHealthStatus.Optimal             => "System Optimal",
        DataSenseHealthStatus.Operational         => "System Operational",
        DataSenseHealthStatus.CollectingTelemetry => "Collecting Baseline",
        DataSenseHealthStatus.Degraded            => "Service Degraded",
        _                                         => "Status Unknown"
    };

    public string StatusColor => Status switch
    {
        DataSenseHealthStatus.Optimal             => "#00E676",
        DataSenseHealthStatus.Operational         => "#00D2FF",
        DataSenseHealthStatus.CollectingTelemetry => "#FF9800",
        DataSenseHealthStatus.Degraded            => "#FF5252",
        _                                         => "#888899"
    };

    public string StatusBadgeIcon => Status switch
    {
        DataSenseHealthStatus.Optimal             => "🟢",
        DataSenseHealthStatus.Operational         => "🔵",
        DataSenseHealthStatus.CollectingTelemetry => "🟠",
        DataSenseHealthStatus.Degraded            => "🔴",
        _                                         => "⚪"
    };
}
