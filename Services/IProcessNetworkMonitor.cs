using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

/// <summary>
/// Abstraction over a backend that provides per-process network usage data.
/// Implementations may use nethogs, eBPF, or any other mechanism.
/// </summary>
public interface IProcessNetworkMonitor
{
    /// <summary>
    /// Returns true when the required external tool (e.g., nethogs) is present on the system.
    /// </summary>
    Task<bool> IsAvailableAsync();

    /// <summary>
    /// Returns true when the tool has sufficient privileges to capture traffic.
    /// </summary>
    Task<bool> HasPermissionsAsync();

    /// <summary>
    /// Starts monitoring and yields batches of <see cref="ProcessNetworkUsage"/>.
    /// The caller should enumerate the async stream and handle cancellation.
    /// </summary>
    IAsyncEnumerable<IEnumerable<ProcessNetworkUsage>> StartMonitoringAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the path to the executable being used.
    /// </summary>
    string NethogsPath { get; }
}
