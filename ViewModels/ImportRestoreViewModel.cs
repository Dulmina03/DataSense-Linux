using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataSense.Models;
using DataSense.Services;

namespace DataSense.ViewModels;

public partial class ImportRestoreViewModel : ViewModelBase
{
    private readonly IImportRestoreService _importRestoreService;

    public override string Title => "Import & Restore";

    [ObservableProperty] private string _selectedFilePath = string.Empty;
    [ObservableProperty] private string _statusMessage = "Select a CSV, JSON, or ZIP backup to preview.";
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private ImportPreview? _preview;

    public ImportRestoreViewModel(IImportRestoreService importRestoreService)
    {
        _importRestoreService = importRestoreService ?? throw new ArgumentNullException(nameof(importRestoreService));
    }

    [RelayCommand]
    private async Task SelectFileAndPreviewAsync(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        SelectedFilePath = path;
        try
        {
            Preview = await _importRestoreService.GeneratePreviewAsync(path);
            StatusMessage = $"File ready: {Preview.TotalRecords} records found.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error inspecting file: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExecuteImportAsync()
    {
        if (string.IsNullOrEmpty(SelectedFilePath)) return;
        IsProcessing = true;
        StatusMessage = "Processing import operation...";

        var result = await _importRestoreService.ImportDataAsync(SelectedFilePath, ImportMode.Merge);
        IsProcessing = false;

        if (result.Success)
        {
            StatusMessage = $"Import completed successfully! {result.ImportedRecords} records imported.";
        }
        else
        {
            StatusMessage = $"Import failed: {result.ErrorMessage}";
        }
    }
}
