using System;

namespace DataSense.Models;

/// <summary>
/// Represents a computed usage forecast for a given period.
/// Only valid when <see cref="HasSufficientData"/> is true.
/// </summary>
public class UsageForecast
{
    /// <summary>When this forecast was computed.</summary>
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// True when there are enough historical observations to generate a reliable forecast.
    /// When false all projection properties should be ignored.
    /// </summary>
    public bool HasSufficientData { get; init; }

    /// <summary>Number of historical days used in the calculation.</summary>
    public int DaysObserved { get; init; }

    /// <summary>Current month's actual usage in bytes (all completed days + today partial).</summary>
    public long CurrentUsageBytes { get; init; }

    /// <summary>Exponential-weighted average daily usage in bytes.</summary>
    public long AverageDailyUsageBytes { get; init; }

    /// <summary>Projected total bytes by end of the current month.</summary>
    public long ProjectedMonthEndBytes { get; init; }

    /// <summary>Lower bound of the projected range (1.5σ below).</summary>
    public long LowerBoundBytes { get; init; }

    /// <summary>Upper bound of the projected range (1.5σ above).</summary>
    public long UpperBoundBytes { get; init; }

    /// <summary>
    /// If a budget is configured, how many bytes remain.
    /// Negative means over-budget.
    /// </summary>
    public long RemainingAllowanceBytes { get; init; }

    /// <summary>
    /// Estimated date the monthly budget will be exhausted.
    /// Null if no budget is set or budget will not be reached.
    /// </summary>
    public DateTime? EstimatedLimitDate { get; init; }

    /// <summary>How confident the forecast is based on data quantity and consistency.</summary>
    public ForecastConfidence Confidence { get; init; }

    /// <summary>How many days remain in the current calendar month.</summary>
    public int RemainingDaysInMonth { get; init; }
}

public enum ForecastConfidence
{
    Low,
    Medium,
    High
}
