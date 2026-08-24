using DataSense.Helpers;

namespace DataSense.ViewModels;

/// <summary>
/// Presentation model for one bar in the Dashboard's 14-day stacked bar chart.
/// All canvas geometry (position, heights) is pre-computed by DashboardViewModel
/// so the AXAML requires zero code-behind math.
/// </summary>
public sealed class DailyChartBarViewModel
{
    // ── Raw data ─────────────────────────────────────────────────────────────

    public string DayLabel       { get; init; } = string.Empty;  // e.g. "Aug 1"
    public long   BytesDownloaded { get; init; }
    public long   BytesUploaded   { get; init; }
    public long   TotalBytes      { get; init; }

    // ── Formatted text (used in tooltips and labels) ──────────────────────

    public string DownloadedText { get; init; } = "0 B";
    public string UploadedText   { get; init; } = "0 B";
    public string TotalText      { get; init; } = "0 B";

    /// <summary>Full tooltip shown on hover.</summary>
    public string Tooltip => $"{DayLabel}\n⬇ {DownloadedText}  ⬆ {UploadedText}\nTotal: {TotalText}";

    // ── Canvas geometry (set once by DashboardViewModel.BuildChartItems) ──

    /// <summary>Left edge of this bar column on the canvas.</summary>
    public double BarX     { get; init; }

    /// <summary>Pixel width of each bar column (same for all bars).</summary>
    public double BarWidth { get; init; }

    /// <summary>Pixel height of the upload (top) segment.</summary>
    public double UploadBarHeight   { get; init; }

    /// <summary>Pixel height of the download (bottom) segment.</summary>
    public double DownloadBarHeight { get; init; }

    /// <summary>Top Y of the upload segment (Canvas.Top). Upload stacks above download.</summary>
    public double UploadBarY   { get; init; }

    /// <summary>Top Y of the download segment (Canvas.Top).</summary>
    public double DownloadBarY { get; init; }

    /// <summary>Center X coordinate of this bar column for trend lines and hover guides.</summary>
    public double CenterX => BarX + BarWidth / 2.0;

    /// <summary>Top Y coordinate of the stacked bar for trend lines.</summary>
    public double TopY => TotalBytes > 0 ? UploadBarY : DownloadBarY;

    /// <summary>Combined pixel height of the stacked download and upload segments.</summary>
    public double TotalBarHeight => DownloadBarHeight + UploadBarHeight;

    /// <summary>Y offset for the day label below the chart area.</summary>
    public double LabelY { get; init; }

    /// <summary>True when this bar has any usage to render.</summary>
    public bool HasData => TotalBytes > 0;

    /// <summary>True if this is the latest/active interval bar.</summary>
    public bool IsLatest { get; init; }
}
