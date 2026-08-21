using System;

namespace DataSense.Models;

public class NetworkInsight
{
    public InsightType Type { get; set; }
    public InsightSeverity Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ApplicationName { get; set; }
    public string? NetworkName { get; set; }
    public DateTime Timestamp { get; set; }
    public double? PercentageChange { get; set; }
    public double? CurrentValue { get; set; }
    public double? BaselineValue { get; set; }
}
