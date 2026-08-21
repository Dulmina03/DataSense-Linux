using CommunityToolkit.Mvvm.ComponentModel;

namespace DataSense.ViewModels;

/// <summary>
/// Bar/point view-model for one calendar day in the forecast chart.
/// Actual days render a solid cyan bar; forecast days render a dashed indigo bar.
/// </summary>
public partial class ForecastChartPointViewModel : ObservableObject
{
    // ── Day metadata ─────────────────────────────────────────────────────────
    public string  DayLabel  { get; init; } = "";
    public bool    IsForecast { get; init; }
    public bool    IsToday    { get; init; }
    public string  Tooltip    { get; init; } = "";

    // ── Bar geometry (Canvas-based, same pattern as DailyChartBarViewModel) ──
    public double BarX     { get; init; }
    public double BarWidth { get; init; }

    // Actual (solid cyan) bar
    public double ActualBarHeight { get; init; }
    public double ActualBarY      { get; init; }
    public bool   HasActual       => ActualBarHeight > 0;

    // Forecast (translucent indigo) bar
    public double ForecastBarHeight { get; init; }
    public double ForecastBarY      { get; init; }
    public bool   HasForecast       => ForecastBarHeight > 0;

    // Today marker (thin vertical line drawn over the bar)
    public bool HasData => HasActual || HasForecast;
}
