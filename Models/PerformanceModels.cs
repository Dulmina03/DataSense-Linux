using System;

namespace DataSense.Models;

public class PerformanceSnapshot
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public double ProcessCpuPercentage { get; init; }
    public long WorkingSetBytes { get; init; }
    public long ManagedMemoryBytes { get; init; }
    public long PrivateMemoryBytes { get; init; }
    public int ThreadCount { get; init; }
    public int GcGen0Collections { get; init; }
    public int GcGen1Collections { get; init; }
    public int GcGen2Collections { get; init; }
}

public class PerformanceMetric
{
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = "Core";
    public double LastDurationMs { get; set; }
    public double AverageDurationMs { get; set; }
    public double MaxDurationMs { get; set; }
    public double MinDurationMs { get; set; }
    public long InvocationCount { get; set; }
    public DateTime LastExecutedAt { get; set; } = DateTime.UtcNow;
    public int SlowOperationCount { get; set; }
}

public class PerformanceRecommendation
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Severity { get; init; } = "Info"; // Info, Warning, Critical
    public string Evidence { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
}
