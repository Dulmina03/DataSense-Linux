using System;

namespace DataSense.Models;

/// <summary>Budget consumption status tiers.</summary>
public enum BudgetStatus
{
    Healthy,
    Warning,
    Critical,
    Exceeded
}

/// <summary>
/// Computed result of comparing current usage against a <see cref="DataBudget"/>.
/// </summary>
public class BudgetResult
{
    /// <summary>The current consumption status tier.</summary>
    public BudgetStatus Status { get; init; }

    /// <summary>Actual bytes used this month.</summary>
    public long UsedBytes { get; init; }

    /// <summary>Configured monthly limit in bytes.</summary>
    public long LimitBytes { get; init; }

    /// <summary>Percentage of limit consumed (can exceed 100).</summary>
    public double UsedPercent { get; init; }

    /// <summary>Bytes remaining before the limit is hit. Negative when over budget.</summary>
    public long RemainingBytes { get; init; }

    /// <summary>Estimated date the monthly budget will be exhausted. Null if within allowance.</summary>
    public DateTime? EstimatedExhaustionDate { get; init; }

    /// <summary>Current average daily usage in bytes over the recent observed window.</summary>
    public long CurrentDailyPaceBytes { get; init; }

    /// <summary>
    /// The maximum daily average required to stay within the budget by month end.
    /// Null when there are no remaining days or no budget.
    /// </summary>
    public long? RequiredDailyPaceBytes { get; init; }

    // ── Daily budget (optional) ──────────────────────────────────────────────

    /// <summary>True when a daily limit is configured.</summary>
    public bool HasDailyBudget { get; init; }

    /// <summary>Today's usage in bytes.</summary>
    public long TodayUsedBytes { get; init; }

    /// <summary>Daily limit in bytes.</summary>
    public long DailyLimitBytes { get; init; }

    /// <summary>Percentage of daily limit used today.</summary>
    public double TodayUsedPercent { get; init; }

    // ── Presentation helpers ─────────────────────────────────────────────────

    public string StatusLabel => Status switch
    {
        BudgetStatus.Healthy  => "✅ Healthy",
        BudgetStatus.Warning  => "⚠️ Warning",
        BudgetStatus.Critical => "🔴 Critical",
        BudgetStatus.Exceeded => "❌ Over Budget",
        _                     => "—"
    };

    public string StatusColor => Status switch
    {
        BudgetStatus.Healthy  => "#00E676",
        BudgetStatus.Warning  => "#FF9800",
        BudgetStatus.Critical => "#FF5252",
        BudgetStatus.Exceeded => "#FF1744",
        _                     => "#888899"
    };

    /// <summary>Progress bar value clamped to [0, 100].</summary>
    public double ProgressValue => Math.Clamp(UsedPercent, 0, 100);
}
