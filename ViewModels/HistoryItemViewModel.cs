using System;
using DataSense.Helpers;
using DataSense.Models;

namespace DataSense.ViewModels;

public class HistoryItemViewModel
{
    private readonly NetworkUsageRecord _record;

    public HistoryItemViewModel(NetworkUsageRecord record)
    {
        _record = record ?? throw new ArgumentNullException(nameof(record));
    }

    public string TimestampText => _record.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string InterfaceName => _record.InterfaceName;
    public string DownloadSpeedText => ByteFormatter.FormatSpeed(_record.DownloadSpeed);
    public string UploadSpeedText => ByteFormatter.FormatSpeed(_record.UploadSpeed);
    public string TotalDownloadedText => ByteFormatter.FormatBytes(_record.BytesReceived);
    public string TotalUploadedText => ByteFormatter.FormatBytes(_record.BytesSent);
}
