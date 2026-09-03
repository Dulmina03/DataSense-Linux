using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

/// <summary>
/// Canonical engine responsible for taking periodic multi-interface measurements, calculating non-negative
/// reset-safe deltas, and emitting a single unified event stream of canonical snapshots.
/// </summary>
public interface IUsageSnapshotService : IDisposable
{
    /// <summary>
    /// Event emitted exactly once per measurement cycle containing verified snapshots for all active interfaces.
    /// </summary>
    event Action<IReadOnlyList<NetworkUsageSnapshot>>? SnapshotsGenerated;

    /// <summary>
    /// Returns the latest snapshots captured during the most recent measurement cycle.
    /// </summary>
    IReadOnlyList<NetworkUsageSnapshot> GetLatestSnapshots();

    /// <summary>
    /// Returns the aggregated host-level snapshot representing all combined physical/external traffic.
    /// </summary>
    NetworkUsageSnapshot? GetLatestHostSnapshot();

    /// <summary>
    /// Gets the primary active interface name.
    /// </summary>
    string? PrimaryActiveInterface { get; }

    /// <summary>
    /// Indicates whether the background measurement loop is active.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Starts the canonical measurement engine.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops the canonical measurement engine.
    /// </summary>
    void Stop();

    /// <summary>
    /// Manually triggers a single synchronous measurement cycle (primarily for testing and deterministic evaluation).
    /// </summary>
    Task<IReadOnlyList<NetworkUsageSnapshot>> MeasureCycleAsync();
}
