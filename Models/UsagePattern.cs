using System;

namespace DataSense.Models;

/// <summary>
/// Represents the statistical usage pattern for a specific time window or entity.
/// Computed locally from historical telemetry.
/// </summary>
public class UsagePattern
{
    public double AverageBytes { get; set; }
    public double MedianBytes { get; set; }
    public double StandardDeviation { get; set; }
    public double MinimumBytes { get; set; }
    public double MaximumBytes { get; set; }
    public int SampleCount { get; set; }
    public bool HasSufficientData => SampleCount >= 3;

    public double NormalRangeLower => Math.Max(0, AverageBytes - 1.5 * StandardDeviation);
    public double NormalRangeUpper => AverageBytes + 1.5 * StandardDeviation;
}
