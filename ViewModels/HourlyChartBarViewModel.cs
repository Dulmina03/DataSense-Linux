using DataSense.Helpers;

namespace DataSense.ViewModels;

/// <summary>
/// Presentation model for one bar in the Dashboard's Today hourly chart.
/// </summary>
public sealed class HourlyChartBarViewModel
{
    // ── Raw data ─────────────────────────────────────────────────────────────

    public int  Hour            { get; init; } // 0–23
    public long BytesDownloaded { get; init; }
    public long BytesUploaded   { get; init; }
    public long TotalBytes      { get; init; }

    // ── Formatted text ───────────────────────────────────────────────────────

    public string HourLabel      => $"{Hour:00}:00";
    public string DownloadedText { get; init; } = "0 B";
    public string UploadedText   { get; init; } = "0 B";
    public string TotalText      { get; init; } = "0 B";

    public string Tooltip => $"{HourLabel}\n⬇ {DownloadedText}  ⬆ {UploadedText}\nTotal: {TotalText}";

    // ── Canvas geometry ──────────────────────────────────────────────────────

    public double BarX              { get; init; }
    public double BarWidth          { get; init; }
    public double UploadBarHeight   { get; init; }
    public double DownloadBarHeight { get; init; }
    public double UploadBarY        { get; init; }
    public double DownloadBarY      { get; init; }
    public double LabelY            { get; init; }

    public bool HasData => TotalBytes > 0;
}
