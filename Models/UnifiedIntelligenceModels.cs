using System;
using System.Collections.Generic;
using DataSense.Services;

namespace DataSense.Models;

public enum UnifiedInsightCategory
{
    Application,
    Network,
    Usage,
    Budget,
    Forecast,
    Anomaly,
    Performance,
    CrossDomain
}

public enum UnifiedInsightSeverity
{
    Info,
    Success,
    Warning,
    Critical
}

public enum UnifiedInsightConfidence
{
    InsufficientData,
    Low,
    Medium,
    High
}

public class UnifiedInsight
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public UnifiedInsightCategory Category { get; set; }
    public UnifiedInsightSeverity Severity { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public UnifiedInsightConfidence Confidence { get; set; } = UnifiedInsightConfidence.InsufficientData;
    
    // Optional relations
    public string? RelatedApplication { get; set; }
    public string? RelatedNetwork { get; set; }
    public string? RelatedMetric { get; set; }

    // Values (if applicable)
    public double? CurrentValue { get; set; }
    public double? HistoricalValue { get; set; }
    public double? PercentageChange { get; set; }

    public string RecommendedAction { get; set; } = string.Empty;
    
    // Supporting Evidence
    public List<string> Evidence { get; set; } = new();

    // Priority for sorting
    public int Priority { get; set; }
}

public class UnifiedSystemSummary
{
    public string CurrentNetwork { get; set; } = "Disconnected";
    public double CurrentDownloadSpeedBytesPerSec { get; set; }
    public double CurrentUploadSpeedBytesPerSec { get; set; }
    public long TodayTotalBytes { get; set; }
    public string TodayTopApplication { get; set; } = "None";
    public string TodayTopNetwork { get; set; } = "None";
    
    // High-level statuses
    public SubsystemState BudgetStatus { get; set; } = SubsystemState.Healthy;
    public SubsystemState ForecastStatus { get; set; } = SubsystemState.Healthy;
    public SubsystemState AnomalyStatus { get; set; } = SubsystemState.Healthy;
    public SubsystemState OverallIntelligenceStatus { get; set; } = SubsystemState.Healthy;

    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
