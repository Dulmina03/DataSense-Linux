using System.Collections.Generic;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

/// <summary>
/// Service responsible for querying raw Linux OS network interface counters across all active and physical interfaces.
/// </summary>
public interface INetworkUsageCollector
{
    /// <summary>
    /// Collects raw counters for all operational and candidate network interfaces on the host.
    /// Filters out loopback and non-host container virtual pairs according to policy.
    /// </summary>
    Task<IReadOnlyList<InterfaceRawCounters>> CollectAllInterfacesAsync();

    /// <summary>
    /// Collects raw counters for a specific named interface.
    /// </summary>
    Task<InterfaceRawCounters?> CollectInterfaceAsync(string interfaceName);
}
