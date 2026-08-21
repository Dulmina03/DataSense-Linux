using System;

namespace DataSense.Models;

/// <summary>
/// A single data point used to render the combined actual + forecast chart.
/// Days in the past have <see cref="IsForecast"/> = false.
/// Days in the future have <see cref="IsForecast"/> = true.
/// </summary>
public class ForecastPoint
{
    /// <summary>Calendar date (UTC) this point represents.</summary>
    public DateTime Date { get; init; }

    /// <summary>
    /// Actual recorded bytes for this day.
    /// Zero for future days.
    /// </summary>
    public long ActualBytes { get; init; }

    /// <summary>
    /// Forecasted bytes for this day.
    /// Zero for past days (actual data takes precedence).
    /// </summary>
    public long ForecastBytes { get; init; }

    /// <summary>True when this point is a future projection, false for historical actual data.</summary>
    public bool IsForecast { get; init; }

    /// <summary>True when this point represents today (partial data + forecast split).</summary>
    public bool IsToday { get; init; }

    /// <summary>Short label for the chart axis (e.g. "Aug 18").</summary>
    public string Label => Date.ToString("MMM d");

    /// <summary>Full tooltip text for this data point.</summary>
    public string Tooltip
    {
        get
        {
            if (IsForecast)
                return $"{Date:MMMM d}\nForecast: {FormatBytes(ForecastBytes)}";
            return ActualBytes > 0
                ? $"{Date:MMMM d}\nActual: {FormatBytes(ActualBytes)}"
                : $"{Date:MMMM d}\nNo data";
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        double v = bytes;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{v:F0} {units[i]}" : $"{v:F1} {units[i]}";
    }
}
