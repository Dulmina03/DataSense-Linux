using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DataSense.Database;
using DataSense.Helpers;
using DataSense.Models;

namespace DataSense.Services;

public interface IExportService
{
    Task<ExportResult> ExportDataAsync(ExportOptions options, IProgress<int>? progress = null, CancellationToken cancellationToken = default);
    Task<ExportResult> ExportCurrentSnapshotAsync(string targetFilePath, NetworkInterfaceStats? currentInterface, IEnumerable<LiveProcessRankItem>? topProcesses, CancellationToken cancellationToken = default);
    Task<ExportResult> CreateCompleteBackupAsync(string targetZipPath, CancellationToken cancellationToken = default);
    Task<bool> ValidateBackupAsync(string zipFilePath);
    Task<bool> RestoreBackupAsync(string zipFilePath, CancellationToken cancellationToken = default);
}

public class ExportService : IExportService
{
    private readonly INetworkUsageRepository _repository;
    private readonly IAnalyticsService _analyticsService;

    public ExportService(INetworkUsageRepository repository, IAnalyticsService analyticsService)
    {
        _repository       = repository       ?? throw new ArgumentNullException(nameof(repository));
        _analyticsService = analyticsService ?? throw new ArgumentNullException(nameof(analyticsService));
    }

    public async Task<ExportResult> ExportDataAsync(ExportOptions options, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        if (string.IsNullOrEmpty(options.OutputDirectory))
        {
            options.OutputDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DataSense");
        }
        Directory.CreateDirectory(options.OutputDirectory);

        try
        {
            string fileName = $"DataSense_{options.DataType}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.{options.Format.ToString().ToLower()}";
            string filePath = Path.Combine(options.OutputDirectory, fileName);
            long recordsCount = 0;

            if (options.Format == ExportFormat.CSV)
            {
                recordsCount = await ExportCsvAsync(options, filePath, progress, cancellationToken);
            }
            else if (options.Format == ExportFormat.JSON)
            {
                recordsCount = await ExportJsonAsync(options, filePath, progress, cancellationToken);
            }
            else if (options.Format == ExportFormat.TXT)
            {
                recordsCount = await ExportReportTxtAsync(options, filePath, cancellationToken);
            }

            var fileInfo = new FileInfo(filePath);
            return new ExportResult
            {
                Success = true,
                FilePath = filePath,
                RecordsExported = recordsCount,
                Duration = DateTime.UtcNow - startTime,
                FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0
            };
        }
        catch (OperationCanceledException)
        {
            return new ExportResult { Success = false, CancellationRequested = true, ErrorMessage = "Export cancelled by user." };
        }
        catch (Exception ex)
        {
            return new ExportResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private async Task<long> ExportCsvAsync(ExportOptions options, string filePath, IProgress<int>? progress, CancellationToken ct)
    {
        using var writer = new StreamWriter(filePath, false, Encoding.UTF8);

        if (options.DataType == ExportDataType.Usage)
        {
            await writer.WriteLineAsync("Timestamp,DownloadedBytes,UploadedBytes,Interface");
            var (records, count) = await _repository.GetHistoryPagedAsync(options.StartDate, options.EndDate, options.SelectedNetwork, 0, 100000);
            int idx = 0;
            foreach (var r in records)
            {
                ct.ThrowIfCancellationRequested();
                string iface = options.AnonymizeNetworkNames ? "Network_1" : r.InterfaceName;
                await writer.WriteLineAsync($"{r.Timestamp:o},{r.BytesReceived},{r.BytesSent},{iface}");
                idx++;
                if (count > 0 && idx % 100 == 0) progress?.Report((int)((idx / (double)count) * 100));
            }
            return count;
        }
        else if (options.DataType == ExportDataType.NetworkSessions)
        {
            await writer.WriteLineAsync("NetworkName,ConnectionType,StartTime,EndTime,BytesDownloaded,BytesUploaded");
            var sessions = await _repository.GetSessionsAsync(options.StartDate, options.EndDate, null, options.SelectedNetwork);
            var list = sessions.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var s = list[i];
                string name = options.AnonymizeNetworkNames ? "Network_Anon" : s.NetworkName;
                await writer.WriteLineAsync($"\"{name}\",{s.ConnectionType},{s.StartTime:o},{s.EndTime:o},{s.BytesDownloaded},{s.BytesUploaded}");
                if (list.Count > 0) progress?.Report((int)((i / (double)list.Count) * 100));
            }
            return list.Count;
        }
        else if (options.DataType == ExportDataType.Applications)
        {
            await writer.WriteLineAsync("Timestamp,ProcessName,ExecutablePath,UserName,BytesDownloaded,BytesUploaded,TotalBytes,DataSource");
            var procs = await _repository.GetTopProcessesAsync(options.StartDate, options.EndDate, 10000);
            var list = procs.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var p = list[i];
                string pName = options.AnonymizeApplicationNames ? $"App_{i + 1}" : p.ProcessName;
                string execPath = options.AnonymizeApplicationNames ? "/usr/bin/anon" : p.ExecutablePath;
                await writer.WriteLineAsync($"{p.Timestamp:o},\"{pName}\",\"{execPath}\",\"{p.UserName}\",{p.BytesDownloaded},{p.BytesUploaded},{p.TotalBytes},\"{p.DataSource}\"");
                if (list.Count > 0) progress?.Report((int)((i / (double)list.Count) * 100));
            }
            return list.Count;
        }

        return 0;
    }

