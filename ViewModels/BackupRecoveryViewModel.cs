using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataSense.Models;
using DataSense.Services;

namespace DataSense.ViewModels;

public partial class BackupRecoveryViewModel : ViewModelBase
{
    private readonly IBackupRecoveryService _backupService;

    public override string Title => "Backup & Recovery";

    [ObservableProperty] private string _statusText = "Protected";
    [ObservableProperty] private bool _isProcessing;

    public ObservableCollection<RecoveryPoint> RecoveryPoints { get; } = new();

    public BackupRecoveryViewModel(IBackupRecoveryService backupService)
    {
        _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
        Task.Run(async () => await RefreshRecoveryPointsAsync());
    }

    [RelayCommand]
    private async Task RefreshRecoveryPointsAsync()
    {
        var points = await _backupService.GetRecoveryPointsAsync();
        RecoveryPoints.Clear();
        foreach (var p in points)
        {
            RecoveryPoints.Add(p);
        }
    }

    [RelayCommand]
    private async Task CreateBackupNowAsync()
    {
        IsProcessing = true;
        StatusText = "Creating local disaster backup...";
        var res = await _backupService.CreateBackupAsync("Manual");
        IsProcessing = false;

        if (res.Success)
        {
            StatusText = "Backup created successfully!";
            await RefreshRecoveryPointsAsync();
        }
        else
        {
            StatusText = $"Backup failed: {res.ErrorMessage}";
        }
    }
}
