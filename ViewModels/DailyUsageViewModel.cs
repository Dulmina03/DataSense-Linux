using System;
using DataSense.Helpers;
using DataSense.Models;

namespace DataSense.ViewModels;

/// <summary>
/// Presentation model for one day's aggregated network usage.
/// </summary>
public class DailyUsageViewModel
{
    private readonly DailyUsageRecord _record;

    public DailyUsageViewModel(DailyUsageRecord record)
    {
        _record = record ?? throw new ArgumentNullException(nameof(record));
    }

    /// <summary>Display label for the day, e.g. "Aug 12".</summary>
    public string DayLabel => _record.Day.ToString("MMM dd");

    /// <summary>Full date label for tooltip / accessibility, e.g. "2026-08-12".</summary>
    public string DayFull => _record.Day.ToString("yyyy-MM-dd");

    public string DownloadedText => ByteFormatter.FormatBytes(_record.BytesDownloaded);
    public string UploadedText   => ByteFormatter.FormatBytes(_record.BytesUploaded);
    public string TotalText      => ByteFormatter.FormatBytes(_record.TotalBytes);

    public long BytesDownloaded => _record.BytesDownloaded;
    public long BytesUploaded   => _record.BytesUploaded;
    public long TotalBytes      => _record.TotalBytes;
}
