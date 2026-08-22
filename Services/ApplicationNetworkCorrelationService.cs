using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using DataSense.Database;
using DataSense.Models;
using DataSense.Helpers;

namespace DataSense.Services;

public class ApplicationNetworkCorrelationService : IApplicationNetworkCorrelationService
{
    private readonly INetworkUsageRepository _repository;
    private readonly IPatternAnalysisService _patternService;
    private readonly IForecastService _forecastService;
    private readonly IEventService _eventService;
    
    private IEnumerable<ApplicationNetworkProfile>? _cachedProfiles;
    private DateTime _cacheExpiration = DateTime.MinValue;
    private readonly object _cacheLock = new();
    private int _dbQueryFailures = 0;

    public ApplicationNetworkCorrelationService(
        INetworkUsageRepository repository,
        IPatternAnalysisService patternService,
        IForecastService forecastService,
        IEventService eventService)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _patternService = patternService ?? throw new ArgumentNullException(nameof(patternService));
        _forecastService = forecastService ?? throw new ArgumentNullException(nameof(forecastService));
        _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
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

    public async Task<IEnumerable<ApplicationNetworkProfile>> GetApplicationNetworkProfilesAsync(bool forceRefresh = false)
    {
        lock (_cacheLock)
        {
            if (!forceRefresh && _cachedProfiles != null && DateTime.UtcNow < _cacheExpiration)
            {
                return _cachedProfiles;
            }
        }

        var results = new List<ApplicationNetworkProfile>();
        string connectionString = (_repository as SqliteNetworkUsageRepository)?.ConnectionString ?? "Data Source=datasense.db";

        try
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            // 1. Core aggregations: Process + Network
            const string sqlAgg = @"
                SELECT 
                    p.ProcessName,
                    p.Pid,
                    p.StartTimeTicks,
                    MAX(p.ExecutablePath) AS ExecPath,
                    MAX(p.UserName) AS User,
                    MAX(p.DataSource) AS Source,
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

            var baseProfiles = new List<ApplicationNetworkProfile>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = sqlAgg;
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    int activeDays = reader.GetInt32(14);
                    var profile = new ApplicationNetworkProfile
                    {
                        ProcessName = reader.GetString(0),
                        Pid = reader.GetInt32(1),
                        StartTimeTicks = reader.GetInt64(2),
                        ExecutablePath = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                        Username = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                        DataSource = reader.IsDBNull(5) ? "Nethogs" : reader.GetString(5),
                        NetworkName = reader.GetString(6),
                        ConnectionType = reader.GetString(7),
                        InterfaceName = reader.GetString(8),
                        DownloadBytes = reader.GetInt64(9),
                        UploadBytes = reader.GetInt64(10),
                        FirstObserved = DateTime.Parse(reader.GetString(11), null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime(),
                        LastObserved = DateTime.Parse(reader.GetString(12), null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime(),
                        ObservationCount = reader.GetInt32(13),
                        HasSufficientData = activeDays >= 3
                    };
                    profile.ApplicationName = profile.ProcessName;
                    profile.AverageDailyUsage = activeDays > 0 ? (double)profile.TotalBytes / activeDays : profile.TotalBytes;
                    baseProfiles.Add(profile);
                }
            }

            // 2. Fetch Peak Hourly Bytes for each Process + Network
            const string sqlPeakHourly = @"
                SELECT ProcessName, NetworkName, MAX(HourlyBytes) AS MaxHourlyBytes
                FROM (
                    SELECT p.ProcessName, s.NetworkName, strftime('%Y-%m-%d %H', p.Timestamp) AS HourStr, SUM(p.BytesDownloaded + p.BytesUploaded) AS HourlyBytes
                    FROM ProcessUsageRecords p
                    JOIN NetworkSessions s ON p.Timestamp >= s.StartTime AND (s.EndTime IS NULL OR p.Timestamp <= s.EndTime)
                    GROUP BY p.ProcessName, s.NetworkName, HourStr
                ) GROUP BY ProcessName, NetworkName;";
            var peakHourlyDict = new Dictionary<(string Proc, string Net), long>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = sqlPeakHourly;
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    peakHourlyDict[(reader.GetString(0), reader.GetString(1))] = reader.GetInt64(2);
                }
            }

            // 3. Fetch Peak Hour of the Day (0-23) for each Process + Network
            const string sqlPeakHourOfDay = @"
                SELECT ProcessName, NetworkName, Hour, MAX(HourlyBytes)
                FROM (
                    SELECT p.ProcessName, s.NetworkName, CAST(strftime('%H', p.Timestamp) AS INTEGER) AS Hour, SUM(p.BytesDownloaded + p.BytesUploaded) AS HourlyBytes
                    FROM ProcessUsageRecords p
                    JOIN NetworkSessions s ON p.Timestamp >= s.StartTime AND (s.EndTime IS NULL OR p.Timestamp <= s.EndTime)
                    GROUP BY p.ProcessName, s.NetworkName, Hour
                ) GROUP BY ProcessName, NetworkName;";
            var peakHourDict = new Dictionary<(string Proc, string Net), int>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = sqlPeakHourOfDay;
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    peakHourDict[(reader.GetString(0), reader.GetString(1))] = reader.GetInt32(2);
                }
            }

            // 4. Fetch Peak Day of Week for each Process + Network
            const string sqlPeakDayOfWeek = @"
                SELECT ProcessName, NetworkName, DayOfWeek, MAX(DailyBytes)
                FROM (
                    SELECT p.ProcessName, s.NetworkName, 
                        CASE CAST(strftime('%w', p.Timestamp) AS INTEGER)
                            WHEN 0 THEN 'Sunday' WHEN 1 THEN 'Monday' WHEN 2 THEN 'Tuesday' WHEN 3 THEN 'Wednesday'
                            WHEN 4 THEN 'Thursday' WHEN 5 THEN 'Friday' WHEN 6 THEN 'Saturday' END AS DayOfWeek,
                        SUM(p.BytesDownloaded + p.BytesUploaded) AS DailyBytes
                    FROM ProcessUsageRecords p
                    JOIN NetworkSessions s ON p.Timestamp >= s.StartTime AND (s.EndTime IS NULL OR p.Timestamp <= s.EndTime)
                    GROUP BY p.ProcessName, s.NetworkName, DayOfWeek
                ) GROUP BY ProcessName, NetworkName;";
            var peakDayDict = new Dictionary<(string Proc, string Net), string?>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = sqlPeakDayOfWeek;
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    peakDayDict[(reader.GetString(0), reader.GetString(1))] = reader.IsDBNull(2) ? null : reader.GetString(2);
                }
            }

            // Merge peaks & compute averages / percentages
            var networkTotals = baseProfiles.GroupBy(p => p.NetworkName)
                                           .ToDictionary(g => g.Key, g => g.Sum(p => p.TotalBytes));
            var appTotals = baseProfiles.GroupBy(p => p.ProcessName)
                                       .ToDictionary(g => g.Key, g => g.Sum(p => p.TotalBytes));

            foreach (var p in baseProfiles)
            {
                if (peakHourlyDict.TryGetValue((p.ProcessName, p.NetworkName), out long peakHourly))
                {
                    p.PeakHourlyBytes = peakHourly;
                }
                if (peakHourDict.TryGetValue((p.ProcessName, p.NetworkName), out int peakHour))
                {
                    p.PeakHour = peakHour;
                    p.PeakUsagePeriod = peakHour switch
                    {
                        >= 0 and < 6 => "Night",
                        >= 6 and < 12 => "Morning",
                        >= 12 and < 18 => "Afternoon",
                        _ => "Evening"
                    };
                }
                if (peakDayDict.TryGetValue((p.ProcessName, p.NetworkName), out string? peakDay))
                {
                    p.PeakDay = peakDay ?? "Unknown";
                }

                if (networkTotals.TryGetValue(p.NetworkName, out long netTotal) && netTotal > 0)
                {
                    p.PercentageOfNetworkUsage = ((double)p.TotalBytes / netTotal) * 100.0;
                }
                if (appTotals.TryGetValue(p.ProcessName, out long appTotal) && appTotal > 0)
                {
                    p.PercentageOfApplicationUsage = ((double)p.TotalBytes / appTotal) * 100.0;
                }
            }

            results = baseProfiles;
        }
        catch (Exception ex)
        {
            _dbQueryFailures++;
            System.Diagnostics.Debug.WriteLine($"Error querying application network profiles: {ex}");
        }

        lock (_cacheLock)
        {
            _cachedProfiles = results;
            _cacheExpiration = DateTime.UtcNow.AddSeconds(30);
        }

        return results;
    }

    public async Task<IEnumerable<ApplicationNetworkProfile>> GetTopApplicationsForNetworkAsync(string networkName, string sortBy = "Total", int limit = 10)
    {
        if (string.IsNullOrEmpty(networkName)) return Enumerable.Empty<ApplicationNetworkProfile>();

        var profiles = await GetApplicationNetworkProfilesAsync();
        var list = profiles.Where(p => p.NetworkName.Equals(networkName, StringComparison.OrdinalIgnoreCase));

        return sortBy.ToLower() switch
        {
            "download" => list.OrderByDescending(p => p.DownloadBytes).Take(limit),
            "upload" => list.OrderByDescending(p => p.UploadBytes).Take(limit),
            "share" => list.OrderByDescending(p => p.PercentageOfNetworkUsage).Take(limit),
            _ => list.OrderByDescending(p => p.TotalBytes).Take(limit)
        };
    }

    public async Task<IEnumerable<ApplicationNetworkProfile>> GetNetworkUsageForApplicationAsync(string processName)
    {
        if (string.IsNullOrEmpty(processName)) return Enumerable.Empty<ApplicationNetworkProfile>();

        var profiles = await GetApplicationNetworkProfilesAsync();
        return profiles.Where(p => p.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                       .OrderByDescending(p => p.TotalBytes);
    }

    public async Task<NetworkApplicationBreakdown> GetNetworkApplicationBreakdownAsync(string networkName, AnalyticsPeriod period)
    {
        var breakdown = new NetworkApplicationBreakdown();
        if (string.IsNullOrEmpty(networkName)) return breakdown;

        string connectionString = (_repository as SqliteNetworkUsageRepository)?.ConnectionString ?? "Data Source=datasense.db";

        try
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var startLimit = DateTime.UtcNow;
            startLimit = period switch
            {
                AnalyticsPeriod.Today => DateTime.UtcNow.Date,
                AnalyticsPeriod.Last7Days => DateTime.UtcNow.Date.AddDays(-7),
                AnalyticsPeriod.Last30Days => DateTime.UtcNow.Date.AddDays(-30),
                AnalyticsPeriod.ThisMonth => new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1),
                _ => DateTime.MinValue
            };

            const string sqlAgg = @"
                SELECT 
                    p.ProcessName,
                    p.Pid,
                    p.StartTimeTicks,
                    MAX(p.ExecutablePath) AS ExecPath,
                    MAX(p.UserName) AS User,
                    MAX(p.DataSource) AS Source,
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
                WHERE s.NetworkName = @NetworkName AND p.Timestamp >= @Start
                GROUP BY p.ProcessName, p.Pid, p.StartTimeTicks, s.NetworkName, s.ConnectionType, s.InterfaceName;";

            var profiles = new List<ApplicationNetworkProfile>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = sqlAgg;
                cmd.Parameters.AddWithValue("@NetworkName", networkName);
                cmd.Parameters.AddWithValue("@Start", startLimit.ToString("o"));
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    int activeDays = reader.GetInt32(14);
                    var profile = new ApplicationNetworkProfile
                    {
                        ProcessName = reader.GetString(0),
                        Pid = reader.GetInt32(1),
                        StartTimeTicks = reader.GetInt64(2),
                        ExecutablePath = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                        Username = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                        DataSource = reader.IsDBNull(5) ? "Nethogs" : reader.GetString(5),
                        NetworkName = reader.GetString(6),
                        ConnectionType = reader.GetString(7),
                        InterfaceName = reader.GetString(8),
                        DownloadBytes = reader.GetInt64(9),
                        UploadBytes = reader.GetInt64(10),
                        FirstObserved = DateTime.Parse(reader.GetString(11), null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime(),
                        LastObserved = DateTime.Parse(reader.GetString(12), null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime(),
                        ObservationCount = reader.GetInt32(13),
                        HasSufficientData = activeDays >= 3
                    };
                    profile.ApplicationName = profile.ProcessName;
                    profile.AverageDailyUsage = activeDays > 0 ? (double)profile.TotalBytes / activeDays : profile.TotalBytes;
                    profiles.Add(profile);
                }
            }

            long totalTraffic = profiles.Sum(p => p.TotalBytes);

            foreach (var p in profiles)
            {
                if (totalTraffic > 0)
                {
                    p.PercentageOfNetworkUsage = ((double)p.TotalBytes / totalTraffic) * 100.0;
                }
            }

            breakdown.Profiles = profiles;
            breakdown.TotalAttributedTraffic = totalTraffic;

            if (profiles.Count > 0)
            {
                var sortedTotal = profiles.OrderByDescending(p => p.TotalBytes).ToList();
                breakdown.TopApplication = sortedTotal[0].ProcessName;
                breakdown.TopApplicationBytes = sortedTotal[0].TotalBytes;

                var sortedDl = profiles.OrderByDescending(p => p.DownloadBytes).ToList();
                breakdown.DownloadHeavyApplication = sortedDl[0].ProcessName;
                breakdown.DownloadHeavyBytes = sortedDl[0].DownloadBytes;

                var sortedUl = profiles.OrderByDescending(p => p.UploadBytes).ToList();
                breakdown.UploadHeavyApplication = sortedUl[0].ProcessName;
                breakdown.UploadHeavyBytes = sortedUl[0].UploadBytes;
            }

            // Calculate total network traffic in this period from NetworkSessions
            const string sqlSessionTotal = @"
                SELECT SUM(BytesDownloaded + BytesUploaded) 
                FROM NetworkSessions 
                WHERE NetworkName = @NetworkName AND StartTime >= @Start;";
            long networkSessionTotal = 0;
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = sqlSessionTotal;
                cmd.Parameters.AddWithValue("@NetworkName", networkName);
                cmd.Parameters.AddWithValue("@Start", startLimit.ToString("o"));
                var res = await cmd.ExecuteScalarAsync();
                if (res != null && res != DBNull.Value)
                {
                    networkSessionTotal = Convert.ToInt64(res);
                }
            }

            if (networkSessionTotal > 0)
            {
                breakdown.AttributionPercentage = Math.Min(((double)totalTraffic / networkSessionTotal) * 100.0, 100.0);
            }
            else
            {
                breakdown.AttributionPercentage = totalTraffic > 0 ? 100.0 : 0.0;
            }
        }
        catch (Exception ex)
        {
            _dbQueryFailures++;
            System.Diagnostics.Debug.WriteLine($"Error getting network breakdown: {ex}");
        }

        return breakdown;
    }

    public async Task<IEnumerable<ProcessNetworkAnomaly>> GetNetworkSpecificAnomaliesAsync()
    {
        var anomalies = new List<ProcessNetworkAnomaly>();
        string connectionString = (_repository as SqliteNetworkUsageRepository)?.ConnectionString ?? "Data Source=datasense.db";

        try
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

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

                if (list.Count < 3) continue;

                var historical = list.Take(list.Count - 1).ToList();
                var latest = list[^1];

                // Total check
                var totalSamples = historical.Select(h => (double)h.TotalBytes).ToList();
                var totalPattern = ComputePatternStats(totalSamples);
                double totalStd = totalPattern.StdDev > 0 ? totalPattern.StdDev : 1.0;
                double zTotal = (latest.TotalBytes - totalPattern.Mean) / totalStd;

                if (zTotal > 2.0 && (latest.TotalBytes - totalPattern.Mean) > 10_000_000)
                {
                    anomalies.Add(new ProcessNetworkAnomaly
                    {
                        ProcessName = key.ProcessName,
                        Pid = key.Pid,
                        StartTimeTicks = key.StartTimeTicks,
                        NetworkName = key.NetworkName,
                        Timestamp = latest.Day,
                        Description = $"{key.ProcessName} usage on {key.NetworkName} is significantly above its historical baseline ({zTotal:F1}σ). Today: {ByteFormatter.FormatBytes(latest.TotalBytes)}, Baseline: {ByteFormatter.FormatBytes((long)totalPattern.Mean)}.",
                        ExcessBytes = latest.TotalBytes - (long)totalPattern.Mean,
                        DeviationSigma = zTotal
                    });
                }

                // Upload check
                var ulSamples = historical.Select(h => (double)h.UploadBytes).ToList();
                var ulPattern = ComputePatternStats(ulSamples);
                double ulStd = ulPattern.StdDev > 0 ? ulPattern.StdDev : 1.0;
                double zUpload = (latest.UploadBytes - ulPattern.Mean) / ulStd;

                if (zUpload > 2.0 && (latest.UploadBytes - ulPattern.Mean) > 10_000_000)
                {
                    anomalies.Add(new ProcessNetworkAnomaly
                    {
                        ProcessName = key.ProcessName,
                        Pid = key.Pid,
                        StartTimeTicks = key.StartTimeTicks,
                        NetworkName = key.NetworkName,
                        Timestamp = latest.Day,
                        Description = $"{key.ProcessName} upload behavior on {key.NetworkName} is unusually high ({zUpload:F1}σ). Today: {ByteFormatter.FormatBytes(latest.UploadBytes)}, Baseline: {ByteFormatter.FormatBytes((long)ulPattern.Mean)}.",
                        ExcessBytes = latest.UploadBytes - (long)ulPattern.Mean,
                        DeviationSigma = zUpload
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _dbQueryFailures++;
            System.Diagnostics.Debug.WriteLine($"Error running network specific anomaly detection: {ex}");
        }

        return anomalies;
    }

    public async Task<IEnumerable<string>> GetNetworkSpecificInsightsAsync(string networkName)
    {
        var insights = new List<string>();
        if (string.IsNullOrEmpty(networkName)) return insights;

        var topApps = await GetTopApplicationsForNetworkAsync(networkName, "Total", 3);
        var list = topApps.ToList();

        if (list.Count > 0)
        {
            var top = list[0];
            insights.Add($"{top.ProcessName} accounts for {top.PercentageOfNetworkUsage:F0}% of your {networkName} application traffic.");
            insights.Add($"{top.ProcessName} generated {ByteFormatter.FormatBytes(top.TotalBytes)} on this network during the selected period.");
        }

        var uploadHeavy = list.FirstOrDefault(p => p.UploadPercentage > 80.0 && p.TotalBytes > 10_000_000);
        if (uploadHeavy != null)
        {
            insights.Add($"{uploadHeavy.ProcessName} upload traffic is unusually high compared with its historical behavior on this network.");
        }

        return insights;
    }

    public async Task<BudgetCorrelationInfo> GetBudgetCorrelationAsync()
    {
        var correlation = new BudgetCorrelationInfo();
        try
        {
            var budget = await _forecastService.GetBudgetAsync();
            var forecast = await _forecastService.GetForecastAsync();

            string connectionString = (_repository as SqliteNetworkUsageRepository)?.ConnectionString ?? "Data Source=datasense.db";
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var startOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            const string sqlMonthUsage = @"
                SELECT ProcessName, SUM(BytesDownloaded + BytesUploaded) AS TotalUsage
                FROM ProcessUsageRecords
                WHERE Timestamp >= @MonthStart
                GROUP BY ProcessName
                ORDER BY TotalUsage DESC;";

            var appMonthUsage = new List<(string Name, long Bytes)>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = sqlMonthUsage;
                cmd.Parameters.AddWithValue("@MonthStart", startOfMonth.ToString("o"));
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    appMonthUsage.Add((reader.GetString(0), reader.GetInt64(1)));
                }
            }

            if (appMonthUsage.Count > 0 && budget.Enabled && budget.MonthlyLimitBytes > 0)
            {
                var top = appMonthUsage[0];
                correlation.TopApplicationBudgetShare = ((double)top.Bytes / budget.MonthlyLimitBytes) * 100.0;
                
                // Drivers of overuse: consuming > 20% of limit
                foreach (var app in appMonthUsage.Where(a => ((double)a.Bytes / budget.MonthlyLimitBytes) > 0.20))
                {
                    correlation.OveruseDrivers.Add($"{app.Name} ({ByteFormatter.FormatBytes(app.Bytes)})");
                }

                if (forecast.HasSufficientData)
                {
                    double topShareOfForecast = (double)top.Bytes / (appMonthUsage.Sum(x => x.Bytes) > 0 ? appMonthUsage.Sum(x => x.Bytes) : 1);
                    long projectedBytes = (long)(topShareOfForecast * forecast.ProjectedMonthEndBytes);
                    double pct = ((double)projectedBytes / budget.MonthlyLimitBytes) * 100.0;
                    correlation.ProjectedApplicationContribution = $"{top.Name} is projected to consume approximately {pct:F0}% of your monthly allowance.";
                }
            }
        }
        catch (Exception ex)
        {
            _dbQueryFailures++;
            System.Diagnostics.Debug.WriteLine($"Error calculating budget correlation: {ex}");
        }

        return correlation;
    }

    public async Task<HotspotIntelligenceInfo> GetHotspotIntelligenceAsync(string networkName)
    {
        var info = new HotspotIntelligenceInfo();
        if (string.IsNullOrEmpty(networkName)) return info;

        var profiles = await GetApplicationNetworkProfilesAsync();
        var netProfiles = profiles.Where(p => p.NetworkName.Equals(networkName, StringComparison.OrdinalIgnoreCase)).ToList();

        if (netProfiles.Count > 0)
        {
            var first = netProfiles[0];
            bool isMobile = first.ConnectionType.Equals("Mobile", StringComparison.OrdinalIgnoreCase) || 
                            first.ConnectionType.Equals("Hotspot", StringComparison.OrdinalIgnoreCase) ||
                            networkName.Contains("hotspot", StringComparison.OrdinalIgnoreCase) || 
                            networkName.Contains("mobile", StringComparison.OrdinalIgnoreCase);

            info.IsHotspot = isMobile;

            var top3 = netProfiles.OrderByDescending(p => p.TotalBytes).Take(3).ToList();
            info.TopHotspotConsumers = top3.Select(p => $"{p.ProcessName} ({ByteFormatter.FormatBytes(p.TotalBytes)})").ToList();

            info.UploadHeavyApplications = netProfiles.Where(p => p.UploadPercentage > 80.0 && p.TotalBytes > 5_000_000)
                                                      .Select(p => p.ProcessName).ToList();

            long netTotal = netProfiles.Sum(p => p.TotalBytes);
            if (netTotal > 0)
            {
                info.ConcentrationPercentage = ((double)top3.Sum(p => p.TotalBytes) / netTotal) * 100.0;
            }
        }

        return info;
    }

    public async Task<IEnumerable<NetworkPerformanceCorrelation>> GetPerformanceCorrelationAsync(string networkName)
    {
        var list = new List<NetworkPerformanceCorrelation>();
        if (string.IsNullOrEmpty(networkName)) return list;

        string connectionString = (_repository as SqliteNetworkUsageRepository)?.ConnectionString ?? "Data Source=datasense.db";

        try
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            const string sqlPerf = @"
                SELECT AVG(DownloadSpeedMbps), AVG(UploadSpeedMbps), AVG(PingMs)
                FROM SpeedTestRecords
                WHERE NetworkName = @NetworkName;";

            double avgDl = 0, avgUl = 0, latency = 0;
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = sqlPerf;
                cmd.Parameters.AddWithValue("@NetworkName", networkName);
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    avgDl = reader.IsDBNull(0) ? 0 : reader.GetDouble(0);
                    avgUl = reader.IsDBNull(1) ? 0 : reader.GetDouble(1);
                    latency = reader.IsDBNull(2) ? 0 : reader.GetDouble(2);
                }
            }

            var profiles = await GetApplicationNetworkProfilesAsync();
            long trafficVol = profiles.Where(p => p.NetworkName.Equals(networkName, StringComparison.OrdinalIgnoreCase))
                                      .Sum(p => p.TotalBytes);

            list.Add(new NetworkPerformanceCorrelation
            {
                NetworkName = networkName,
                AvgDownloadSpeed = avgDl,
                AvgUploadSpeed = avgUl,
                Latency = latency,
                ApplicationTrafficVolume = trafficVol
            });
        }
        catch (Exception ex)
        {
            _dbQueryFailures++;
            System.Diagnostics.Debug.WriteLine($"Error running performance correlation query: {ex}");
        }

        return list;
    }

    public async Task<CorrelationDiagnosticsInfo> GetDiagnosticsAsync()
    {
        var profiles = await GetApplicationNetworkProfilesAsync();
        var distinctApps = profiles.Select(p => p.ProcessName).Distinct().Count();
        var distinctNets = profiles.Select(p => p.NetworkName).Distinct().Count();

        DateTime? maxSeen = null;
        if (profiles.Any())
        {
            maxSeen = profiles.Max(p => p.LastObserved);
        }

        return new CorrelationDiagnosticsInfo
        {
            ApplicationsAttributedCount = distinctApps,
            NetworksWithAttributionCount = distinctNets,
            LatestCorrelatedRecordTimestamp = maxSeen,
            QueryHealth = _dbQueryFailures == 0 ? "Healthy" : "Degraded",
            DatabaseQueryFailures = _dbQueryFailures
        };
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
}
