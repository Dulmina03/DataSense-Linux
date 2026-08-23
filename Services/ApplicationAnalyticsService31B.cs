using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

// ─────────────────────────────────────────────────────────────────────────────
// Phase 11.31B — Trend comparison, surge detection, health-registry integration.
// All calculations are deterministic and derived from real ProcessUsageRecords.
// ─────────────────────────────────────────────────────────────────────────────

public partial class ApplicationAnalyticsService
{
    private const string HealthSubsystem = "ApplicationAnalyticsService";
    private static readonly TimeSpan SurgeCacheWindow = TimeSpan.FromMinutes(3);

    // ── Standalone trend comparison ───────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<ApplicationTrendComparison> GetApplicationTrendAsync(
        string processName, int pid, long startTimeTicks)
    {
        var utcNow    = DateTime.UtcNow;
        var todayStart = utcNow.Date;
        var w7Start    = todayStart.AddDays(-6);
        var todayEnd   = todayStart.AddDays(1).AddTicks(-1);
        var prev7Start = todayStart.AddDays(-13);
        var prev7End   = todayStart.AddDays(-7).AddTicks(-1);

        var result = new ApplicationTrendComparison
        {
            ProcessName     = processName,
            Pid             = pid,
            StartTimeTicks  = startTimeTicks
        };

        try
        {
            var recentData = (await _repository.GetProcessUsageIdentitiesAsync(w7Start, todayEnd))
                .FirstOrDefault(x => x.ProcessName == processName
                                  && x.Pid          == pid
                                  && x.StartTimeTicks == startTimeTicks);

            var prevData = (await _repository.GetProcessUsageIdentitiesAsync(prev7Start, prev7End))
                .FirstOrDefault(x => x.ProcessName == processName
                                  && x.Pid          == pid
                                  && x.StartTimeTicks == startTimeTicks);

            long recent = recentData != null ? recentData.BytesDownloaded + recentData.BytesUploaded : 0;
            long prev   = prevData   != null ? prevData.BytesDownloaded   + prevData.BytesUploaded   : 0;

            result.RecentPeriodBytes   = recent;
            result.PreviousPeriodBytes = prev;

            if (prev > 0)
            {
                double pct        = (double)(recent - prev) / prev * 100.0;
                result.PercentageChange   = pct;
                result.HasSufficientData  = true;
                result.TrendDirection     = ClassifyTrend(pct);
                result.TrendState         = TrendLabel(result.TrendDirection);
            }
            else
            {
                // previous period has no data — cannot compute a meaningful trend
                result.PercentageChange  = null;
                result.HasSufficientData = false;
                result.TrendDirection    = AppTrendDirection.InsufficientData;
                result.TrendState        = "Insufficient Data";
            }

            ReportHealthSafe(SubsystemState.Healthy, "Trend calculated successfully.");
        }
        catch (Exception ex)
        {
            ReportHealthSafe(SubsystemState.Error, $"Trend calculation failed: {ex.Message}", ex);
        }

        return result;
    }

    // ── Surge detection — enriches each profile produced by GetApplicationProfilesAsync ──

    /// <summary>
    /// Applies surge detection and health-registry state to a list of already-built profiles.
    /// Called internally after the profile list is assembled; modifies items in place.
    /// Surge rule: recent7DayAvgPerDay > previous7DayAvgPerDay × 1.5,
    ///             only evaluated when HasSufficientData && previous period has data.
    /// </summary>
    internal static void EnrichWithSurgeDetection(
        IList<ApplicationHistoricalProfile> profiles,
        IEnumerable<ProcessUsageRecord> prev7Data,
        SubsystemState healthState)
    {
        // Build a lookup of previous-period totals keyed by composite identity
        var prev7Map = prev7Data.ToDictionary(
            r => $"{r.ProcessName}|{r.Pid}|{r.StartTimeTicks}",
            r => r.BytesDownloaded + r.BytesUploaded);

        foreach (var p in profiles)
        {
            p.AnalyticsHealth = healthState;

            // Can only assess surge when there is enough historical data
            if (!p.HasSufficientData) continue;

            string key = $"{p.ProcessName}|{p.Pid}|{p.StartTimeTicks}";
            if (!prev7Map.TryGetValue(key, out long prev7Total) || prev7Total <= 0)
            {
                // No previous data — cannot determine surge
                p.SurgePercentage = null;
                p.IsUsageSurging  = false;
                continue;
            }

            // Compare averages over their respective 7-day windows
            // recent: SevenDayTotalBytes spans 7 days; prev: prev7Total spans 7 days
            double recentAvg = p.SevenDayTotalBytes / 7.0;
            double prevAvg   = prev7Total           / 7.0;

            double surgePct        = (recentAvg - prevAvg) / prevAvg * 100.0;
            p.SurgePercentage      = surgePct;
            p.IsUsageSurging       = recentAvg > prevAvg * 1.5;
        }
    }

    // ── Health-registry helpers ───────────────────────────────────────────────

    private ISystemHealthRegistry? _healthRegistry;

    /// <summary>
    /// Injects an optional health registry. Called after construction via the
    /// extension method below. Not required — the service works without it.
    /// </summary>
    internal void SetHealthRegistry(ISystemHealthRegistry registry)
    {
        _healthRegistry = registry;
        _healthRegistry.RegisterSubsystem(HealthSubsystem);
        _healthRegistry.ReportHealth(HealthSubsystem, SubsystemState.Healthy, "Initialised");
    }

    private void ReportHealthSafe(SubsystemState state, string message, Exception? ex = null)
    {
        try { _healthRegistry?.ReportHealth(HealthSubsystem, state, message, ex); }
        catch { /* Never let health reporting crash analytics */ }
    }

    // ── Static classification helpers (reused across all trend paths) ─────────

    internal static AppTrendDirection ClassifyTrend(double pct)
    {
        if (pct > 10.0)  return AppTrendDirection.Increasing;
        if (pct < -10.0) return AppTrendDirection.Decreasing;
        return AppTrendDirection.Stable;
    }

    internal static string TrendLabel(AppTrendDirection dir) => dir switch
    {
        AppTrendDirection.Increasing      => "Increasing",
        AppTrendDirection.Decreasing      => "Decreasing",
        AppTrendDirection.Stable          => "Stable",
        AppTrendDirection.InsufficientData => "Insufficient Data",
        _                                 => "Insufficient Data"
    };
}

/// <summary>
/// Extension method to attach an ISystemHealthRegistry to an existing
/// ApplicationAnalyticsService singleton after DI construction.
/// </summary>
public static class ApplicationAnalyticsServiceHealthExtension
{
    public static ApplicationAnalyticsService WithHealthRegistry(
        this ApplicationAnalyticsService service,
        ISystemHealthRegistry registry)
    {
        service.SetHealthRegistry(registry);
        return service;
    }
}