    private async Task<long> ExportJsonAsync(ExportOptions options, string filePath, IProgress<int>? progress, CancellationToken ct)
    {
        if (options.DataType == ExportDataType.Applications)
        {
            var procs = await _repository.GetTopProcessesAsync(options.StartDate, options.EndDate, 10000);
            var procList = procs.ToList();
            var procData = new
            {
                Metadata = new { Application = "DataSense", ExportedAt = DateTime.UtcNow, DataType = "Applications", Version = "1.0.0" },
                Records = procList.Select((p, idx) => new
                {
                    Timestamp = p.Timestamp,
                    ProcessName = options.AnonymizeApplicationNames ? $"App_{idx + 1}" : p.ProcessName,
                    ExecutablePath = options.AnonymizeApplicationNames ? "/usr/bin/anon" : p.ExecutablePath,
                    UserName = p.UserName,
                    BytesDownloaded = p.BytesDownloaded,
                    BytesUploaded = p.BytesUploaded,
                    TotalBytes = p.TotalBytes,
                    DataSource = p.DataSource
                })
            };

            using var procStream = File.Create(filePath);
            await JsonSerializer.SerializeAsync(procStream, procData, new JsonSerializerOptions { WriteIndented = true }, ct);
            return procList.Count;
        }
        else if (options.DataType == ExportDataType.NetworkSessions)
        {
            var sessions = await _repository.GetSessionsAsync(options.StartDate, options.EndDate, null, options.SelectedNetwork);
            var sessionList = sessions.ToList();
            var sessionData = new
            {
                Metadata = new { Application = "DataSense", ExportedAt = DateTime.UtcNow, DataType = "NetworkSessions", Version = "1.0.0" },
                Records = sessionList.Select(s => new
                {
                    NetworkName = options.AnonymizeNetworkNames ? "Network_Anon" : s.NetworkName,
                    ConnectionType = s.ConnectionType,
                    StartTime = s.StartTime,
                    EndTime = s.EndTime,
                    BytesDownloaded = s.BytesDownloaded,
                    BytesUploaded = s.BytesUploaded,
                    TotalBytes = s.BytesDownloaded + s.BytesUploaded
                })
            };

            using var sessionStream = File.Create(filePath);
            await JsonSerializer.SerializeAsync(sessionStream, sessionData, new JsonSerializerOptions { WriteIndented = true }, ct);
            return sessionList.Count;
        }

        var (records, count) = await _repository.GetHistoryPagedAsync(options.StartDate, options.EndDate, options.SelectedNetwork, 0, 100000);
        var data = new
        {
            Metadata = new { Application = "DataSense", ExportedAt = DateTime.UtcNow, Version = "1.0.0" },
            Records = records.Select(r => new
            {
                r.Timestamp,
                r.BytesReceived,
                r.BytesSent,
                Interface = options.AnonymizeNetworkNames ? "Network_Anon" : r.InterfaceName
            })
        };

        using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, data, new JsonSerializerOptions { WriteIndented = true }, ct);
        return count;
    }

    private async Task<long> ExportReportTxtAsync(ExportOptions options, string filePath, CancellationToken ct)
    {
        var today = await _repository.GetTodaySummaryAsync();
        var month = await _repository.GetMonthSummaryAsync();

        var sb = new StringBuilder();
        sb.AppendLine("DataSense Usage Summary Report");
        sb.AppendLine("==============================");
        sb.AppendLine($"Generated At: {DateTime.UtcNow:u}");
        sb.AppendLine($"Period: {options.StartDate:d} to {options.EndDate:d}");
        sb.AppendLine();
        sb.AppendLine("Today Telemetry Summary");
        sb.AppendLine("-----------------------");
        sb.AppendLine($"Downloaded : {ByteFormatter.FormatBytes(today.BytesDownloaded)}");
        sb.AppendLine($"Uploaded   : {ByteFormatter.FormatBytes(today.BytesUploaded)}");
        sb.AppendLine($"Total      : {ByteFormatter.FormatBytes(today.BytesDownloaded + today.BytesUploaded)}");
        sb.AppendLine();
        sb.AppendLine("Monthly Cumulative Telemetry");
        sb.AppendLine("----------------------------");
        sb.AppendLine($"Downloaded : {ByteFormatter.FormatBytes(month.BytesDownloaded)}");
        sb.AppendLine($"Uploaded   : {ByteFormatter.FormatBytes(month.BytesUploaded)}");
        sb.AppendLine($"Total      : {ByteFormatter.FormatBytes(month.BytesDownloaded + month.BytesUploaded)}");

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8, ct);
        return 1;
    }

    public async Task<ExportResult> CreateCompleteBackupAsync(string targetZipPath, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        try
        {
            string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DataSense");
            string dbPath = Path.Combine(appDataFolder, "datasense.db");

            if (!File.Exists(dbPath))
            {
                return new ExportResult { Success = false, ErrorMessage = "Source database file not found." };
            }

            if (File.Exists(targetZipPath)) File.Delete(targetZipPath);

            using var archive = ZipFile.Open(targetZipPath, ZipArchiveMode.Create);
            archive.CreateEntryFromFile(dbPath, "datasense.db");

            var info = new { Application = "DataSense", BackupDate = DateTime.UtcNow, Version = "1.0.0" };
            var infoEntry = archive.CreateEntry("backup-info.json");
            using (var writer = new StreamWriter(infoEntry.Open()))
            {
                await writer.WriteAsync(JsonSerializer.Serialize(info));
            }

            var zipInfo = new FileInfo(targetZipPath);
            return new ExportResult
            {
                Success = true,
                FilePath = targetZipPath,
                RecordsExported = 1,
                Duration = DateTime.UtcNow - startTime,
                FileSizeBytes = zipInfo.Length
            };
        }
        catch (Exception ex)
        {
            return new ExportResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<bool> ValidateBackupAsync(string zipFilePath)
    {
        if (!File.Exists(zipFilePath)) return false;
        try
        {
            using var archive = ZipFile.OpenRead(zipFilePath);
            var dbEntry = archive.GetEntry("datasense.db");
            var infoEntry = archive.GetEntry("backup-info.json");
            return dbEntry != null && infoEntry != null;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> RestoreBackupAsync(string zipFilePath, CancellationToken cancellationToken = default)
    {
        if (!await ValidateBackupAsync(zipFilePath)) return false;

        try
        {
            string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DataSense");
            string dbPath = Path.Combine(appDataFolder, "datasense.db");

            // Safety backup of current db
            if (File.Exists(dbPath))
            {
                File.Copy(dbPath, dbPath + ".bak", overwrite: true);
            }

            using var archive = ZipFile.OpenRead(zipFilePath);
            var entry = archive.GetEntry("datasense.db");
            entry?.ExtractToFile(dbPath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<ExportResult> ExportCurrentSnapshotAsync(
        string targetFilePath,
        NetworkInterfaceStats? currentInterface,
        IEnumerable<LiveProcessRankItem>? topProcesses,
        CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            string dir = Path.GetDirectoryName(targetFilePath) ?? string.Empty;
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var snapshotData = new
            {
                Metadata = new
                {
                    Application = "DataSense Live Traffic Monitor",
                    Timestamp = DateTime.UtcNow,
                    Type = "LiveSnapshot"
                },
                Interface = currentInterface != null ? new
                {
                    currentInterface.InterfaceName,
                    currentInterface.ConnectionType,
                    State = currentInterface.State,
                    DownloadSpeed = ByteFormatter.FormatSpeed(currentInterface.DownloadRateBytesPerSec),
                    UploadSpeed = ByteFormatter.FormatSpeed(currentInterface.UploadRateBytesPerSec),
                    currentInterface.RxErrors,
                    currentInterface.TxErrors,
                    currentInterface.RxDropped,
                    currentInterface.TxDropped
                } : null,
                TopProcesses = (topProcesses ?? Array.Empty<LiveProcessRankItem>()).Select(p => new
                {
                    p.ProcessName,
                    p.Pid,
                    p.DownloadRateText,
                    p.UploadRateText,
                    p.CombinedRateText,
                    Percentage = $"{p.PercentageOfTotalTraffic:F1}%",
                    p.ExecutablePath,
                    p.UserName
                }).ToList()
            };

            using var stream = File.Create(targetFilePath);
            await JsonSerializer.SerializeAsync(stream, snapshotData, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
            sw.Stop();

            var fileInfo = new FileInfo(targetFilePath);
            return new ExportResult
            {
                Success = true,
                FilePath = targetFilePath,
                RecordsExported = snapshotData.TopProcesses.Count,
                Duration = sw.Elapsed,
                FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ExportResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}
