using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

public interface ISessionIntelligenceService
{
    Task<IEnumerable<NetworkSessionItem>> GetSessionTimelineAsync(
        DateTime start,
        DateTime end,
        string? networkFilter = null,
        string? connectionTypeFilter = null,
        long minBytes = 0,
        TimeSpan? minDuration = null);

    Task<NetworkSessionItem?> GetSessionDetailsAsync(long sessionId);
    Task<IEnumerable<SessionProcessAttribution>> GetSessionProcessAttributionAsync(NetworkSession session);
    Task<IEnumerable<NetworkUsageRecord>> GetSessionTrafficSamplesAsync(NetworkSession session);
    Task<SessionComparisonResult> CompareSessionAsync(NetworkSession session);
    Task<NetworkSessionPattern?> GetNetworkPatternAsync(string networkName);
    Task<IEnumerable<NetworkSwitchItem>> GetNetworkSwitchTimelineAsync(DateTime start, DateTime end);
    Task<IEnumerable<SessionIntelligenceInsight>> GenerateSessionInsightsAsync(NetworkSession session);
    Task CheckAndPublishSessionEventsAsync(NetworkSession session);
}
