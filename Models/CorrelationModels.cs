using System;
using System.Collections.Generic;

namespace DataSense.Models;

public class NetworkApplicationBreakdown
{
    public string TopApplication { get; set; } = "—";
    public long TopApplicationBytes { get; set; }
    public string DownloadHeavyApplication { get; set; } = "—";
    public long DownloadHeavyBytes { get; set; }
    public string UploadHeavyApplication { get; set; } = "—";
    public long UploadHeavyBytes { get; set; }
    public long TotalAttributedTraffic { get; set; }
    public double AttributionPercentage { get; set; }
    public List<ApplicationNetworkProfile> Profiles { get; set; } = new();

    public bool HasProfiles => Profiles != null && Profiles.Count > 0;
}

public class BudgetCorrelationInfo
{
    public double TopApplicationBudgetShare { get; set; }
    public string ProjectedApplicationContribution { get; set; } = "—";
    public List<string> OveruseDrivers { get; set; } = new();

    public bool HasOverageRisk => TopApplicationBudgetShare > 0 || OveruseDrivers.Count > 0;
}

public class HotspotIntelligenceInfo
{
    public bool IsHotspot { get; set; }
    public List<string> TopHotspotConsumers { get; set; } = new();
    public List<string> UploadHeavyApplications { get; set; } = new();
    public double ConcentrationPercentage { get; set; }
}

public class NetworkPerformanceCorrelation
{
    public string NetworkName { get; set; } = string.Empty;
    public double AvgDownloadSpeed { get; set; }
    public double AvgUploadSpeed { get; set; }
    public double Latency { get; set; }
    public long ApplicationTrafficVolume { get; set; }
}

public class CorrelationDiagnosticsInfo
{
    public int ApplicationsAttributedCount { get; set; }
    public int NetworksWithAttributionCount { get; set; }
    public DateTime? LatestCorrelatedRecordTimestamp { get; set; }
    public string QueryHealth { get; set; } = "Unknown";
    public int DatabaseQueryFailures { get; set; }
}
