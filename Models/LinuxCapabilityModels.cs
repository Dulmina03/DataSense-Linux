using System;

namespace DataSense.Models;

public enum LinuxCapabilityStatus
{
    Available,
    Unavailable,
    RequiresSetup,
    Degraded
}

public class LinuxCapabilityItem
{
    public string CapabilityId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public LinuxCapabilityStatus Status { get; init; }
    public string Explanation { get; init; } = string.Empty;
    public string RecommendedAction { get; init; } = string.Empty;
    public string SetupCommand { get; init; } = string.Empty;
}
