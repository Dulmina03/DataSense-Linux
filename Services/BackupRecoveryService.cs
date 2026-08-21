using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

public interface IBackupRecoveryService
{
    Task<BackupHealth> InspectBackupHealthAsync();
    Task<IReadOnlyList<RecoveryPoint>> GetRecoveryPointsAsync();
    Task<ExportResult> CreateBackupAsync(string type = "Manual", CancellationToken ct = default);
    Task<bool> ValidateRecoveryPointAsync(string filePath);
}

public class BackupRecoveryService : IBackupRecoveryService
{
    private readonly IExportService _exportService;
    private readonly IEventService _eventService;
    private readonly SemaphoreSlim _backupLock = new(1, 1);

    public BackupRecoveryService(IExportService exportService, IEventService eventService)
    {
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _eventService  = eventService  ?? throw new ArgumentNullException(nameof(eventService));
    }

    private string GetBackupFolder()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DataSense", "backups");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task<IReadOnlyList<RecoveryPoint>> GetRecoveryPointsAsync()
    {
        string folder = GetBackupFolder();
        var files = Directory.GetFiles(folder, "*.zip");
        var list = new List<RecoveryPoint>();

        foreach (var file in files)
        {
            FileInfo fi = new(file);
            string hash = CalculateChecksum(file);
            list.Add(new RecoveryPoint
            {
                FilePath = file,
                CreatedAt = fi.CreationTimeUtc,
                BackupType = fi.Name.Contains("Automatic") ? "Automatic" : "Manual",
                SizeBytes = fi.Length,
                IsValid = true,
                Checksum = hash
            });
        }

        return list.OrderByDescending(r => r.CreatedAt).ToList();
    }

    public async Task<BackupHealth> InspectBackupHealthAsync()
    {
        var points = await GetRecoveryPointsAsync();
        long totalSize = points.Sum(p => p.SizeBytes);
        var latest = points.FirstOrDefault()?.CreatedAt;

        return new BackupHealth
        {
            TotalBackups = points.Count,
            ValidBackups = points.Count(p => p.IsValid),
            TotalStorageBytes = totalSize,
            LatestValidBackupAt = latest,
            LatestBackupStatus = points.Any() ? "Protected" : "No Backups"
        };
    }

    public async Task<ExportResult> CreateBackupAsync(string type = "Manual", CancellationToken ct = default)
    {
        await _backupLock.WaitAsync(ct);
        try
        {
            string folder = GetBackupFolder();
            string fileName = $"DataSense_{DateTime.UtcNow:yyyy-MM-dd_HHmmss}_{type}.zip";
            string targetPath = Path.Combine(folder, fileName);

            var result = await _exportService.CreateCompleteBackupAsync(targetPath, ct);

            if (result.Success)
            {
                _eventService.PublishEvent(new DataSenseEvent
                {
                    Title = "Disaster Backup Created",
                    Description = $"Automated local recovery point archive created cleanly ({fileName}).",
                    Severity = EventSeverity.Success,
                    Source = "BackupRecovery"
                });
            }

            return result;
        }
        finally
        {
            _backupLock.Release();
        }
    }

    public async Task<bool> ValidateRecoveryPointAsync(string filePath)
    {
        return await _exportService.ValidateBackupAsync(filePath);
    }

    private string CalculateChecksum(string filePath)
    {
        try
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }
}
