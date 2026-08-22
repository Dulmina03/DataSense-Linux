using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

public interface IProcessNetworkIntelligenceService
{
    Task InvalidateCacheAsync();
    Task<IEnumerable<ProcessNetworkProfile>> GetProcessNetworkProfilesAsync(bool forceRefresh = false);
    Task<IEnumerable<ProcessNetworkUsageSummary>> GetNetworkProcessUsageAsync(string networkName);
    Task<IEnumerable<ProcessNetworkProfile>> GetProcessNetworkUsageAsync(string processName, int pid, long startTimeTicks);
    Task<IEnumerable<ProcessNetworkUsageSummary>> GetTopProcessesForNetworkAsync(string networkName, int limit = 5);
    Task<IEnumerable<ProcessNetworkProfile>> GetTopNetworksForProcessAsync(string processName, int pid, long startTimeTicks);
    
    Task<IEnumerable<ProcessNetworkInsight>> GetNetworkSpecificBehaviorInsightsAsync();
    Task<IEnumerable<ProcessNetworkAnomaly>> GetProcessNetworkAnomaliesAsync();
    Task<string> GetNetworkSpikeAttributionAsync(DateTime startTime, DateTime endTime);
}
