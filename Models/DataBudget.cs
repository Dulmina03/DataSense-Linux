namespace DataSense.Models;

/// <summary>
/// Configurable data budget settings for the user.
/// Persisted in SQLite AppSettings as JSON.
/// </summary>
public class DataBudget
{
    /// <summary>Whether budget tracking is active.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Monthly data allowance in bytes. 0 = unlimited/not set.</summary>
    public long MonthlyLimitBytes { get; set; } = 0;

    /// <summary>Optional daily data allowance in bytes. 0 = not set.</summary>
    public long DailyLimitBytes { get; set; } = 0;

    /// <summary>Percentage of monthly allowance at which a Warning status is shown (default 75).</summary>
    public int WarningThresholdPct { get; set; } = 75;

    /// <summary>Percentage of monthly allowance at which a Critical status is shown (default 90).</summary>
    public int CriticalThresholdPct { get; set; } = 90;

    // ── Convenience factories ────────────────────────────────────────────────

    /// <summary>Returns a default (disabled) budget.</summary>
    public static DataBudget Default() => new();

    /// <summary>Validates and clamps thresholds to sensible ranges.</summary>
    public void Validate()
    {
        if (WarningThresholdPct < 1)  WarningThresholdPct  = 75;
        if (CriticalThresholdPct < 1) CriticalThresholdPct = 90;
        if (WarningThresholdPct  > 99) WarningThresholdPct  = 99;
        if (CriticalThresholdPct > 99) CriticalThresholdPct = 99;
        if (WarningThresholdPct >= CriticalThresholdPct)
            WarningThresholdPct = CriticalThresholdPct - 5;
        if (MonthlyLimitBytes < 0) MonthlyLimitBytes = 0;
        if (DailyLimitBytes   < 0) DailyLimitBytes   = 0;
    }
}
