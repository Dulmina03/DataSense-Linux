using Avalonia.Media;

namespace DataSense.Services;

/// <summary>
/// Provides deterministic and session-stable color mapping for applications and process segments
/// in chart visualizations, ensuring consistent colors across refreshes and rank order changes.
/// </summary>
public interface IApplicationChartColorProvider
{
    /// <summary>
    /// Returns the session-stable Avalonia brush assigned to the specified process/application.
    /// </summary>
    IBrush GetColorBrush(string? processIdentifier);

    /// <summary>
    /// Returns the palette brush directly by index (0..11).
    /// </summary>
    IBrush GetColorBrushByIndex(int index);

    /// <summary>
    /// Returns the deterministic 0-based palette index (0..11) assigned to the process.
    /// </summary>
    int GetColorIndex(string? processIdentifier);

    /// <summary>
    /// Returns the hex color string for the application.
    /// </summary>
    string GetColorHex(string? processIdentifier);

    /// <summary>
    /// Returns the hex color string directly by index (0..11).
    /// </summary>
    string GetColorHexByIndex(int index);

    /// <summary>
    /// Returns the semantic theme resource key (e.g. "Brush.ChartSegment1").
    /// </summary>
    string GetColorToken(string? processIdentifier);

    /// <summary>
    /// Returns the semantic theme resource key directly by index (0..11).
    /// </summary>
    string GetColorTokenByIndex(int index);
}
