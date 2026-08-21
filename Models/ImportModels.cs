using System;

namespace DataSense.Models;

public enum ImportMode
{
    Merge,
    Replace
}

public class ImportPreview
{
    public string FilePath { get; init; } = string.Empty;
    public string Format { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public long TotalRecords { get; init; }
    public long ValidRecords { get; init; }
    public long InvalidRecords { get; init; }
    public long DuplicateRecords { get; init; }
    public DateTime? MinTimestamp { get; init; }
    public DateTime? MaxTimestamp { get; init; }
}

public class ImportResult
{
    public bool Success { get; init; }
    public long ImportedRecords { get; init; }
    public long SkippedRecords { get; init; }
    public long ErrorRecords { get; init; }
    public TimeSpan Duration { get; init; }
    public string? ErrorMessage { get; init; }
}
