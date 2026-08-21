using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataSense.Models;
using DataSense.Services;

namespace DataSense.ViewModels;

public partial class ExportViewModel : ViewModelBase
{
    private readonly IExportService _exportService;

    public override string Title => "Export & Reports";

    [ObservableProperty] private int      _selectedFormatIndex = 0; // 0=CSV, 1=JSON, 2=TXT
    [ObservableProperty] private int      _selectedDataTypeIndex = 0; // 0=Usage, 1=Sessions
    [ObservableProperty] private bool     _anonymizeNetworks = false;
    [ObservableProperty] private string   _statusMessage = string.Empty;
    [ObservableProperty] private bool     _isExporting = false;
    [ObservableProperty] private int      _progressValue = 0;
    [ObservableProperty] private string   _backupStatusText = string.Empty;

    public ObservableCollection<ExportItemHistory> History { get; } = new();

    public ExportViewModel(IExportService exportService)
    {
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
    }

    [RelayCommand]
    private async Task StartExportAsync()
    {
        IsExporting = true;
        ProgressValue = 0;
        StatusMessage = "Exporting data...";

        var options = new ExportOptions
        {
            Format = SelectedFormatIndex switch { 1 => ExportFormat.JSON, 2 => ExportFormat.TXT, _ => ExportFormat.CSV },
            DataType = SelectedDataTypeIndex == 1 ? ExportDataType.NetworkSessions : ExportDataType.Usage,
            AnonymizeNetworkNames = AnonymizeNetworks
        };

        var progressHandler = new Progress<int>(percent => Dispatcher.UIThread.Post(() => ProgressValue = percent));

        var result = await _exportService.ExportDataAsync(options, progressHandler);

        IsExporting = false;
        if (result.Success)
        {
            StatusMessage = $"✅ Export complete! Saved {result.RecordsExported} records to {Path.GetFileName(result.FilePath)}";
            History.Insert(0, new ExportItemHistory
            {
                FileName = Path.GetFileName(result.FilePath),
                Type = options.Format.ToString(),
                Records = result.RecordsExported,
                SizeText = $"{result.FileSizeBytes / 1024.0:F1} KB",
                Status = "Success"
            });
        }
        else
        {
            StatusMessage = $"⚠️ Export failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        BackupStatusText = "Creating complete ZIP backup...";
        string docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DataSense");
        Directory.CreateDirectory(docs);
        string zipPath = Path.Combine(docs, $"DataSense-Backup-{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip");

        var result = await _exportService.CreateCompleteBackupAsync(zipPath);

        if (result.Success)
        {
            BackupStatusText = $"✅ Full backup saved: {Path.GetFileName(zipPath)} ({result.FileSizeBytes / (1024 * 1024.0):F2} MB)";
        }
        else
        {
            BackupStatusText = $"⚠️ Backup failed: {result.ErrorMessage}";
        }
    }
}
