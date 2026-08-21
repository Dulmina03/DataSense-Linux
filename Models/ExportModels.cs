using System;

namespace DataSense.Models;

public enum ExportFormat
{
    CSV,
    JSON,
    TXT,
    ZIP
}

public enum ExportDataType
{
    Usage,
    NetworkSessions,
    SpeedTests,
    Applications,
    Analytics,
    Forecasts,
    Budgets,
    Anomalies,
    Complete
}

public class ExportOptions
{
    public ExportFormat Format { get; set; } = ExportFormat.CSV;
    public ExportDataType DataType { get; set; } = ExportDataType.Usage;
    public DateTime StartDate { get; set; } = DateTime.UtcNow.AddDays(-30);
    public DateTime EndDate { get; set; } = DateTime.UtcNow;
    public string? SelectedNetwork { get; set; }
    public string? SelectedApplication { get; set; }
    public bool IncludeDiagnostics { get; set; }
    public bool IncludePerformanceData { get; set; }
    public bool AnonymizeNetworkNames { get; set; }
    public bool AnonymizeApplicationNames { get; set; }
    public string OutputDirectory { get; set; } = string.Empty;
}

public class ExportResult
{
    public bool Success { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public long RecordsExported { get; init; }
    public TimeSpan Duration { get; init; }
    public long FileSizeBytes { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public bool CancellationRequested { get; init; }
}

public class ExportItemHistory
{
    public string FileName { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string SizeText { get; init; } = string.Empty;
    public long Records { get; init; }
    public string Status { get; init; } = "Success";
}
