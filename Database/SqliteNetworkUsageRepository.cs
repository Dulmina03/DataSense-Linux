using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using DataSense.Models;

namespace DataSense.Database;

public class SqliteNetworkUsageRepository : INetworkUsageRepository
{
    private readonly string _connectionString;

    public SqliteNetworkUsageRepository()
    {
        string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string dbDirectory = Path.Combine(appDataPath, "DataSense");
        string dbPath = Path.Combine(dbDirectory, "datasense.db");

        _connectionString = $"Data Source={dbPath}";
    }

    public SqliteNetworkUsageRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
    }

    public async Task InitializeAsync()
    {
        var builder = new SqliteConnectionStringBuilder(_connectionString);
        string? dbPath = builder.DataSource;

        if (!string.IsNullOrEmpty(dbPath))
        {
            string? dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string createTableSql = @"
            PRAGMA journal_mode=WAL;

            CREATE TABLE IF NOT EXISTS NetworkUsageRecords (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp TEXT NOT NULL,
                InterfaceName TEXT NOT NULL,
                DownloadSpeed REAL NOT NULL,
                UploadSpeed REAL NOT NULL,
                BytesReceived INTEGER NOT NULL,
                BytesSent INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_NetworkUsageRecords_Timestamp 
            ON NetworkUsageRecords(Timestamp);

            CREATE TABLE IF NOT EXISTS NetworkSessions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                NetworkName TEXT NOT NULL,
                InterfaceName TEXT NOT NULL,
                ConnectionType TEXT NOT NULL,
                StartTime TEXT NOT NULL,
                EndTime TEXT,
                BytesDownloaded INTEGER NOT NULL,
                BytesUploaded INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS SpeedTestRecords (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp TEXT NOT NULL,
                DownloadSpeedMbps REAL NOT NULL,
                UploadSpeedMbps REAL NOT NULL,
                PingMs REAL NOT NULL,
                JitterMs REAL NOT NULL,
                ServerName TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ProcessUsageRecords (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp TEXT NOT NULL,
                ProcessName TEXT NOT NULL,
                BytesDownloaded INTEGER NOT NULL,
                BytesUploaded INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_ProcessUsageRecords_Timestamp 
            ON ProcessUsageRecords(Timestamp);
            CREATE INDEX IF NOT EXISTS IX_ProcessUsageRecords_ProcessName 
            ON ProcessUsageRecords(ProcessName);
        ";

        using var command = connection.CreateCommand();
        command.CommandText = createTableSql;
        await command.ExecuteNonQueryAsync();
    }

    public async Task SaveUsageAsync(NetworkUsage usage)
    {
        if (usage == null || string.IsNullOrEmpty(usage.InterfaceName) || usage.InterfaceName == "None")
            return;

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string insertSql = @"
            INSERT INTO NetworkUsageRecords (Timestamp, InterfaceName, DownloadSpeed, UploadSpeed, BytesReceived, BytesSent)
            VALUES (@Timestamp, @InterfaceName, @DownloadSpeed, @UploadSpeed, @BytesReceived, @BytesSent);
        ";

        using var command = connection.CreateCommand();
        command.CommandText = insertSql;

        command.Parameters.AddWithValue("@Timestamp", usage.Timestamp.ToString("o"));
        command.Parameters.AddWithValue("@InterfaceName", usage.InterfaceName);
        command.Parameters.AddWithValue("@DownloadSpeed", usage.DownloadSpeed);
        command.Parameters.AddWithValue("@UploadSpeed", usage.UploadSpeed);
        command.Parameters.AddWithValue("@BytesReceived", usage.BytesReceived);
        command.Parameters.AddWithValue("@BytesSent", usage.BytesSent);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<IEnumerable<NetworkUsageRecord>> GetHistoryAsync(DateTime start, DateTime end, string? interfaceName = null)
    {
        var records = new List<NetworkUsageRecord>();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        string querySql = @"
            SELECT Id, Timestamp, InterfaceName, DownloadSpeed, UploadSpeed, BytesReceived, BytesSent
            FROM NetworkUsageRecords
            WHERE Timestamp >= @Start AND Timestamp <= @End
        ";

        if (!string.IsNullOrEmpty(interfaceName))
        {
            querySql += " AND InterfaceName = @InterfaceName";
        }

        querySql += " ORDER BY Timestamp ASC;";

        using var command = connection.CreateCommand();
        command.CommandText = querySql;

        command.Parameters.AddWithValue("@Start", start.ToString("o"));
        command.Parameters.AddWithValue("@End", end.ToString("o"));
        if (!string.IsNullOrEmpty(interfaceName))
        {
            command.Parameters.AddWithValue("@InterfaceName", interfaceName);
        }

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            records.Add(new NetworkUsageRecord
            {
                Id = reader.GetInt64(0),
                Timestamp = DateTime.Parse(reader.GetString(1)),
                InterfaceName = reader.GetString(2),
                DownloadSpeed = reader.GetDouble(3),
                UploadSpeed = reader.GetDouble(4),
                BytesReceived = reader.GetInt64(5),
                BytesSent = reader.GetInt64(6)
            });
        }

        return records;
    }

    public async Task<(IEnumerable<NetworkUsageRecord> Records, int TotalCount)> GetHistoryPagedAsync(
        DateTime start,
        DateTime end,
        string? interfaceName,
        int pageIndex,
        int pageSize)
    {
        var records = new List<NetworkUsageRecord>();
        int totalCount = 0;

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // Get total count for pagination
        string countSql = @"SELECT COUNT(*) FROM NetworkUsageRecords WHERE Timestamp >= @Start AND Timestamp <= @End";
        if (!string.IsNullOrEmpty(interfaceName))
        {
            countSql += " AND InterfaceName = @InterfaceName";
        }
        using var countCmd = connection.CreateCommand();
        countCmd.CommandText = countSql;
        countCmd.Parameters.AddWithValue("@Start", start.ToString("o"));
        countCmd.Parameters.AddWithValue("@End", end.ToString("o"));
        if (!string.IsNullOrEmpty(interfaceName))
        {
            countCmd.Parameters.AddWithValue("@InterfaceName", interfaceName);
        }
        var cnt = await countCmd.ExecuteScalarAsync();
        totalCount = Convert.ToInt32(cnt);

        // Fetch the page
        string pageSql = @"SELECT Id, Timestamp, InterfaceName, DownloadSpeed, UploadSpeed, BytesReceived, BytesSent
                          FROM NetworkUsageRecords
                          WHERE Timestamp >= @Start AND Timestamp <= @End";
        if (!string.IsNullOrEmpty(interfaceName))
        {
            pageSql += " AND InterfaceName = @InterfaceName";
        }
        pageSql += " ORDER BY Timestamp ASC LIMIT @PageSize OFFSET @Offset;";
        using var pageCmd = connection.CreateCommand();
        pageCmd.CommandText = pageSql;
        pageCmd.Parameters.AddWithValue("@Start", start.ToString("o"));
        pageCmd.Parameters.AddWithValue("@End", end.ToString("o"));
        if (!string.IsNullOrEmpty(interfaceName))
        {
            pageCmd.Parameters.AddWithValue("@InterfaceName", interfaceName);
        }
        pageCmd.Parameters.AddWithValue("@PageSize", pageSize);
        pageCmd.Parameters.AddWithValue("@Offset", pageIndex * pageSize);
        using var reader = await pageCmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            records.Add(new NetworkUsageRecord
            {
                Id = reader.GetInt64(0),
                Timestamp = DateTime.Parse(reader.GetString(1)),
                InterfaceName = reader.GetString(2),
                DownloadSpeed = reader.GetDouble(3),
                UploadSpeed = reader.GetDouble(4),
                BytesReceived = reader.GetInt64(5),
                BytesSent = reader.GetInt64(6)
            });
        }

        return (records, totalCount);
    }

    public async Task PurgeOldRecordsAsync(TimeSpan retentionPeriod)
    {
        DateTime cutoff = DateTime.UtcNow - retentionPeriod;

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string purgeSql = "DELETE FROM NetworkUsageRecords WHERE Timestamp < @Cutoff;";

        using var command = connection.CreateCommand();
        command.CommandText = purgeSql;
        command.Parameters.AddWithValue("@Cutoff", cutoff.ToString("o"));

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Aggregates usage by day using MIN/MAX of cumulative counters.
    /// Daily download = MAX(BytesReceived) - MIN(BytesReceived) for that UTC day.
    /// Counter resets (where delta would be negative) are clamped to 0.
    /// Results are ordered by day descending (most recent first).
    /// </summary>
    public async Task<IEnumerable<DailyUsageRecord>> GetDailyUsageAsync(
        DateTime start,
        DateTime end,
        string? interfaceName = null)
    {
        var results = new List<DailyUsageRecord>();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        string sql = @"
            SELECT
                date(Timestamp) AS Day,
                MIN(BytesReceived)  AS MinRx,
                MAX(BytesReceived)  AS MaxRx,
                MIN(BytesSent)      AS MinTx,
                MAX(BytesSent)      AS MaxTx
            FROM NetworkUsageRecords
            WHERE Timestamp >= @Start AND Timestamp <= @End";

        if (!string.IsNullOrEmpty(interfaceName))
            sql += " AND InterfaceName = @InterfaceName";

        sql += " GROUP BY date(Timestamp) ORDER BY Day DESC;";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@Start", start.ToString("o"));
        cmd.Parameters.AddWithValue("@End",   end.ToString("o"));
        if (!string.IsNullOrEmpty(interfaceName))
            cmd.Parameters.AddWithValue("@InterfaceName", interfaceName);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var day      = DateTime.Parse(reader.GetString(0));
            long minRx   = reader.GetInt64(1);
            long maxRx   = reader.GetInt64(2);
            long minTx   = reader.GetInt64(3);
            long maxTx   = reader.GetInt64(4);

            // Clamp to 0 in case of counter resets
            long dl = Math.Max(0, maxRx - minRx);
            long ul = Math.Max(0, maxTx - minTx);

            results.Add(new DailyUsageRecord
            {
                Day             = day,
                BytesDownloaded = dl,
                BytesUploaded   = ul,
            });
        }

        return results;
    }

    /// <summary>Returns distinct interface names recorded in the database.</summary>
    public async Task<IEnumerable<string>> GetInterfaceNamesAsync()
    {
        var names = new List<string>();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = "SELECT DISTINCT InterfaceName FROM NetworkUsageRecords ORDER BY InterfaceName;";
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));

        return names;
    }

    /// <summary>
    /// Returns today's downloaded and uploaded bytes (UTC calendar day) by
    /// delegating to GetDailyUsageAsync with today's date range.
    /// Reuses MAX–MIN clamping already in that method.
    /// </summary>
    public async Task<(long BytesDownloaded, long BytesUploaded)> GetTodaySummaryAsync(string? interfaceName = null)
    {
        var utcNow = DateTime.UtcNow;
        var start  = utcNow.Date;
        var end    = utcNow.Date.AddDays(1).AddTicks(-1);

        var daily = await GetDailyUsageAsync(start, end, interfaceName);
        var row   = daily.FirstOrDefault();

        return row is null ? (0L, 0L) : (row.BytesDownloaded, row.BytesUploaded);
    }

    /// <summary>
    /// Returns the current UTC calendar month's downloaded and uploaded bytes by
    /// summing each day's already-clamped delta returned by GetDailyUsageAsync.
    /// </summary>
    public async Task<(long BytesDownloaded, long BytesUploaded)> GetMonthSummaryAsync(string? interfaceName = null)
    {
        var utcNow     = DateTime.UtcNow;
        var monthStart = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthEnd   = monthStart.AddMonths(1).AddTicks(-1);

        var daily  = await GetDailyUsageAsync(monthStart, monthEnd, interfaceName);
        long dl    = 0;
        long ul    = 0;
        foreach (var d in daily) { dl += d.BytesDownloaded; ul += d.BytesUploaded; }

        return (dl, ul);
    }

    /// <summary>
    /// Returns one <see cref="HourlyUsageRecord"/> per clock-hour for a single UTC calendar day.
    /// Uses the same MAX–MIN clamping strategy as GetDailyUsageAsync.
    /// Hours with no records are omitted (caller fills gaps for display if needed).
    /// </summary>
    public async Task<IEnumerable<HourlyUsageRecord>> GetHourlyUsageAsync(
        DateTime day, string? interfaceName = null)
    {
        var results = new List<HourlyUsageRecord>();

        // Clamp to UTC day boundaries
        var dayStart = day.Date.ToUniversalTime();
        var dayEnd   = dayStart.AddDays(1).AddTicks(-1);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        string sql = @"
            SELECT
                CAST(strftime('%H', Timestamp) AS INTEGER) AS Hour,
                MIN(BytesReceived) AS MinRx,
                MAX(BytesReceived) AS MaxRx,
                MIN(BytesSent)     AS MinTx,
                MAX(BytesSent)     AS MaxTx
            FROM NetworkUsageRecords
            WHERE Timestamp >= @Start AND Timestamp <= @End";

        if (!string.IsNullOrEmpty(interfaceName))
            sql += " AND InterfaceName = @InterfaceName";

        sql += " GROUP BY Hour ORDER BY Hour ASC;";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@Start", dayStart.ToString("o"));
        cmd.Parameters.AddWithValue("@End",   dayEnd.ToString("o"));
        if (!string.IsNullOrEmpty(interfaceName))
            cmd.Parameters.AddWithValue("@InterfaceName", interfaceName);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            int  hour  = reader.GetInt32(0);
            long minRx = reader.GetInt64(1);
            long maxRx = reader.GetInt64(2);
            long minTx = reader.GetInt64(3);
            long maxTx = reader.GetInt64(4);

            results.Add(new HourlyUsageRecord
            {
                Hour            = hour,
                BytesDownloaded = Math.Max(0, maxRx - minRx),
                BytesUploaded   = Math.Max(0, maxTx - minTx),
            });
        }

        return results;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Network Sessions
    // ────────────────────────────────────────────────────────────────────────

    public async Task SaveSessionAsync(NetworkSession session)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            INSERT INTO NetworkSessions (NetworkName, InterfaceName, ConnectionType, StartTime, EndTime, BytesDownloaded, BytesUploaded)
            VALUES (@NetworkName, @InterfaceName, @ConnectionType, @StartTime, @EndTime, @BytesDownloaded, @BytesUploaded);
            SELECT last_insert_rowid();";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@NetworkName", session.NetworkName);
        command.Parameters.AddWithValue("@InterfaceName", session.InterfaceName);
        command.Parameters.AddWithValue("@ConnectionType", session.ConnectionType);
        command.Parameters.AddWithValue("@StartTime", session.StartTime.ToString("o"));
        command.Parameters.AddWithValue("@EndTime", session.EndTime.HasValue ? session.EndTime.Value.ToString("o") : DBNull.Value);
        command.Parameters.AddWithValue("@BytesDownloaded", session.BytesDownloaded);
        command.Parameters.AddWithValue("@BytesUploaded", session.BytesUploaded);

        var id = await command.ExecuteScalarAsync();
        if (id != null)
        {
            session.Id = Convert.ToInt64(id);
        }
    }

    public async Task UpdateSessionAsync(NetworkSession session)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            UPDATE NetworkSessions
            SET EndTime = @EndTime,
                BytesDownloaded = @BytesDownloaded,
                BytesUploaded = @BytesUploaded
            WHERE Id = @Id;";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@Id", session.Id);
        command.Parameters.AddWithValue("@EndTime", session.EndTime.HasValue ? session.EndTime.Value.ToString("o") : DBNull.Value);
        command.Parameters.AddWithValue("@BytesDownloaded", session.BytesDownloaded);
        command.Parameters.AddWithValue("@BytesUploaded", session.BytesUploaded);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<IEnumerable<NetworkSession>> GetSessionsAsync(DateTime start, DateTime end, string? interfaceName = null)
    {
        var sessions = new List<NetworkSession>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        string sql = @"
            SELECT Id, NetworkName, InterfaceName, ConnectionType, StartTime, EndTime, BytesDownloaded, BytesUploaded
            FROM NetworkSessions
            WHERE StartTime >= @Start AND StartTime <= @End";

        if (!string.IsNullOrEmpty(interfaceName))
        {
            sql += " AND InterfaceName = @InterfaceName";
        }
        
        sql += " ORDER BY StartTime DESC;";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@Start", start.ToString("o"));
        command.Parameters.AddWithValue("@End", end.ToString("o"));
        
        if (!string.IsNullOrEmpty(interfaceName))
        {
            command.Parameters.AddWithValue("@InterfaceName", interfaceName);
        }

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            sessions.Add(new NetworkSession
            {
                Id = reader.GetInt64(0),
                NetworkName = reader.GetString(1),
                InterfaceName = reader.GetString(2),
                ConnectionType = reader.GetString(3),
                StartTime = DateTime.Parse(reader.GetString(4)),
                EndTime = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5)),
                BytesDownloaded = reader.GetInt64(6),
                BytesUploaded = reader.GetInt64(7)
            });
        }
        
        return sessions;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Speed Tests
    // ────────────────────────────────────────────────────────────────────────

    public async Task SaveSpeedTestAsync(SpeedTestRecord record)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            INSERT INTO SpeedTestRecords (Timestamp, DownloadSpeedMbps, UploadSpeedMbps, PingMs, JitterMs, ServerName)
            VALUES (@Timestamp, @DownloadSpeedMbps, @UploadSpeedMbps, @PingMs, @JitterMs, @ServerName);
            SELECT last_insert_rowid();";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@Timestamp", record.Timestamp.ToString("o"));
        command.Parameters.AddWithValue("@DownloadSpeedMbps", record.DownloadSpeedMbps);
        command.Parameters.AddWithValue("@UploadSpeedMbps", record.UploadSpeedMbps);
        command.Parameters.AddWithValue("@PingMs", record.PingMs);
        command.Parameters.AddWithValue("@JitterMs", record.JitterMs);
        command.Parameters.AddWithValue("@ServerName", record.ServerName);

        var id = await command.ExecuteScalarAsync();
        if (id != null)
        {
            record.Id = Convert.ToInt64(id);
        }
    }

    public async Task<IEnumerable<SpeedTestRecord>> GetSpeedTestsAsync(int count = 50)
    {
        var records = new List<SpeedTestRecord>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT Id, Timestamp, DownloadSpeedMbps, UploadSpeedMbps, PingMs, JitterMs, ServerName
            FROM SpeedTestRecords
            ORDER BY Timestamp DESC
            LIMIT @Count;";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@Count", count);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            records.Add(new SpeedTestRecord
            {
                Id = reader.GetInt64(0),
                Timestamp = DateTime.Parse(reader.GetString(1)),
                DownloadSpeedMbps = reader.GetDouble(2),
                UploadSpeedMbps = reader.GetDouble(3),
                PingMs = reader.GetDouble(4),
                JitterMs = reader.GetDouble(5),
                ServerName = reader.GetString(6)
            });
        }

        return records;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Process Analytics
    // ────────────────────────────────────────────────────────────────────────

    public async Task SaveProcessUsageAsync(ProcessUsageRecord record)
    {
        if (string.IsNullOrEmpty(record.ProcessName)) return;

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            INSERT INTO ProcessUsageRecords (Timestamp, ProcessName, BytesDownloaded, BytesUploaded)
            VALUES (@Timestamp, @ProcessName, @BytesDownloaded, @BytesUploaded);
        ";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@Timestamp", record.Timestamp.ToString("o"));
        command.Parameters.AddWithValue("@ProcessName", record.ProcessName);
        command.Parameters.AddWithValue("@BytesDownloaded", record.BytesDownloaded);
        command.Parameters.AddWithValue("@BytesUploaded", record.BytesUploaded);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<IEnumerable<HourlyUsageRecord>> GetProcessHourlyUsageAsync(string processName, DateTime day)
    {
        var results = new List<HourlyUsageRecord>();
        var dayStart = day.Date.ToUniversalTime();
        var dayEnd   = dayStart.AddDays(1).AddTicks(-1);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // For ProcessUsageRecords, each row is a discrete chunk of integrated usage (not cumulative counters).
        // So we just SUM them by hour.
        string sql = @"
            SELECT
                CAST(strftime('%H', Timestamp) AS INTEGER) AS Hour,
                SUM(BytesDownloaded) AS TotalDl,
                SUM(BytesUploaded)   AS TotalUl
            FROM ProcessUsageRecords
            WHERE Timestamp >= @Start AND Timestamp <= @End AND ProcessName = @ProcessName
            GROUP BY Hour ORDER BY Hour ASC;";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@Start", dayStart.ToString("o"));
        cmd.Parameters.AddWithValue("@End", dayEnd.ToString("o"));
        cmd.Parameters.AddWithValue("@ProcessName", processName);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new HourlyUsageRecord
            {
                Hour = reader.GetInt32(0),
                BytesDownloaded = reader.GetInt64(1),
                BytesUploaded = reader.GetInt64(2)
            });
        }
        return results;
    }

    public async Task<IEnumerable<DailyUsageRecord>> GetProcessDailyUsageAsync(string processName, DateTime start, DateTime end)
    {
        var results = new List<DailyUsageRecord>();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        string sql = @"
            SELECT
                date(Timestamp) AS Day,
                SUM(BytesDownloaded) AS TotalDl,
                SUM(BytesUploaded)   AS TotalUl
            FROM ProcessUsageRecords
            WHERE Timestamp >= @Start AND Timestamp <= @End AND ProcessName = @ProcessName
            GROUP BY date(Timestamp) ORDER BY Day DESC;";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@Start", start.ToString("o"));
        cmd.Parameters.AddWithValue("@End", end.ToString("o"));
        cmd.Parameters.AddWithValue("@ProcessName", processName);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new DailyUsageRecord
            {
                Day = DateTime.Parse(reader.GetString(0)),
                BytesDownloaded = reader.GetInt64(1),
                BytesUploaded = reader.GetInt64(2)
            });
        }
        return results;
    }

    public async Task<IEnumerable<ProcessUsageRecord>> GetTopProcessesAsync(DateTime start, DateTime end, int limit)
    {
        var results = new List<ProcessUsageRecord>();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        string sql = @"
            SELECT
                ProcessName,
                SUM(BytesDownloaded) AS TotalDl,
                SUM(BytesUploaded)   AS TotalUl
            FROM ProcessUsageRecords
            WHERE Timestamp >= @Start AND Timestamp <= @End
            GROUP BY ProcessName
            ORDER BY (SUM(BytesDownloaded) + SUM(BytesUploaded)) DESC
            LIMIT @Limit;";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@Start", start.ToString("o"));
        cmd.Parameters.AddWithValue("@End", end.ToString("o"));
        cmd.Parameters.AddWithValue("@Limit", limit);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new ProcessUsageRecord
            {
                ProcessName = reader.GetString(0),
                BytesDownloaded = reader.GetInt64(1),
                BytesUploaded = reader.GetInt64(2)
            });
        }
        return results;
    }
}
