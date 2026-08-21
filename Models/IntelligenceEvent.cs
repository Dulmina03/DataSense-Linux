using System;

namespace DataSense.Models;

/// <summary>
/// Unified representation for all DataSense intelligence events across network, application, budget, anomaly, and health systems.
/// </summary>
public class IntelligenceEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public IntelligencePriority Priority { get; set; } = IntelligencePriority.Info;
    public IntelligenceEventType Type { get; set; } = IntelligenceEventType.Network;

    public string? ApplicationName { get; set; }
    public string? NetworkName { get; set; }

    public double? Value { get; set; }
    public double? Percentage { get; set; }
    public string? ActionableStep { get; set; }

    public string PriorityColor => Priority switch
    {
        IntelligencePriority.Critical => "#FF5252",
        IntelligencePriority.High     => "#FF9800",
        IntelligencePriority.Medium   => "#00D2FF",
        IntelligencePriority.Low      => "#00E676",
        _                             => "#888899"
    };

    public string PriorityLabel => Priority switch
    {
        IntelligencePriority.Critical => "Critical",
        IntelligencePriority.High     => "High Priority",
        IntelligencePriority.Medium   => "Notice",
        IntelligencePriority.Low      => "Normal",
        _                             => "Info"
    };

    public string TypeIcon => Type switch
    {
        IntelligenceEventType.Anomaly      => "⚡",
        IntelligenceEventType.Budget       => "💰",
        IntelligenceEventType.Application  => "📱",
        IntelligenceEventType.Network      => "🌐",
        IntelligenceEventType.Forecast     => "📈",
        IntelligenceEventType.SystemHealth => "🏥",
        _                                  => "💡"
    };

    public string FormattedTimeAgo
    {
        get
        {
            var span = DateTime.UtcNow - Timestamp;
            if (span.TotalMinutes < 1) return "Just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
            return $"{(int)span.TotalDays}d ago";
        }
    }
}
