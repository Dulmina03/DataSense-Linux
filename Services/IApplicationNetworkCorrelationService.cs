using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

public interface IApplicationNetworkCorrelationService
{
    Task InvalidateCacheAsync();
    
    // Core rankings & aggregates
    Task<IEnumerable<ApplicationNetworkProfile>> GetApplicationNetworkProfilesAsync(bool forceRefresh = false);
    Task<IEnumerable<ApplicationNetworkProfile>> GetTopApplicationsForNetworkAsync(string networkName, string sortBy = "Total", int limit = 10);
    Task<IEnumerable<ApplicationNetworkProfile>> GetNetworkUsageForApplicationAsync(string processName);
    
    // Network Breakdown
    Task<NetworkApplicationBreakdown> GetNetworkApplicationBreakdownAsync(string networkName, AnalyticsPeriod period);
    
    // Anomaly Detection
    Task<IEnumerable<ProcessNetworkAnomaly>> GetNetworkSpecificAnomaliesAsync();
    
    // Insights
    Task<IEnumerable<string>> GetNetworkSpecificInsightsAsync(string networkName);
    
    // Budget & Cost correlation
    Task<BudgetCorrelationInfo> GetBudgetCorrelationAsync();
    
    // Hotspot Intelligence
    Task<HotspotIntelligenceInfo> GetHotspotIntelligenceAsync(string networkName);
    
    // Performance
    Task<IEnumerable<NetworkPerformanceCorrelation>> GetPerformanceCorrelationAsync(string networkName);
    
    // Diagnostics
    Task<CorrelationDiagnosticsInfo> GetDiagnosticsAsync();
}
