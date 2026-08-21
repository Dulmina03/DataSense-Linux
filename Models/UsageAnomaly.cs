using System;

namespace DataSense.Models;

/// <summary>
/// Container for a statistically detected usage anomaly.
/// </summary>
public class UsageAnomaly
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Target { get; set; } = string.Empty;
    public string AnomalyType { get; set; } = string.Empty;
    public AnomalySeverity Severity { get; set; } = AnomalySeverity.Info;
    public long CurrentValue { get; set; }
    public double ExpectedAverage { get; set; }
    public double NormalRangeLower { get; set; }
    public double NormalRangeUpper { get; set; }
    public double ZScore { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public string SeverityColor => Severity switch
    {
        AnomalySeverity.Critical => "#FF5252",
        AnomalySeverity.Warning  => "#FF9800",
        _                        => "#00D2FF"
    };

    public string SeverityLabel => Severity switch
    {
        AnomalySeverity.Critical => "Critical Anomaly",
        AnomalySeverity.Warning  => "Unusual Activity",
        _                        => "Notice"
    };
}
