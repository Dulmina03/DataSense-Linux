using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

public interface INetworkIntelligenceService
{
    Task<IReadOnlyList<NetworkProfile>> GetNetworkProfilesAsync();
    Task<IReadOnlyList<NetworkPerformanceProfile>> GetNetworkPerformanceProfilesAsync();
    Task<NetworkProfile?> GetCurrentNetworkAsync();
}
