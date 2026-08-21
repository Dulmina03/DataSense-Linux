using System;
using System.Collections.Generic;

namespace DataSense.Models;

public class BackupConfiguration
{
    public bool IsEnabled { get; set; } = true;
    public string BackupDirectory { get; set; } = string.Empty;
    public string Frequency { get; set; } = "Daily";
    public int RetentionCount { get; set; } = 7;
    public long MaxStorageBytes { get; set; } = 2L * 1024 * 1024 * 1024; // 2 GB
    public bool CompressBackups { get; set; } = true;
    public bool ValidateAfterCreation { get; set; } = true;
    public bool BackupBeforeRestore { get; set; } = true;
}

public class RecoveryPoint
{
    public string FilePath { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public string BackupType { get; init; } = "Automatic";
    public long SizeBytes { get; init; }
    public bool IsValid { get; init; } = true;
    public string Checksum { get; init; } = string.Empty;
}

public class BackupHealth
{
    public int TotalBackups { get; init; }
    public int ValidBackups { get; init; }
    public long TotalStorageBytes { get; init; }
    public DateTime? LatestValidBackupAt { get; init; }
    public string LatestBackupStatus { get; init; } = "Protected";
}
