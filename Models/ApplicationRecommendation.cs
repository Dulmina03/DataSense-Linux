using System;

namespace DataSense.Models;

public enum RecommendationImpact
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Actionable smart recommendation generated locally from application telemetry.
/// </summary>
public class ApplicationRecommendation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ProcessName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ActionableStep { get; set; } = string.Empty;
    public RecommendationImpact Impact { get; set; } = RecommendationImpact.Low;
    public long PotentialSavingsBytes { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public string FormattedPotentialSavings => PotentialSavingsBytes > 0
        ? FormatBytes(PotentialSavingsBytes)
        : string.Empty;

    public string ImpactColor => Impact switch
    {
        RecommendationImpact.Critical => "#FF5252",
        RecommendationImpact.High     => "#FF9800",
        RecommendationImpact.Medium   => "#00D2FF",
        _                             => "#888899"
    };

    public string ImpactLabel => Impact switch
    {
        RecommendationImpact.Critical => "High Impact",
        RecommendationImpact.High     => "High Impact",
        RecommendationImpact.Medium   => "Medium Impact",
        _                             => "Tip"
    };

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0) bytes = 0;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < units.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {units[order]}";
    }
}
