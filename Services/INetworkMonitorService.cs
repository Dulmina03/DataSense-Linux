using System.Collections.Generic;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

public interface INetworkMonitorService
{
    /// <summary>
    /// Returns a list of active network interface names (excluding loopback).
    /// </summary>
    Task<IEnumerable<string>> GetAvailableInterfacesAsync();

    /// <summary>
    /// Retrieves usage for the specified interface, calculating speeds based on previous measurements.
    /// Returns null if the interface is not found.
    /// </summary>
    Task<NetworkUsage?> GetUsageAsync(string interfaceName);

    /// <summary>
    /// Resets previous measurements for the specified interface or all interfaces to prevent false spikes.
    /// </summary>
    void ResetMeasurement(string? interfaceName = null);
}
