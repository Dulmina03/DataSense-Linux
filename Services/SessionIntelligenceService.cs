using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Database;
using DataSense.Helpers;
using DataSense.Models;

namespace DataSense.Services;

public class SessionIntelligenceService : ISessionIntelligenceService
{
    private readonly INetworkUsageRepository _repository;
    private readonly NetworkSessionManager _sessionManager;
    private readonly IPatternAnalysisService _patternAnalysisService;
    private readonly IEventService? _eventService;

    public SessionIntelligenceService(
        INetworkUsageRepository repository,
        NetworkSessionManager sessionManager,
        IPatternAnalysisService patternAnalysisService,
        IEventService? eventService = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _patternAnalysisService = patternAnalysisService ?? throw new ArgumentNullException(nameof(patternAnalysisService));
        _eventService = eventService;
    }

    public async Task<IEnumerable<NetworkSessionItem>> GetSessionTimelineAsync(
        DateTime start,
        DateTime end,
        string? networkFilter = null,
        string? connectionTypeFilter = null,
        long minBytes = 0,
        TimeSpan? minDuration = null)
    {
        var rawSessions = (await _repository.GetSessionsAsync(start, end, interfaceName: null, networkName: networkFilter)).ToList();

        // Include current active session if within range and not already present
        var activeSession = _sessionManager.CurrentSession;
        if (activeSession != null && activeSession.StartTime <= end && (activeSession.EndTime == null || activeSession.EndTime >= start))
        {
            if (!rawSessions.Any(s => s.Id == activeSession.Id || (s.StartTime == activeSession.StartTime && s.InterfaceName == activeSession.InterfaceName)))
            {
                rawSessions.Add(activeSession);
            }
        }

        var items = new List<NetworkSessionItem>();

        foreach (var session in rawSessions.OrderByDescending(s => s.StartTime))
        {
            // Filter connection type if specified
            if (!string.IsNullOrEmpty(connectionTypeFilter) && !connectionTypeFilter.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                if (!session.ConnectionType.Equals(connectionTypeFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            // Filter min bytes
            if (minBytes > 0 && session.TotalBytes < minBytes)
                continue;

            // Filter min duration
            if (minDuration.HasValue && session.Duration < minDuration.Value)
                continue;

            var item = await BuildSessionItemAsync(session);
            items.Add(item);
        }

        return items;
    }

    private async Task<NetworkSessionItem> BuildSessionItemAsync(NetworkSession session)
    {
        var item = new NetworkSessionItem { Session = session };

        // Determine Status
        var activeSession = _sessionManager.CurrentSession;
        if (!session.EndTime.HasValue || (activeSession != null && activeSession.Id == session.Id && activeSession.Id > 0))
        {
            item.Status = SessionStatusEnum.Active;
        }
        else if (session.Duration.TotalSeconds < 10 && session.TotalBytes > 0)
        {
            item.Status = SessionStatusEnum.Interrupted;
            item.InterruptionReason = "Abrupt disconnect / short session";
        }
        else
        {
            item.Status = SessionStatusEnum.Completed;
        }

        // Speed Telemetry Samples within session window
        var endTime = session.EndTime ?? DateTime.UtcNow;
        var samples = (await _repository.GetHistoryAsync(session.StartTime, endTime, session.InterfaceName)).ToList();

        if (samples.Count >= 2)
        {
            item.AverageDownloadSpeedBps = samples.Average(s => s.DownloadSpeed);
            item.AverageUploadSpeedBps = samples.Average(s => s.UploadSpeed);
            item.PeakDownloadSpeedBps = samples.Max(s => s.DownloadSpeed);
            item.PeakUploadSpeedBps = samples.Max(s => s.UploadSpeed);
        }

        return item;
    }

    public async Task<NetworkSessionItem?> GetSessionDetailsAsync(long sessionId)
    {
        var sessions = await _repository.GetSessionsAsync(DateTime.UtcNow.AddDays(-365), DateTime.UtcNow);
        var match = sessions.FirstOrDefault(s => s.Id == sessionId);
        if (match == null) return null;

        return await BuildSessionItemAsync(match);
    }

    public async Task<IEnumerable<SessionProcessAttribution>> GetSessionProcessAttributionAsync(NetworkSession session)
    {
        var endTime = session.EndTime ?? DateTime.UtcNow;
        var rawProcesses = (await _repository.GetTopProcessesAsync(session.StartTime, endTime, 100)).ToList();

        if (rawProcesses.Count == 0)
            return Enumerable.Empty<SessionProcessAttribution>();

        var grouped = rawProcesses
            .GroupBy(p => p.ProcessName)
            .Select(g => new
            {
                ProcessName = g.Key,
                Pid = g.FirstOrDefault()?.Id ?? 0, // Fallback PID
                ExecutablePath = g.FirstOrDefault()?.ExecutablePath ?? string.Empty,
                UserName = g.FirstOrDefault()?.UserName ?? string.Empty,
                BytesDownloaded = g.Sum(x => x.BytesDownloaded),
                BytesUploaded = g.Sum(x => x.BytesUploaded)
            })
            .ToList();

        long sessionProcessTotal = grouped.Sum(g => g.BytesDownloaded + g.BytesUploaded);
        if (sessionProcessTotal == 0) return Enumerable.Empty<SessionProcessAttribution>();

        return grouped
            .Select(g => new SessionProcessAttribution
            {
                ProcessName = g.ProcessName,
                Pid = (int)g.Pid,
                ExecutablePath = g.ExecutablePath,
                UserName = g.UserName,
                BytesDownloaded = g.BytesDownloaded,
                BytesUploaded = g.BytesUploaded,
                PercentageOfSessionTraffic = Math.Min(100.0, ((double)(g.BytesDownloaded + g.BytesUploaded) / sessionProcessTotal) * 100.0)
            })
            .OrderByDescending(p => p.TotalBytes);
    }

    public async Task<IEnumerable<NetworkUsageRecord>> GetSessionTrafficSamplesAsync(NetworkSession session)
    {
        var endTime = session.EndTime ?? DateTime.UtcNow;
        return await _repository.GetHistoryAsync(session.StartTime, endTime, session.InterfaceName);
    }

    public async Task<SessionComparisonResult> CompareSessionAsync(NetworkSession session)
    {
        var historical = (await _repository.GetSessionsAsync(DateTime.UtcNow.AddDays(-90), DateTime.UtcNow, networkName: session.NetworkName))
            .Where(s => s.Id != session.Id && s.EndTime.HasValue)
            .ToList();

        if (historical.Count < 3)
        {
            return new SessionComparisonResult
            {
                HasSufficientData = false,
                ComparableSessionCount = historical.Count,
                StatusMessage = "Insufficient historical sessions for comparison."
            };
        }

        double avgBytes = historical.Average(s => (double)s.TotalBytes);
        double avgDurationSec = historical.Average(s => s.Duration.TotalSeconds);

        double usageDiff = avgBytes > 0 ? ((session.TotalBytes - avgBytes) / avgBytes) * 100.0 : 0;
        double durationDiff = avgDurationSec > 0 ? ((session.Duration.TotalSeconds - avgDurationSec) / avgDurationSec) * 100.0 : 0;

        var statements = new List<string>();

        if (Math.Abs(usageDiff) >= 5.0)
        {
            string direction = usageDiff > 0 ? "more" : "less";
            statements.Add($"This session used {Math.Abs(usageDiff):F0}% {direction} data than the average {session.NetworkName} session.");
        }
        else
        {
            statements.Add($"Session data usage is consistent with historical {session.NetworkName} average.");
        }

        if (Math.Abs(durationDiff) >= 10.0)
        {
            string dir = durationDiff > 0 ? "longer" : "shorter";
            statements.Add($"Session duration is {Math.Abs(durationDiff):F0}% {dir} than previous sessions.");
        }

        return new SessionComparisonResult
        {
            HasSufficientData = true,
            ComparableSessionCount = historical.Count,
            HistoricalAverageBytes = (long)avgBytes,
            HistoricalAverageDuration = TimeSpan.FromSeconds(avgDurationSec),
            UsageDifferencePercentage = usageDiff,
            DurationDifferencePercentage = durationDiff,
            ComparisonStatements = statements,
            StatusMessage = $"Compared against {historical.Count} historical sessions on {session.NetworkName}."
        };
    }

    public async Task<NetworkSessionPattern?> GetNetworkPatternAsync(string networkName)
    {
        var historical = (await _repository.GetSessionsAsync(DateTime.UtcNow.AddDays(-90), DateTime.UtcNow, networkName: networkName))
            .Where(s => s.EndTime.HasValue)
            .ToList();

        if (historical.Count == 0) return null;

        double avgDurationSec = historical.Average(s => s.Duration.TotalSeconds);
        double avgBytes = historical.Average(s => (double)s.TotalBytes);

        var startTimes = historical.Select(s => s.StartTime.TimeOfDay.TotalMinutes).OrderBy(t => t).ToList();
        double medianStartMinutes = startTimes[startTimes.Count / 2];
        TimeSpan medianStartTime = TimeSpan.FromMinutes(medianStartMinutes);

        var endTimes = historical.Select(s => s.EndTime!.Value.TimeOfDay.TotalMinutes).OrderBy(t => t).ToList();
        double medianEndMinutes = endTimes[endTimes.Count / 2];
        TimeSpan medianEndTime = TimeSpan.FromMinutes(medianEndMinutes);

        return new NetworkSessionPattern
        {
            NetworkName = networkName,
            TypicalDuration = TimeSpan.FromSeconds(avgDurationSec),
            AverageUsageBytes = (long)avgBytes,
            TypicalStartTimeOfDay = $"{medianStartTime.Hours:D2}:{medianStartTime.Minutes:D2}",
            TypicalEndTimeOfDay = $"{medianEndTime.Hours:D2}:{medianEndTime.Minutes:D2}",
            SessionCount = historical.Count
        };
    }

    public async Task<IEnumerable<NetworkSwitchItem>> GetNetworkSwitchTimelineAsync(DateTime start, DateTime end)
    {
        var sessions = (await _repository.GetSessionsAsync(start, end)).OrderBy(s => s.StartTime).ToList();
        var switches = new List<NetworkSwitchItem>();

        for (int i = 1; i < sessions.Count; i++)
        {
            var prev = sessions[i - 1];
            var curr = sessions[i];

            if (!prev.NetworkName.Equals(curr.NetworkName, StringComparison.OrdinalIgnoreCase) ||
                !prev.InterfaceName.Equals(curr.InterfaceName, StringComparison.OrdinalIgnoreCase))
            {
                var switchItem = new NetworkSwitchItem
                {
                    Timestamp = curr.StartTime,
                    OldNetwork = !string.IsNullOrEmpty(prev.NetworkName) ? prev.NetworkName : prev.InterfaceName,
                    NewNetwork = !string.IsNullOrEmpty(curr.NetworkName) ? curr.NetworkName : curr.InterfaceName,
                    ConnectionType = curr.ConnectionType
                };

                // Read traffic immediately before switch
                var beforeSamples = (await _repository.GetHistoryAsync(curr.StartTime.AddSeconds(-30), curr.StartTime, prev.InterfaceName)).ToList();
                if (beforeSamples.Count > 0)
                {
                    switchItem.TrafficBeforeDownloadBps = beforeSamples.Average(s => s.DownloadSpeed);
                    switchItem.TrafficBeforeUploadBps = beforeSamples.Average(s => s.UploadSpeed);
                }

                // Read traffic immediately after switch
                var afterSamples = (await _repository.GetHistoryAsync(curr.StartTime, curr.StartTime.AddSeconds(30), curr.InterfaceName)).ToList();
                if (afterSamples.Count > 0)
                {
                    switchItem.TrafficAfterDownloadBps = afterSamples.Average(s => s.DownloadSpeed);
                    switchItem.TrafficAfterUploadBps = afterSamples.Average(s => s.UploadSpeed);
                }

                switches.Add(switchItem);
            }
        }

        return switches;
    }

    public async Task<IEnumerable<SessionIntelligenceInsight>> GenerateSessionInsightsAsync(NetworkSession session)
    {
        var insights = new List<SessionIntelligenceInsight>();

        // 1. Session share of today's total traffic
        var (todayDown, todayUp) = await _repository.GetTodaySummaryAsync();
        long todayTotal = todayDown + todayUp;
        if (todayTotal > 0 && session.TotalBytes > 0)
        {
            double share = ((double)session.TotalBytes / todayTotal) * 100.0;
            if (share >= 25.0)
            {
                insights.Add(new SessionIntelligenceInsight
                {
                    Title = "High Traffic Contribution",
                    Description = $"This {session.ConnectionType} session consumed {share:F0}% of your total daily traffic.",
                    Severity = share >= 60.0 ? "Warning" : "Info"
                });
            }
        }

        // 2. Top process contribution during session
        var appBreakdown = (await GetSessionProcessAttributionAsync(session)).ToList();
        if (appBreakdown.Count > 0)
        {
            var topApp = appBreakdown[0];
            insights.Add(new SessionIntelligenceInsight
            {
                Title = "Top Process Traffic",
                Description = $"{topApp.ProcessName} generated the highest traffic ({topApp.FormattedTotal}) during this session.",
                Severity = "Info"
            });
        }

        // 3. Session Duration Comparison
        var historical = (await _repository.GetSessionsAsync(DateTime.UtcNow.AddDays(-60), DateTime.UtcNow, networkName: session.NetworkName))
            .Where(s => s.Id != session.Id && s.EndTime.HasValue)
            .ToList();

        if (historical.Count >= 3)
        {
            double avgDurationSec = historical.Average(s => s.Duration.TotalSeconds);
            if (avgDurationSec > 0 && session.Duration.TotalSeconds >= 2.0 * avgDurationSec)
            {
                double multiple = session.Duration.TotalSeconds / avgDurationSec;
                insights.Add(new SessionIntelligenceInsight
                {
                    Title = "Extended Duration",
                    Description = $"This session is {multiple:F1}× longer than your normal {session.NetworkName} session.",
                    Severity = "Info"
                });
            }
        }

        // 4. Upload vs Download ratio check
        if (session.BytesUploaded > session.BytesDownloaded && session.BytesUploaded > 50 * 1024 * 1024)
        {
            insights.Add(new SessionIntelligenceInsight
            {
                Title = "High Upload Usage",
                Description = $"Upload usage ({ByteFormatter.FormatBytes(session.BytesUploaded)}) was unusually high compared with download usage during this session.",
                Severity = "Warning"
            });
        }

        return insights;
    }

    public async Task CheckAndPublishSessionEventsAsync(NetworkSession session)
    {
        if (_eventService == null) return;

        // Long Session Event
        if (session.Duration >= TimeSpan.FromHours(2))
        {
            _eventService.PublishEvent(new DataSenseEvent
            {
                EventType = DataSenseEventType.LongSessionDetected,
                Severity = EventSeverity.Info,
                Title = "Long Network Session",
                Description = $"Session on {session.NetworkName} has been active for {session.Duration.Hours} hours.",
                Source = "Session Intelligence",
                Fingerprint = $"LongSession_{session.Id}_{session.StartTime:yyyyMMdd_HH}"
            });
        }

        // High Usage Session Event
        if (session.TotalBytes >= 1024L * 1024 * 1024) // 1 GB
        {
            _eventService.PublishEvent(new DataSenseEvent
            {
                EventType = DataSenseEventType.HighUsageSession,
                Severity = EventSeverity.Warning,
                Title = "High Usage Session",
                Description = $"Session on {session.NetworkName} reached {ByteFormatter.FormatBytes(session.TotalBytes)}.",
                Source = "Session Intelligence",
                Fingerprint = $"HighUsageSession_{session.Id}"
            });
        }

        // Unusual Upload Event
        if (session.BytesUploaded > session.BytesDownloaded && session.BytesUploaded >= 500 * 1024 * 1024)
        {
            _eventService.PublishEvent(new DataSenseEvent
            {
                EventType = DataSenseEventType.UnusualUploadSession,
                Severity = EventSeverity.Warning,
                Title = "Unusual Upload Session",
                Description = $"Session uploaded {ByteFormatter.FormatBytes(session.BytesUploaded)}, exceeding download volume.",
                Source = "Session Intelligence",
                Fingerprint = $"UnusualUpload_{session.Id}"
            });
        }

        await Task.CompletedTask;
    }
}
