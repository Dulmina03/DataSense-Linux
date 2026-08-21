namespace DataSense.Models;

/// <summary>
/// A named pattern point representing a clock hour (0-23), day of week, process, or network baseline.
/// </summary>
public class UsagePatternPoint
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public UsagePattern Pattern { get; set; } = new();

    public double NormalRangeLower => Pattern.NormalRangeLower;
    public double NormalRangeUpper => Pattern.NormalRangeUpper;
}
