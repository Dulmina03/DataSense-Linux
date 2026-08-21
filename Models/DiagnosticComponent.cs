using System;
using DataSense.Services;

namespace DataSense.Models;

public class DiagnosticComponent
{
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Category { get; init; } = "Core";
    public SubsystemState Status { get; set; } = SubsystemState.Healthy;
    public string Message { get; set; } = "Operational";
    public string DetailedMessage { get; set; } = string.Empty;
    public DateTime? LastHealthyAt { get; set; }
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastErrorAt { get; set; }
    public int RetryCount { get; set; }
    public bool IsRequired { get; init; } = true;
    public bool CanRecoverAutomatically { get; init; } = true;
    public string RecommendedAction { get; set; } = "No action required.";
}
