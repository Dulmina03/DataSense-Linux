using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

public interface IApplicationIntelligenceService
{
    /// <summary>
    /// Computes a comprehensive usage profile for a specific process.
    /// </summary>
    Task<ApplicationUsageProfile?> GetApplicationProfileAsync(string processName);

    /// <summary>
    /// Returns application usage profiles for the top data consuming applications in the selected period.
    /// </summary>
    Task<IEnumerable<ApplicationUsageProfile>> GetTopApplicationProfilesAsync(AnalyticsPeriod period, int limit);

    /// <summary>
    /// Generates actionable smart recommendations based on process consumption patterns, trends, and budget impact.
    /// </summary>
    Task<IEnumerable<ApplicationRecommendation>> GenerateApplicationRecommendationsAsync();

    /// <summary>
    /// Generates process-specific recommendations for a single application.
    /// </summary>
    Task<IEnumerable<ApplicationRecommendation>> GetProcessRecommendationsAsync(string processName);

    /// <summary>
    /// Identifies processes contributing significantly to a specific daily/hourly usage spike.
    /// </summary>
    Task<IEnumerable<ApplicationUsageProfile>> GetSpikeContributorsAsync(DateTime date);

    /// <summary>
    /// Computes a comprehensive network profile for a specific application/process.
    /// </summary>
    Task<ApplicationNetworkProfile?> GetApplicationNetworkProfileAsync(string processName, int pid, long startTimeTicks);
}
