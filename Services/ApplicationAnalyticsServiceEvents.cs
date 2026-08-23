using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 11.31 Part 2 — Event integration, fastest-growing ranking, error guard.
// Builds on 11.31A/B foundations without duplicating existing logic.
// ─────────────────────────────────────────────────────────────────────────────

public partial class ApplicationAnalyticsService
{
    private IEventService? _eventService;

    /// <summary>Injects the optional event service. Called post-construction.</summary>
    public void SetEventService(IEventService eventService)
        => _eventService = eventService;

    // ── Fastest-growing ranking ───────────────────────────────────────────────

    /// <summary>
    /// Returns applications ordered by CombinedTrendPercentage descending
    /// (those growing fastest relative to their prior 7-day baseline).
    /// Only includes applications with HasSufficientData and a valid TrendPercentage.
    /// </summary>
    public async Task<IEnumerable<ApplicationHistoricalProfile>> GetFastestGrowingApplicationsAsync(
        int limit = 10)
    {
        var profiles = await GetApplicationProfilesAsync();
        return profiles
            .Where(p => p.HasSufficientData && p.TrendPercentage.HasValue)
            .OrderByDescending(p => p.TrendPercentage!.Value)
            .ThenBy(p => p.ProcessName)
            .Take(limit);
    }

    // ── Analytics event publishing ────────────────────────────────────────────

    /// <summary>
    /// Evaluates current application profiles for notable conditions and publishes
    /// events to IEventService with fingerprint deduplication (15-min cooldown).
    /// Called after GetApplicationProfilesAsync completes; never throws.
    /// Conditions evaluated:
    ///   - Application becomes dominant consumer (>25% of total traffic)
    ///   - Application usage surges (IsUsageSurging == true)
    ///   - Analytics unavailable (no process telemetry at all)
    /// </summary>
    internal void PublishAnalyticsEvents(IReadOnlyList<ApplicationHistoricalProfile> profiles)
    {
        if (_eventService == null) return;

        try
        {
            if (profiles.Count == 0)
            {
                _eventService.PublishEvent(new DataSenseEvent
                {
                    EventType   = DataSenseEventType.ApplicationAnomaly,
                    Severity    = EventSeverity.Info,
                    Title       = "Application Analytics Unavailable",
                    Description = "No process telemetry has been collected yet. Analytics will appear once monitoring gathers data.",
                    Source      = "ApplicationAnalyticsService",
                    Fingerprint = "app-analytics-unavailable"
                });
                return;
            }

            // Dominant consumer events
            foreach (var p in profiles.Where(p => p.PercentageOfTotal >= 25.0))
            {
                _eventService.PublishEvent(new DataSenseEvent
                {
                    EventType   = DataSenseEventType.ApplicationAnomaly,
                    Severity    = EventSeverity.Warning,
                    Title       = $"{p.ProcessName} is a dominant data consumer",
                    Description = $"{p.ProcessName} accounts for {p.PercentageOfTotal:F1}% of all monitored process traffic.",
                    Source      = "ApplicationAnalyticsService",
                    Fingerprint = $"dominant-consumer-{p.ProcessName}-{p.Pid}",
                    NavigationTarget = "ApplicationAnalytics"
                });
            }

            // Surge events (only when HasSufficientData to avoid false positives)
            foreach (var p in profiles.Where(p => p.IsUsageSurging && p.HasSufficientData))
            {
                _eventService.PublishEvent(new DataSenseEvent
                {
                    EventType   = DataSenseEventType.TrafficSpikeDetected,
                    Severity    = EventSeverity.Warning,
                    Title       = $"{p.ProcessName} usage is surging",
                    Description = $"{p.ProcessName} recent 7-day average is significantly higher than its historical baseline ({p.SurgePercentage:F0}% above previous period).",
                    Source      = "ApplicationAnalyticsService",
                    Fingerprint = $"app-surge-{p.ProcessName}-{p.Pid}",
                    NavigationTarget = "ApplicationAnalytics"
                });
            }
        }
        catch (Exception ex)
        {
            ReportHealthSafe(SubsystemState.Degraded, $"Event publishing failed: {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Extension methods for wiring ApplicationAnalyticsService post-construction
/// optional dependencies (health registry, event service) without changing
/// the primary DI constructor.
/// </summary>
public static class ApplicationAnalyticsServiceWiring
{
    /// <summary>
    /// Attaches health registry and event service to the analytics singleton
    /// after the DI container has been built. Call once from startup.
    /// </summary>
    public static IApplicationAnalyticsService WireOptionalDependencies(
        this IApplicationAnalyticsService service,
        ISystemHealthRegistry? healthRegistry,
        IEventService? eventService)
    {
        if (service is not ApplicationAnalyticsService concrete) return service;
        if (healthRegistry != null) concrete.SetHealthRegistry(healthRegistry);
        if (eventService   != null) concrete.SetEventService(eventService);
        return service;
    }
}
