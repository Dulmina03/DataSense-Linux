using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Database;
using DataSense.Models;
using DataSense.Helpers;

namespace DataSense.Services;

public class ProcessNetworkIntelligenceService : IProcessNetworkIntelligenceService
{
    private readonly INetworkUsageRepository _repository;
    private readonly ILinuxProcessResolver _processResolver;
    
    private IEnumerable<ProcessNetworkProfile>? _cachedProfiles;
    private DateTime _cacheExpiration = DateTime.MinValue;
    private readonly object _cacheLock = new();

    public ProcessNetworkIntelligenceService(
        INetworkUsageRepository repository,
        ILinuxProcessResolver processResolver)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _processResolver = processResolver ?? throw new ArgumentNullException(nameof(processResolver));
    }

    public async Task InvalidateCacheAsync()
    {
        lock (_cacheLock)
        {
            _cachedProfiles = null;
            _cacheExpiration = DateTime.MinValue;
        }
        await Task.CompletedTask;
    }

    public async Task<IEnumerable<ProcessNetworkProfile>> GetProcessNetworkProfilesAsync(bool forceRefresh = false)
    {
        lock (_cacheLock)
        {
            if (!forceRefresh && _cachedProfiles != null && DateTime.UtcNow < _cacheExpiration)
            {
                return _cachedProfiles;
            }
        }

        var results = new List<ProcessNetworkProfile>();
        string connectionString = (_repository as SqliteNetworkUsageRepository)?.ConnectionString ?? "Data Source=datasense.db";

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT 
                p.ProcessName,
                p.Pid,
                p.StartTimeTicks,
                s.NetworkName,
                s.ConnectionType,
                s.InterfaceName,
                SUM(p.BytesDownloaded) AS DownloadBytes,
                SUM(p.BytesUploaded) AS UploadBytes,
                MIN(p.Timestamp) AS FirstSeen,
                MAX(p.Timestamp) AS LastSeen,
                COUNT(p.Id) AS SampleCount,
                COUNT(DISTINCT date(p.Timestamp)) AS ActiveDays
            FROM ProcessUsageRecords p
            JOIN NetworkSessions s ON p.Timestamp >= s.StartTime AND (s.EndTime IS NULL OR p.Timestamp <= s.EndTime)
            GROUP BY p.ProcessName, p.Pid, p.StartTimeTicks, s.NetworkName, s.ConnectionType, s.InterfaceName;";

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = sql;
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var profile = new ProcessNetworkProfile
                {
                    ProcessName = reader.GetString(0),
                    Pid = reader.GetInt32(1),
                    StartTimeTicks = reader.GetInt64(2),
                    NetworkName = reader.GetString(3),
                    ConnectionType = reader.GetString(4),
                    InterfaceName = reader.GetString(5),
                    DownloadBytes = reader.GetInt64(6),
                    UploadBytes = reader.GetInt64(7),
                    FirstSeen = DateTime.Parse(reader.GetString(8)),
                    LastSeen = DateTime.Parse(reader.GetString(9)),
                    SampleCount = reader.GetInt32(10),
                    ActiveDays = reader.GetInt32(11),
                    HasHistoricalData = true
                };
                results.Add(profile);
            }
        }

        // Calculate network percentage share
        var networkTotals = results.GroupBy(r => r.NetworkName)
                                   .ToDictionary(g => g.Key, g => g.Sum(r => r.TotalBytes));

        foreach (var profile in results)
        {
            long netTotal = networkTotals.TryGetValue(profile.NetworkName, out var total) && total > 0 ? total : 1;
            profile.PercentageOfNetworkUsage = ((double)profile.TotalBytes / netTotal) * 100.0;
        }

        lock (_cacheLock)
        {
            _cachedProfiles = results;
            _cacheExpiration = DateTime.UtcNow.AddMinutes(5);
        }

        return results;
    }

    public async Task<IEnumerable<ProcessNetworkUsageSummary>> GetNetworkProcessUsageAsync(string networkName)
    {
        if (string.IsNullOrEmpty(networkName)) return Enumerable.Empty<ProcessNetworkUsageSummary>();
        
        var profiles = await GetProcessNetworkProfilesAsync();
        var list = profiles.Where(p => p.NetworkName.Equals(networkName, StringComparison.OrdinalIgnoreCase))
                           .OrderByDescending(p => p.TotalBytes)
                           .ToList();

        var summaries = new List<ProcessNetworkUsageSummary>();
        int rank = 1;
        foreach (var p in list)
        {
            summaries.Add(new ProcessNetworkUsageSummary
            {
                ProcessName = p.ProcessName,
                Pid = p.Pid,
                StartTimeTicks = p.StartTimeTicks,
                DownloadBytes = p.DownloadBytes,
                UploadBytes = p.UploadBytes,
                PercentageOfTotal = p.PercentageOfNetworkUsage,
                Rank = rank++
            });
        }
        return summaries;
    }

    public async Task<IEnumerable<ProcessNetworkProfile>> GetProcessNetworkUsageAsync(string processName, int pid, long startTimeTicks)
    {
        if (string.IsNullOrEmpty(processName)) return Enumerable.Empty<ProcessNetworkProfile>();

        var profiles = await GetProcessNetworkProfilesAsync();
        
        if (pid <= 0)
        {
            return profiles.Where(p => p.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));
        }

        return profiles.Where(p => p.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase) && p.Pid == pid && p.StartTimeTicks == startTimeTicks);
    }

    public async Task<IEnumerable<ProcessNetworkUsageSummary>> GetTopProcessesForNetworkAsync(string networkName, int limit = 5)
    {
        var summaries = await GetNetworkProcessUsageAsync(networkName);
        return summaries.Take(limit);
    }

    public async Task<IEnumerable<ProcessNetworkProfile>> GetTopNetworksForProcessAsync(string processName, int pid, long startTimeTicks)
    {
        var profiles = await GetProcessNetworkUsageAsync(processName, pid, startTimeTicks);
        return profiles.OrderByDescending(p => p.TotalBytes);
    }

    public async Task<IEnumerable<ProcessNetworkInsight>> GetNetworkSpecificBehaviorInsightsAsync()
    {
        var profiles = await GetProcessNetworkProfilesAsync();
        var processGroups = profiles.GroupBy(p => p.ProcessName).ToList();
        var insights = new List<ProcessNetworkInsight>();

        // 5. Cross-network behavior
        foreach (var group in processGroups)
        {
            var processName = group.Key;
            var netUsages = group.GroupBy(p => p.NetworkName)
                                 .Select(g => new { NetworkName = g.Key, TotalBytes = g.Sum(x => x.TotalBytes) })
                                 .OrderByDescending(x => x.TotalBytes)
                                 .ToList();

            if (netUsages.Count >= 2)
            {
                var highest = netUsages[0];
                var lowest = netUsages[^1];
                if (highest.TotalBytes > 0 && lowest.TotalBytes > 0 && highest.TotalBytes != lowest.TotalBytes)
                {
                    double diffPct = ((double)(highest.TotalBytes - lowest.TotalBytes) / lowest.TotalBytes) * 100.0;
                    insights.Add(new ProcessNetworkInsight
                    {
                        Title = $"Cross-Network Behavior: {processName}",
                        Description = $"{processName} consumes significantly more data on '{highest.NetworkName}' than on '{lowest.NetworkName}'.",
                        NetworkName = highest.NetworkName,
                        ProcessName = processName,
                        Severity = diffPct > 100.0 ? ProcessNetworkInsightSeverity.Warning : ProcessNetworkInsightSeverity.Info,
                        ActionableStep = $"Highest: {highest.NetworkName} ({ByteFormatter.FormatBytes(highest.TotalBytes)}), Lowest: {lowest.NetworkName} ({ByteFormatter.FormatBytes(lowest.TotalBytes)}). Difference: {diffPct:F1}%"
                    });
                }
            }
        }

        // 8. Download / Upload Traffic classification
        foreach (var group in processGroups)
        {
            var processName = group.Key;
            long dl = group.Sum(x => x.DownloadBytes);
            long ul = group.Sum(x => x.UploadBytes);
            long total = dl + ul;
            if (total > 10_000_000)
            {
                double dlRatio = (double)dl / total;
                string classification = "Balanced";
                if (dlRatio > 0.8) classification = "Download-Heavy";
                else if (dlRatio < 0.2) classification = "Upload-Heavy";

                insights.Add(new ProcessNetworkInsight
                {
                    Title = $"Application Traffic Classification: {processName}",
                    Description = $"{processName} exhibits {classification.ToLower()} behavior.",
                    NetworkName = "All Networks",
                    ProcessName = processName,
                    Severity = ProcessNetworkInsightSeverity.Info,
                    ActionableStep = $"{classification} profile: {ByteFormatter.FormatBytes(dl)} down, {ByteFormatter.FormatBytes(ul)} up (Ratio: {dlRatio:F2})"
                });
            }
        }

        return insights;
    }

    public async Task<IEnumerable<ProcessNetworkAnomaly>> GetProcessNetworkAnomaliesAsync()
    {
        var anomalies = new List<ProcessNetworkAnomaly>();
        string connectionString = (_repository as SqliteNetworkUsageRepository)?.ConnectionString ?? "Data Source=datasense.db";

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
        await connection.OpenAsync();

        // 1. Load Daily process-network records for baseline statistics
        const string sqlDaily = @"
            SELECT 
                p.ProcessName,
                p.Pid,
                p.StartTimeTicks,
                s.NetworkName,
                date(p.Timestamp) AS Day,
                SUM(p.BytesDownloaded) AS DownloadBytes,
                SUM(p.BytesUploaded) AS UploadBytes
            FROM ProcessUsageRecords p
            JOIN NetworkSessions s ON p.Timestamp >= s.StartTime AND (s.EndTime IS NULL OR p.Timestamp <= s.EndTime)
            GROUP BY p.ProcessName, p.Pid, p.StartTimeTicks, s.NetworkName, Day;";

        var dailyData = new List<DailyRecord>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = sqlDaily;
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                dailyData.Add(new DailyRecord
                {
                    ProcessName = reader.GetString(0),
                    Pid = reader.GetInt32(1),
                    StartTimeTicks = reader.GetInt64(2),
                    NetworkName = reader.GetString(3),
                    Day = DateTime.Parse(reader.GetString(4)),
                    DownloadBytes = reader.GetInt64(5),
                    UploadBytes = reader.GetInt64(6)
                });
            }
        }

        var groups = dailyData.GroupBy(d => (d.ProcessName, d.Pid, d.StartTimeTicks, d.NetworkName));
        foreach (var group in groups)
        {
            var key = group.Key;
            var list = group.OrderBy(x => x.Day).ToList();

            // We need at least 3 historical observations to establish a baseline
            if (list.Count < 3) continue;

            var historical = list.Take(list.Count - 1).ToList();
            var latest = list[^1];

            // Evaluate Total
            var totalSamples = historical.Select(h => (double)h.TotalBytes).ToList();
            var totalPattern = ComputePatternStats(totalSamples);
            if (totalPattern.StdDev > 0)
            {
                double zScore = (latest.TotalBytes - totalPattern.Mean) / totalPattern.StdDev;
                if (zScore > 2.0 && (latest.TotalBytes - totalPattern.Mean) > 10_000_000)
                {
                    anomalies.Add(new ProcessNetworkAnomaly
                    {
                        ProcessName = key.ProcessName,
                        Pid = key.Pid,
                        StartTimeTicks = key.StartTimeTicks,
                        NetworkName = key.NetworkName,
                        Timestamp = latest.Day,
                        Description = $"Process usage of {ByteFormatter.FormatBytes(latest.TotalBytes)} on network '{key.NetworkName}' is > 2σ above baseline ({zScore:F1}σ).",
                        ExcessBytes = latest.TotalBytes - (long)totalPattern.Mean,
                        DeviationSigma = zScore
                    });
                }
            }

            // Evaluate Download
            var dlSamples = historical.Select(h => (double)h.DownloadBytes).ToList();
            var dlPattern = ComputePatternStats(dlSamples);
            if (dlPattern.StdDev > 0)
            {
                double zScore = (latest.DownloadBytes - dlPattern.Mean) / dlPattern.StdDev;
                if (zScore > 2.0 && (latest.DownloadBytes - dlPattern.Mean) > 10_000_000)
                {
                    anomalies.Add(new ProcessNetworkAnomaly
                    {
                        ProcessName = key.ProcessName,
                        Pid = key.Pid,
                        StartTimeTicks = key.StartTimeTicks,
                        NetworkName = key.NetworkName,
                        Timestamp = latest.Day,
                        Description = $"Download usage of {ByteFormatter.FormatBytes(latest.DownloadBytes)} on network '{key.NetworkName}' is > 2σ above baseline ({zScore:F1}σ).",
                        ExcessBytes = latest.DownloadBytes - (long)dlPattern.Mean,
                        DeviationSigma = zScore
                    });
                }
            }

            // Evaluate Upload
            var ulSamples = historical.Select(h => (double)h.UploadBytes).ToList();
            var ulPattern = ComputePatternStats(ulSamples);
            if (ulPattern.StdDev > 0)
            {
                double zScore = (latest.UploadBytes - ulPattern.Mean) / ulPattern.StdDev;
                if (zScore > 2.0 && (latest.UploadBytes - ulPattern.Mean) > 10_000_000)
                {
                    anomalies.Add(new ProcessNetworkAnomaly
                    {
                        ProcessName = key.ProcessName,
                        Pid = key.Pid,
                        StartTimeTicks = key.StartTimeTicks,
                        NetworkName = key.NetworkName,
                        Timestamp = latest.Day,
                        Description = $"Upload usage of {ByteFormatter.FormatBytes(latest.UploadBytes)} on network '{key.NetworkName}' is > 2σ above baseline ({zScore:F1}σ).",
                        ExcessBytes = latest.UploadBytes - (long)ulPattern.Mean,
                        DeviationSigma = zScore
                    });
                }
            }
        }

        // 2. Evaluate if process contributes an unusually large share of a network session
        const string sqlSessions = @"
            SELECT 
                s.Id AS SessionId,
                s.NetworkName,
                p.ProcessName,
                p.Pid,
                p.StartTimeTicks,
                SUM(p.BytesDownloaded + p.BytesUploaded) AS ProcessSessionBytes,
                s.BytesDownloaded + s.BytesUploaded AS SessionTotalBytes,
                s.StartTime
            FROM ProcessUsageRecords p
            JOIN NetworkSessions s ON p.Timestamp >= s.StartTime AND (s.EndTime IS NULL OR p.Timestamp <= s.EndTime)
            GROUP BY s.Id, p.ProcessName, p.Pid, p.StartTimeTicks;";

        var sessionShares = new List<SessionShareRecord>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = sqlSessions;
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                long processBytes = reader.GetInt64(5);
                long sessionBytes = reader.GetInt64(6);
                if (sessionBytes <= 0) sessionBytes = 1;
                sessionShares.Add(new SessionShareRecord
                {
                    SessionId = reader.GetInt64(0),
                    NetworkName = reader.GetString(1),
                    ProcessName = reader.GetString(2),
                    Pid = reader.GetInt32(3),
                    StartTimeTicks = reader.GetInt64(4),
                    ProcessBytes = processBytes,
                    SessionBytes = sessionBytes,
                    StartTime = DateTime.Parse(reader.GetString(7))
                });
            }
        }

        var shareGroups = sessionShares.GroupBy(s => (s.ProcessName, s.Pid, s.StartTimeTicks, s.NetworkName));
        foreach (var group in shareGroups)
        {
            var key = group.Key;
            var list = group.OrderBy(x => x.StartTime).ToList();

            if (list.Count < 3) continue;

            var historical = list.Take(list.Count - 1).ToList();
            var latest = list[^1];

            var shareSamples = historical.Select(h => (double)h.ProcessBytes / h.SessionBytes).ToList();
            var sharePattern = ComputePatternStats(shareSamples);
            if (sharePattern.StdDev > 0)
            {
                double latestShare = (double)latest.ProcessBytes / latest.SessionBytes;
                double zScore = (latestShare - sharePattern.Mean) / sharePattern.StdDev;

                if (zScore > 2.0 && latest.ProcessBytes > 10_000_000)
                {
                    anomalies.Add(new ProcessNetworkAnomaly
                    {
                        ProcessName = key.ProcessName,
                        Pid = key.Pid,
                        StartTimeTicks = key.StartTimeTicks,
                        NetworkName = key.NetworkName,
                        Timestamp = latest.StartTime,
                        Description = $"Process share ({latestShare * 100:F1}%) of session total in '{key.NetworkName}' is > 2σ above baseline ({zScore:F1}σ).",
                        ExcessBytes = latest.ProcessBytes,
                        DeviationSigma = zScore
                    });
                }
            }
        }

        return anomalies;
    }

    public async Task<string> GetNetworkSpikeAttributionAsync(DateTime startTime, DateTime endTime)
    {
        string connectionString = (_repository as SqliteNetworkUsageRepository)?.ConnectionString ?? "Data Source=datasense.db";
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT ProcessName, SUM(BytesDownloaded + BytesUploaded) AS TotalUsage
            FROM ProcessUsageRecords
            WHERE Timestamp >= @Start AND Timestamp <= @End
            GROUP BY ProcessName
            ORDER BY TotalUsage DESC;";

        var list = new List<(string Name, long Bytes)>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@Start", startTime.ToString("o"));
            cmd.Parameters.AddWithValue("@End", endTime.ToString("o"));
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add((reader.GetString(0), reader.GetInt64(1)));
            }
        }

        if (list.Count == 0)
        {
            return "No process telemetry available for this period.";
        }

        long totalProcessBytes = list.Sum(x => x.Bytes);
        if (totalProcessBytes <= 0)
        {
            return "No active process telemetry recorded during this period.";
        }

        var top = list[0];
        double pct = ((double)top.Bytes / totalProcessBytes) * 100.0;
        
        string timeStr = $"{startTime:HH:mm}–{endTime:HH:mm}";
        return $"Network usage spike detected between {timeStr}. {top.Name} was the top recorded contributor, accounting for {pct:F0}% of recorded process traffic during this period.";
    }

    private static (double Mean, double StdDev) ComputePatternStats(IList<double> values)
    {
        if (values == null || values.Count == 0) return (0, 0);
        double mean = values.Average();
        double sumSquares = values.Sum(v => Math.Pow(v - mean, 2));
        double stdDev = Math.Sqrt(sumSquares / values.Count);
        return (mean, stdDev);
    }

    private class DailyRecord
    {
        public string ProcessName { get; set; } = string.Empty;
        public int Pid { get; set; }
        public long StartTimeTicks { get; set; }
        public string NetworkName { get; set; } = string.Empty;
        public DateTime Day { get; set; }
        public long DownloadBytes { get; set; }
        public long UploadBytes { get; set; }
        public long TotalBytes => DownloadBytes + UploadBytes;
    }

    private class SessionShareRecord
    {
        public long SessionId { get; set; }
        public string NetworkName { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public int Pid { get; set; }
        public long StartTimeTicks { get; set; }
        public long ProcessBytes { get; set; }
        public long SessionBytes { get; set; }
        public DateTime StartTime { get; set; }
    }
}
