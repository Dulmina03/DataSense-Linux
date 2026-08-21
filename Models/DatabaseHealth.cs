using System;

namespace DataSense.Models;

public class DatabaseHealth
{
    public string DatabasePath { get; init; } = string.Empty;
    public long DatabaseSizeBytes { get; init; }
    public string DatabaseSizeFormatted { get; init; } = "0 B";
    public long TotalRecords { get; init; }
    public DateTime? OldestRecord { get; init; }
    public DateTime? NewestRecord { get; init; }
    public DateTime LastCleanupAt { get; init; } = DateTime.MinValue;
    public DateTime LastMaintenanceAt { get; init; } = DateTime.MinValue;
    public bool IsHealthy { get; init; } = true;
    public string StatusMessage { get; init; } = "Optimal";
}
