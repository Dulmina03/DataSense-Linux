using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Models;
using DataSense.Services;

namespace DataSense.Services;

public class NetworkIntelligenceService : INetworkIntelligenceService
{
    private readonly IAnalyticsService _analyticsService;
    private readonly INetworkConnectionService _connectionService;

    public NetworkIntelligenceService(
        IAnalyticsService analyticsService,
        INetworkConnectionService connectionService)
    {
        _analyticsService  = analyticsService  ?? throw new ArgumentNullException(nameof(analyticsService));
        _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
    }

    // ── Network Profiles ─────────────────────────────────────────────────────
    /// <summary>
    /// Aggregates one <see cref="NetworkProfile"/> per known network using
    /// real All-Time analytics summaries from SQLite.
    /// </summary>
    public async Task<IReadOnlyList<NetworkProfile>> GetNetworkProfilesAsync()
    {
        var networks = (await _analyticsService.GetAvailableNetworksAsync()).ToList();
        var profiles = new List<NetworkProfile>();

        foreach (var name in networks)
        {
            try
            {
                var summary = await _analyticsService.GetNetworkSummaryAsync(name, AnalyticsPeriod.AllTime);

                // NetworkAnalyticsSummary does not carry ConnectionType / InterfaceName.
                // We leave those as "Unknown" – they will be populated for the *current*
                // network via GetCurrentNetworkAsync() which uses INetworkConnectionService.
                var profile = new NetworkProfile
                {
                    NetworkName             = name,
                    ConnectionType          = "Unknown",
                    InterfaceName           = "Unknown",
                    FirstSeenAt             = summary.FirstConnected ?? DateTime.MinValue,
                    LastSeenAt              = summary.LastConnected  ?? DateTime.MinValue,
                    TotalSessions           = summary.TotalSessions,
                    TotalConnectionDuration = summary.TotalConnectionTime,
                    TotalDownloadBytes      = summary.TotalDownloaded,
                    TotalUploadBytes        = summary.TotalUploaded,
                    IsCurrentlyConnected    = false   // enriched below for the active network
                };
                profiles.Add(profile);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[NetworkIntelligenceService] Failed to build profile for '{name}': {ex.Message}");
            }
        }

        // Mark the active network
        try
        {
            var current = await GetCurrentNetworkAsync();
            if (current != null)
            {
                var match = profiles.FirstOrDefault(p =>
                    string.Equals(p.NetworkName, current.NetworkName, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    match.IsCurrentlyConnected = true;
            }
        }
        catch { /* ignore – current-network detection is best-effort */ }

        return profiles;
    }

    // ── Performance Profiles ─────────────────────────────────────────────────
    /// <summary>
    /// Builds a <see cref="NetworkPerformanceProfile"/> per network using
    /// real speed-test data from SQLite via <see cref="IAnalyticsService"/>.
    ///
    /// Reliability  = SuccessfulTests / TotalTests × 100
    ///              (NetworkPerformanceSummary.TotalTests counts only completed tests;
    ///               failed tests are those where AvgDownloadMbps == 0.)
    ///
    /// Stability is approximated from the same ratio when no dedicated
    /// packet-loss telemetry exists.
    /// </summary>
    public async Task<IReadOnlyList<NetworkPerformanceProfile>> GetNetworkPerformanceProfilesAsync()
    {
        var networks     = (await _analyticsService.GetAvailableNetworksAsync()).ToList();
        var perfProfiles = new List<NetworkPerformanceProfile>();

        foreach (var name in networks)
        {
            try
            {
                var perf = await _analyticsService.GetNetworkPerformanceAsync(name);
                if (perf == null || perf.TotalTests == 0) continue;

                // NetworkPerformanceSummary has no SuccessfulTests field.
                // We treat TotalTests as successful tests (analytics only records completed tests).
                int totalTests      = perf.TotalTests;
                int successfulTests = perf.TotalTests; // all recorded tests completed successfully

                double reliability = totalTests > 0
                    ? (double)successfulTests / totalTests * 100.0
                    : 0.0;

                var profile = new NetworkPerformanceProfile
                {
                    NetworkName          = name,
                    AverageDownloadSpeed = perf.AvgDownloadMbps,
                    AverageUploadSpeed   = perf.AvgUploadMbps,
                    AverageLatency       = perf.AvgPingMs,
                    SpeedTestCount       = totalTests,
                    ReliabilityScore     = reliability,
                    StabilityScore       = reliability   // same basis until packet-loss telemetry exists
                };

                perfProfiles.Add(profile);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[NetworkIntelligenceService] Failed to build perf profile for '{name}': {ex.Message}");
            }
        }

        return perfProfiles;
    }

    // ── Current Network ──────────────────────────────────────────────────────
    /// <summary>
    /// Returns a lightweight <see cref="NetworkProfile"/> for the currently
    /// active connection by calling <see cref="INetworkConnectionService"/>.
    /// Returns <c>null</c> when there is no active network.
    /// </summary>
    public async Task<NetworkProfile?> GetCurrentNetworkAsync()
    {
        try
        {
            // INetworkConnectionService.GetConnectionDetailsAsync requires an interface name.
            // We pass an empty string so the implementation falls back to the default route.
            var details = await _connectionService.GetConnectionDetailsAsync(string.Empty);

            if (details == null ||
                string.Equals(details.ConnectionState, "Disconnected", StringComparison.OrdinalIgnoreCase))
                return null;

            // Use WifiSsid when available; otherwise fall back to ConnectionName, then InterfaceName.
            string networkName = !string.IsNullOrWhiteSpace(details.WifiSsid)
                ? details.WifiSsid
                : !string.IsNullOrWhiteSpace(details.ConnectionName)
                    ? details.ConnectionName
                    : details.InterfaceName;

            return new NetworkProfile
            {
                NetworkName             = networkName,
                ConnectionType          = details.ConnectionType,
                InterfaceName           = details.InterfaceName,
                FirstSeenAt             = DateTime.UtcNow,
                LastSeenAt              = DateTime.UtcNow,
                IsCurrentlyConnected    = true,
                TotalSessions           = 0,
                TotalConnectionDuration = TimeSpan.Zero,
                TotalDownloadBytes      = 0,
                TotalUploadBytes        = 0
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[NetworkIntelligenceService] GetCurrentNetworkAsync failed: {ex.Message}");
            return null;
        }
    }
}
