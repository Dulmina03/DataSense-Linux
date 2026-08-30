using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using DataSense.Helpers;
using DataSense.Models;

namespace DataSense.Database;

public class SqliteNetworkUsageRepository : INetworkUsageRepository
{
    private readonly string _connectionString;
    public string ConnectionString => _connectionString;

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
            PRAGMA synchronous=NORMAL;
            PRAGMA cache_size=-2000;

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

            CREATE INDEX IF NOT EXISTS IX_NetworkUsageRecords_Interface_Timestamp
            ON NetworkUsageRecords(InterfaceName, Timestamp);

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

            CREATE INDEX IF NOT EXISTS IX_NetworkSessions_StartTime
            ON NetworkSessions(StartTime);

            CREATE INDEX IF NOT EXISTS IX_NetworkSessions_EndTime
            ON NetworkSessions(EndTime);

            CREATE INDEX IF NOT EXISTS IX_NetworkSessions_NetworkName
            ON NetworkSessions(NetworkName);

            CREATE INDEX IF NOT EXISTS IX_NetworkSessions_Interface_StartTime
            ON NetworkSessions(InterfaceName, StartTime);

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
                BytesUploaded INTEGER NOT NULL,
                ExecutablePath TEXT NOT NULL DEFAULT '',
                UserName TEXT NOT NULL DEFAULT '',
                DataSource TEXT NOT NULL DEFAULT 'Nethogs',
                Pid INTEGER NOT NULL DEFAULT 0,
                StartTimeTicks INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS IX_ProcessUsageRecords_Timestamp 
            ON ProcessUsageRecords(Timestamp);
            CREATE INDEX IF NOT EXISTS IX_ProcessUsageRecords_ProcessName 
            ON ProcessUsageRecords(ProcessName);
            CREATE INDEX IF NOT EXISTS IX_ProcessUsageRecords_Process_Time
            ON ProcessUsageRecords(ProcessName, Timestamp);

            CREATE TABLE IF NOT EXISTS AppSettings (
                Key   TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
        ";

        using var command = connection.CreateCommand();
        command.CommandText = createTableSql;
        await command.ExecuteNonQueryAsync();

        // Safe migration for SpeedTestRecords
        try
        {
            using var alterCmd = connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE SpeedTestRecords ADD COLUMN NetworkName TEXT NOT NULL DEFAULT 'Unknown';";
            await alterCmd.ExecuteNonQueryAsync();
        }
        catch (SqliteException) { /* Column already exists */ }

        try
        {
            using var alterCmd = connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE SpeedTestRecords ADD COLUMN ConnectionType TEXT NOT NULL DEFAULT 'Unknown';";
            await alterCmd.ExecuteNonQueryAsync();
        }
        catch (SqliteException) { /* Column already exists */ }

        // Phase 11.26: Migration for ProcessUsageRecords extended columns
        string[] processUsageMigrations = new[]
        {
            "ALTER TABLE ProcessUsageRecords ADD COLUMN ExecutablePath TEXT NOT NULL DEFAULT '';" ,
            "ALTER TABLE ProcessUsageRecords ADD COLUMN UserName TEXT NOT NULL DEFAULT '';" ,
            "ALTER TABLE ProcessUsageRecords ADD COLUMN DataSource TEXT NOT NULL DEFAULT 'Nethogs';" ,
            "ALTER TABLE ProcessUsageRecords ADD COLUMN Pid INTEGER NOT NULL DEFAULT 0;" ,
            "ALTER TABLE ProcessUsageRecords ADD COLUMN StartTimeTicks INTEGER NOT NULL DEFAULT 0;"
        };
        foreach (var migrationSql in processUsageMigrations)
        {
            try
            {
                using var migCmd = connection.CreateCommand();
                migCmd.CommandText = migrationSql;
                await migCmd.ExecuteNonQueryAsync();
            }
            catch (SqliteException) { /* Column already exists */ }
        }

        // Safe intelligent migration for invalid / placeholder NetworkName records in NetworkSessions
        try
        {
            // 1. Identify records with placeholder network names
            using var findCmd = connection.CreateCommand();
            findCmd.CommandText = @"
                SELECT Id, NetworkName, InterfaceName, StartTime, EndTime 
                FROM NetworkSessions 
                WHERE NetworkName IS NULL 
                   OR TRIM(NetworkName) IN ('-', '--', '—', '–', '', 'Unknown', 'unknown', 'Unknown Network', 'Wi-Fi', 'Wifi', 'wifi', 'Wireless', 'Mobile Hotspot', 'Hotspot', 'Connected Network', 'Network', 'None', 'null')
                   OR NetworkName LIKE 'Interface: %'
                ORDER BY StartTime ASC;
            ";

            var invalidSessions = new List<(long Id, string InterfaceName, DateTime StartTime, DateTime? EndTime)>();
            using (var reader = await findCmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    long id = reader.GetInt64(0);
                    string iface = reader.IsDBNull(2) ? "" : reader.GetString(2);
                    if (DateTime.TryParse(reader.GetString(3), out var start))
                    {
                        DateTime? end = reader.IsDBNull(4) ? null : (DateTime.TryParse(reader.GetString(4), out var e) ? e : null);
                        invalidSessions.Add((id, iface, start, end));
                    }
                }
            }

            // For each invalid session, see if we can safely attribute it from adjacent valid sessions on the same interface within 2 hours
            foreach (var invalid in invalidSessions)
            {
                string? inferredNetwork = null;
                if (!string.IsNullOrWhiteSpace(invalid.InterfaceName) && invalid.InterfaceName != "None" && invalid.InterfaceName != "Disconnected")
                {
                    using var adjacentCmd = connection.CreateCommand();
                    adjacentCmd.CommandText = @"
                        SELECT NetworkName 
                        FROM NetworkSessions 
                        WHERE InterfaceName = @Iface 
                          AND Id != @Id
                          AND NetworkName IS NOT NULL 
                          AND TRIM(NetworkName) NOT IN ('-', '--', '—', '–', '', 'Unknown', 'unknown', 'Unknown Network', 'Wi-Fi', 'Wifi', 'wifi', 'Wireless', 'Mobile Hotspot', 'Hotspot', 'Connected Network', 'Network', 'None', 'null')
                          AND NOT NetworkName LIKE 'Interface: %'
                          AND (
                              (StartTime <= @EndPlus AND EndTime >= @StartMinus) OR
                              (StartTime <= @Start AND EndTime >= @Start) OR
                              (StartTime >= @StartMinus AND StartTime <= @EndPlus)
                          )
                        ORDER BY ABS(strftime('%s', StartTime) - strftime('%s', @Start)) ASC
                        LIMIT 1;
                    ";
                    adjacentCmd.Parameters.AddWithValue("@Iface", invalid.InterfaceName);
                    adjacentCmd.Parameters.AddWithValue("@Id", invalid.Id);
                    adjacentCmd.Parameters.AddWithValue("@Start", invalid.StartTime.ToString("o"));
                    adjacentCmd.Parameters.AddWithValue("@StartMinus", invalid.StartTime.AddHours(-2).ToString("o"));
                    adjacentCmd.Parameters.AddWithValue("@EndPlus", (invalid.EndTime ?? invalid.StartTime).AddHours(2).ToString("o"));

                    var result = await adjacentCmd.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                    {
                        inferredNetwork = result.ToString();
                    }
                }

                string targetName = !string.IsNullOrWhiteSpace(inferredNetwork)
                    ? inferredNetwork
                    : (!string.IsNullOrWhiteSpace(invalid.InterfaceName) && invalid.InterfaceName != "None" && invalid.InterfaceName != "Disconnected" ? $"Interface: {invalid.InterfaceName}" : "Unknown Network");

                using var updateCmd = connection.CreateCommand();
                updateCmd.CommandText = "UPDATE NetworkSessions SET NetworkName = @Name WHERE Id = @Id;";
                updateCmd.Parameters.AddWithValue("@Name", targetName);
                updateCmd.Parameters.AddWithValue("@Id", invalid.Id);
                await updateCmd.ExecuteNonQueryAsync();
            }
        }
        catch { }
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

        command.Parameters.AddWithValue("@Timestamp", usage.Timestamp.ToUniversalTime().ToString("o"));
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

        command.Parameters.AddWithValue("@Start", start.ToUniversalTime().ToString("o"));
        command.Parameters.AddWithValue("@End", end.ToUniversalTime().ToString("o"));
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
        countCmd.Parameters.AddWithValue("@Start", start.ToUniversalTime().ToString("o"));
        countCmd.Parameters.AddWithValue("@End", end.ToUniversalTime().ToString("o"));
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
        pageCmd.Parameters.AddWithValue("@Start", start.ToUniversalTime().ToString("o"));
        pageCmd.Parameters.AddWithValue("@End", end.ToUniversalTime().ToString("o"));
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
        cmd.Parameters.AddWithValue("@Start", start.ToUniversalTime().ToString("o"));
        cmd.Parameters.AddWithValue("@End",   end.ToUniversalTime().ToString("o"));
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

    public async Task<(long BytesDownloaded, long BytesUploaded)> GetSessionsSummaryAsync(DateTime start, DateTime end, string? interfaceName = null)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // A closed session overlaps [start,end] when: StartTime <= end AND EndTime >= start.
        // An open session (EndTime IS NULL) is included when it started on or before end.
        string sql = @"
            SELECT COALESCE(SUM(BytesDownloaded), 0), COALESCE(SUM(BytesUploaded), 0)
            FROM NetworkSessions
            WHERE (StartTime <= @End AND (EndTime >= @Start OR EndTime IS NULL))";

        if (!string.IsNullOrEmpty(interfaceName) && interfaceName != "All")
        {
            sql += " AND InterfaceName = @InterfaceName";
        }

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@Start", start.ToUniversalTime().ToString("o"));
        command.Parameters.AddWithValue("@End", end.ToUniversalTime().ToString("o"));
        if (!string.IsNullOrEmpty(interfaceName) && interfaceName != "All")
        {
            command.Parameters.AddWithValue("@InterfaceName", interfaceName);
        }

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return (reader.GetInt64(0), reader.GetInt64(1));
        }

        return (0L, 0L);
    }

    /// <summary>
    /// Returns today's downloaded and uploaded bytes (local calendar day converted to UTC).
    /// Combines NetworkSessions and continuous NetworkUsageRecords samples so Today updates in real time.
    /// </summary>
    public async Task<(long BytesDownloaded, long BytesUploaded)> GetTodaySummaryAsync(string? interfaceName = null)
    {
        var utcNow = DateTime.UtcNow;
        var start  = DateTime.SpecifyKind(utcNow.Date, DateTimeKind.Utc);
        var end    = start.AddDays(1).AddTicks(-1);

        var (sDl, sUl) = await GetSessionsSummaryAsync(start, end, interfaceName);

        var daily = await GetDailyUsageAsync(start, end, interfaceName);
        var row   = daily.FirstOrDefault();
        long rDl  = row?.BytesDownloaded ?? 0;
        long rUl  = row?.BytesUploaded ?? 0;

        long dl = Math.Max(sDl, rDl);
        long ul = Math.Max(sUl, rUl);

        if (dl > 0 || ul > 0)
        {
            return (dl, ul);
        }

        var procHourly = await GetAllProcessesHourlyUsageAsync(start);
        long pDl = procHourly.Sum(h => h.BytesDownloaded);
        long pUl = procHourly.Sum(h => h.BytesUploaded);
        return (pDl, pUl);
    }

    /// <summary>
    /// Returns the current UTC calendar month's downloaded and uploaded bytes.
    /// </summary>
    public async Task<(long BytesDownloaded, long BytesUploaded)> GetMonthSummaryAsync(string? interfaceName = null)
    {
        var utcNow     = DateTime.UtcNow;
        var monthStart = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthEnd   = monthStart.AddMonths(1).AddTicks(-1);

        var (sDl, sUl) = await GetSessionsSummaryAsync(monthStart, monthEnd, interfaceName);
        var daily = await GetDailyUsageAsync(monthStart, monthEnd, interfaceName);
        long rDl = daily.Sum(d => d.BytesDownloaded);
        long rUl = daily.Sum(d => d.BytesUploaded);

        long dl = Math.Max(sDl, rDl);
        long ul = Math.Max(sUl, rUl);

        if (dl > 0 || ul > 0)
        {
            return (dl, ul);
        }

        var procDaily = await GetAllProcessesDailyUsageAsync(monthStart, monthEnd);
        long pDl = procDaily.Sum(d => d.BytesDownloaded);
        long pUl = procDaily.Sum(d => d.BytesUploaded);
        return (pDl, pUl);
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
        cmd.Parameters.AddWithValue("@Start", dayStart.ToUniversalTime().ToString("o"));
        cmd.Parameters.AddWithValue("@End",   dayEnd.ToUniversalTime().ToString("o"));
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
        command.Parameters.AddWithValue("@StartTime", session.StartTime.ToUniversalTime().ToString("o"));
        command.Parameters.AddWithValue("@EndTime", session.EndTime.HasValue ? session.EndTime.Value.ToUniversalTime().ToString("o") : DBNull.Value);
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
        command.Parameters.AddWithValue("@EndTime", session.EndTime.HasValue ? session.EndTime.Value.ToUniversalTime().ToString("o") : DBNull.Value);
        command.Parameters.AddWithValue("@BytesDownloaded", session.BytesDownloaded);
        command.Parameters.AddWithValue("@BytesUploaded", session.BytesUploaded);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Closes any sessions that have EndTime = NULL and were started before <paramref name="cutoff"/>.
    /// EndTime is set to StartTime (last-seen time) so the session byte totals stay intact but
    /// the session is no longer treated as "active" or "open" by any date-range query.
    /// This repairs sessions that were orphaned by a crash or force-quit.
    /// </summary>
    public async Task CloseOrphanedSessionsAsync(DateTime cutoff)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // Set EndTime = StartTime for orphaned sessions.
        // We use StartTime rather than the current clock so the session's date attribution
        // remains correct (it belongs to the day it started, not today).
        const string sql = @"
            UPDATE NetworkSessions
            SET EndTime = StartTime
            WHERE EndTime IS NULL
              AND StartTime < @Cutoff;";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@Cutoff", cutoff.ToUniversalTime().ToString("o"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IEnumerable<NetworkSession>> GetSessionsAsync(DateTime start, DateTime end, string? interfaceName = null, string? networkName = null)
    {
        var results = new List<NetworkSession>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // Same predicate as GetSessionsSummaryAsync: open sessions are only included
        // for the period in which they started, preventing stale sessions from
        // polluting later time ranges.
        string sql = @"
            SELECT Id, NetworkName, InterfaceName, ConnectionType, StartTime, EndTime, BytesDownloaded, BytesUploaded
            FROM NetworkSessions
            WHERE (StartTime <= @End AND (EndTime >= @Start OR EndTime IS NULL))";

        if (!string.IsNullOrEmpty(interfaceName))
            sql += " AND InterfaceName = @InterfaceName";
        if (!string.IsNullOrEmpty(networkName))
            sql += " AND NetworkName = @NetworkName";

        sql += " ORDER BY StartTime DESC;";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@Start", start.ToUniversalTime().ToString("o"));
        command.Parameters.AddWithValue("@End", end.ToUniversalTime().ToString("o"));
        if (!string.IsNullOrEmpty(interfaceName))
            command.Parameters.AddWithValue("@InterfaceName", interfaceName);
        if (!string.IsNullOrEmpty(networkName))
            command.Parameters.AddWithValue("@NetworkName", networkName);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new NetworkSession
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
        
        return results;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Speed Tests
    // ────────────────────────────────────────────────────────────────────────

    public async Task SaveSpeedTestAsync(SpeedTestRecord record)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            INSERT INTO SpeedTestRecords (Timestamp, DownloadSpeedMbps, UploadSpeedMbps, PingMs, JitterMs, ServerName, NetworkName, ConnectionType)
            VALUES (@Timestamp, @DownloadSpeedMbps, @UploadSpeedMbps, @PingMs, @JitterMs, @ServerName, @NetworkName, @ConnectionType);
            SELECT last_insert_rowid();";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@Timestamp", record.Timestamp.ToUniversalTime().ToString("o"));
        command.Parameters.AddWithValue("@DownloadSpeedMbps", record.DownloadSpeedMbps);
        command.Parameters.AddWithValue("@UploadSpeedMbps", record.UploadSpeedMbps);
        command.Parameters.AddWithValue("@PingMs", record.PingMs);
        command.Parameters.AddWithValue("@JitterMs", record.JitterMs);
        command.Parameters.AddWithValue("@ServerName", record.ServerName);
        command.Parameters.AddWithValue("@NetworkName", record.NetworkName);
        command.Parameters.AddWithValue("@ConnectionType", record.ConnectionType);

        record.Id = (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    public async Task<IEnumerable<SpeedTestRecord>> GetSpeedTestsAsync(int count = 50, string? networkName = null)
    {
        var results = new List<SpeedTestRecord>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        string sql = @"
            SELECT Id, Timestamp, DownloadSpeedMbps, UploadSpeedMbps, PingMs, JitterMs, ServerName, NetworkName, ConnectionType
            FROM SpeedTestRecords";
        
        if (!string.IsNullOrEmpty(networkName))
            sql += " WHERE NetworkName = @NetworkName";
            
        sql += " ORDER BY Timestamp DESC LIMIT @Count;";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@Count", count);
        if (!string.IsNullOrEmpty(networkName))
            command.Parameters.AddWithValue("@NetworkName", networkName);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new SpeedTestRecord
            {
                Id                = reader.GetInt64(0),
                Timestamp         = DateTime.Parse(reader.GetString(1)),
                DownloadSpeedMbps = reader.GetDouble(2),
                UploadSpeedMbps   = reader.GetDouble(3),
                PingMs            = reader.GetDouble(4),
                JitterMs          = reader.GetDouble(5),
                ServerName        = reader.GetString(6),
                NetworkName       = reader.GetString(7),
                ConnectionType    = reader.GetString(8)
            });
        }

        return results;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Process Analytics
    // ────────────────────────────────────────────────────────────────────────

    public async Task SaveProcessUsageAsync(ProcessUsageRecord record)
    {
        if (record == null) return;
        if (string.IsNullOrEmpty(record.ProcessName)) return;

        if (record.BytesDownloaded < 0 || record.BytesUploaded < 0)
        {
            System.Diagnostics.Debug.WriteLine($"Rejected invalid process usage record: Negative bytes (DL={record.BytesDownloaded}, UL={record.BytesUploaded}) for process {record.ProcessName}");
            return;
        }
        if (record.Timestamp == default || record.Timestamp.Year < 2000)
        {
            System.Diagnostics.Debug.WriteLine($"Rejected invalid process usage record: Invalid timestamp {record.Timestamp} for process {record.ProcessName}");
            return;
        }
        if (string.IsNullOrEmpty(record.DataSource))
        {
            System.Diagnostics.Debug.WriteLine($"Rejected invalid process usage record: Data source is empty for process {record.ProcessName}");
            return;
        }

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            INSERT INTO ProcessUsageRecords (Timestamp, ProcessName, BytesDownloaded, BytesUploaded, ExecutablePath, UserName, DataSource, Pid, StartTimeTicks)
            VALUES (@Timestamp, @ProcessName, @BytesDownloaded, @BytesUploaded, @ExecutablePath, @UserName, @DataSource, @Pid, @StartTimeTicks);
        ";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@Timestamp", record.Timestamp.ToUniversalTime().ToString("o"));
        command.Parameters.AddWithValue("@ProcessName", record.ProcessName);
        command.Parameters.AddWithValue("@BytesDownloaded", record.BytesDownloaded);
        command.Parameters.AddWithValue("@BytesUploaded", record.BytesUploaded);
        command.Parameters.AddWithValue("@ExecutablePath", record.ExecutablePath ?? string.Empty);
        command.Parameters.AddWithValue("@UserName", record.UserName ?? string.Empty);
        command.Parameters.AddWithValue("@DataSource", record.DataSource ?? "Nethogs");
        command.Parameters.AddWithValue("@Pid", record.Pid);
        command.Parameters.AddWithValue("@StartTimeTicks", record.StartTimeTicks);

        await command.ExecuteNonQueryAsync();
    }

    public async Task SaveProcessUsageBatchAsync(IEnumerable<ProcessUsageRecord> records)
    {
        var recordList = new List<ProcessUsageRecord>();
        foreach (var record in records)
        {
            if (record == null) continue;
            if (string.IsNullOrEmpty(record.ProcessName)) continue;
            if (record.BytesDownloaded < 0 || record.BytesUploaded < 0)
            {
                System.Diagnostics.Debug.WriteLine($"Rejected invalid process usage record: Negative bytes (DL={record.BytesDownloaded}, UL={record.BytesUploaded}) for process {record.ProcessName}");
                continue;
            }
            if (record.Timestamp == default || record.Timestamp.Year < 2000)
            {
                System.Diagnostics.Debug.WriteLine($"Rejected invalid process usage record: Invalid timestamp {record.Timestamp} for process {record.ProcessName}");
                continue;
            }
            if (string.IsNullOrEmpty(record.DataSource))
            {
                System.Diagnostics.Debug.WriteLine($"Rejected invalid process usage record: Data source is empty for process {record.ProcessName}");
                continue;
            }
            recordList.Add(record);
        }

        if (recordList.Count == 0) return;

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        const string sql = @"
            INSERT INTO ProcessUsageRecords (Timestamp, ProcessName, BytesDownloaded, BytesUploaded, ExecutablePath, UserName, DataSource, Pid, StartTimeTicks)
            VALUES (@Timestamp, @ProcessName, @BytesDownloaded, @BytesUploaded, @ExecutablePath, @UserName, @DataSource, @Pid, @StartTimeTicks);
        ";

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        var pTimestamp   = command.Parameters.Add("@Timestamp", SqliteType.Text);
        var pProcess     = command.Parameters.Add("@ProcessName", SqliteType.Text);
        var pDownloaded  = command.Parameters.Add("@BytesDownloaded", SqliteType.Integer);
        var pUploaded    = command.Parameters.Add("@BytesUploaded", SqliteType.Integer);
        var pExecPath    = command.Parameters.Add("@ExecutablePath", SqliteType.Text);
        var pUserName    = command.Parameters.Add("@UserName", SqliteType.Text);
        var pDataSource  = command.Parameters.Add("@DataSource", SqliteType.Text);
        var pPid         = command.Parameters.Add("@Pid", SqliteType.Integer);
        var pStartTime   = command.Parameters.Add("@StartTimeTicks", SqliteType.Integer);

        foreach (var record in recordList)
        {
            pTimestamp.Value   = record.Timestamp.ToUniversalTime().ToString("o");
            pProcess.Value     = record.ProcessName;
            pDownloaded.Value  = record.BytesDownloaded;
            pUploaded.Value    = record.BytesUploaded;
            pExecPath.Value    = record.ExecutablePath ?? string.Empty;
            pUserName.Value    = record.UserName ?? string.Empty;
            pDataSource.Value  = record.DataSource ?? "Nethogs";
            pPid.Value         = record.Pid;
            pStartTime.Value   = record.StartTimeTicks;
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
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
        cmd.Parameters.AddWithValue("@Start", dayStart.ToUniversalTime().ToString("o"));
        cmd.Parameters.AddWithValue("@End", dayEnd.ToUniversalTime().ToString("o"));
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
        cmd.Parameters.AddWithValue("@Start", start.ToUniversalTime().ToString("o"));
        cmd.Parameters.AddWithValue("@End", end.ToUniversalTime().ToString("o"));
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

    public async Task<IEnumerable<HourlyUsageRecord>> GetAllProcessesHourlyUsageAsync(DateTime day)
    {
        var results = new List<HourlyUsageRecord>();
        var dayStart = DateTime.SpecifyKind(day.Date, DateTimeKind.Utc);
        var dayEnd = dayStart.AddDays(1).AddTicks(-1);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        string sql = @"
            SELECT
                CAST(strftime('%H', Timestamp) AS INTEGER) AS Hour,
                SUM(BytesDownloaded) AS TotalDl,
                SUM(BytesUploaded)   AS TotalUl
            FROM ProcessUsageRecords
            WHERE Timestamp >= @Start AND Timestamp <= @End
            GROUP BY Hour ORDER BY Hour ASC;";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@Start", dayStart.ToUniversalTime().ToString("o"));
        cmd.Parameters.AddWithValue("@End", dayEnd.ToUniversalTime().ToString("o"));

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

    public async Task<IEnumerable<DailyUsageRecord>> GetAllProcessesDailyUsageAsync(DateTime start, DateTime end)
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
            WHERE Timestamp >= @Start AND Timestamp <= @End
            GROUP BY date(Timestamp) ORDER BY Day DESC;";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@Start", start.ToUniversalTime().ToString("o"));
        cmd.Parameters.AddWithValue("@End", end.ToUniversalTime().ToString("o"));

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new DailyUsageRecord
            {
                Day = DateTime.SpecifyKind(DateTime.Parse(reader.GetString(0)), DateTimeKind.Utc),
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
                SUM(BytesUploaded)   AS TotalUl,
                MAX(ExecutablePath)  AS ExecPath,
                MAX(UserName)        AS User,
                MAX(DataSource)      AS Source,
                MIN(Timestamp)       AS FirstSeen,
                MAX(Timestamp)       AS LastSeen
            FROM ProcessUsageRecords
            WHERE Timestamp >= @Start AND Timestamp <= @End
            GROUP BY ProcessName
            ORDER BY (SUM(BytesDownloaded) + SUM(BytesUploaded)) DESC
            LIMIT @Limit;";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@Start", start.ToUniversalTime().ToString("o"));
        cmd.Parameters.AddWithValue("@End", end.ToUniversalTime().ToString("o"));
        cmd.Parameters.AddWithValue("@Limit", limit);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new ProcessUsageRecord
            {
                ProcessName = reader.GetString(0),
                BytesDownloaded = reader.GetInt64(1),
                BytesUploaded = reader.GetInt64(2),
                ExecutablePath = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                UserName = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                DataSource = reader.IsDBNull(5) ? "Nethogs" : reader.GetString(5),
                Timestamp = reader.IsDBNull(7) ? DateTime.UtcNow : DateTime.Parse(reader.GetString(7))
            });
        }
        return results;
    }
    // ────────────────────────────────────────────────────────────────────────
    // Network Analytics Methods
    // ────────────────────────────────────────────────────────────────────────

    public async Task<IEnumerable<string>> GetAvailableNetworksAsync()
    {
        var rawNetworks = new List<string>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = "SELECT DISTINCT NetworkName FROM NetworkSessions WHERE NetworkName != '' ORDER BY NetworkName;";
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rawNetworks.Add(reader.GetString(0));
        }

        // Deduplicate and filter out placeholder names when valid SSIDs exist
        var validNetworks = rawNetworks
            .Where(name => NetworkIdentityValidator.IsValidNetworkName(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (validNetworks.Count > 0)
        {
            return validNetworks;
        }

        return rawNetworks
            .Select(name => NetworkIdentityValidator.NormalizeNetworkName(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<NetworkAnalyticsSummary> GetNetworkSummaryAsync(string networkName, DateTime start, DateTime end)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // 1. Get Totals
        string sql = @"
            SELECT 
                COALESCE(SUM(BytesDownloaded), 0),
                COALESCE(SUM(BytesUploaded), 0),
                COALESCE(SUM((julianday(COALESCE(EndTime, datetime('now'))) - julianday(StartTime)) * 24 * 60 * 60), 0),
                COUNT(*),
                MIN(StartTime),
                MAX(StartTime)
            FROM NetworkSessions
            WHERE NetworkName = @NetworkName AND StartTime >= @Start AND StartTime <= @End;";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@NetworkName", networkName);
        command.Parameters.AddWithValue("@Start", start.ToUniversalTime().ToString("o"));
        command.Parameters.AddWithValue("@End", end.ToUniversalTime().ToString("o"));

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            long dl = reader.GetInt64(0);
            long ul = reader.GetInt64(1);
            double seconds = reader.GetDouble(2);
            int count = reader.GetInt32(3);
            DateTime? first = reader.IsDBNull(4) ? null : DateTime.Parse(reader.GetString(4));
            DateTime? last = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5));

            return new NetworkAnalyticsSummary
            {
                TotalDownloaded = dl,
                TotalUploaded = ul,
                TotalConnectionTime = TimeSpan.FromSeconds(seconds),
                TotalSessions = count,
                FirstConnected = first,
                LastConnected = last
            };
        }
        
        return new NetworkAnalyticsSummary();
    }

    public async Task<IEnumerable<DailyUsageRecord>> GetNetworkDailyUsageAsync(string networkName, DateTime start, DateTime end)
    {
        var results = new List<DailyUsageRecord>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        string sql = @"
            SELECT 
                date(StartTime) AS DayStr,
                SUM(BytesDownloaded), 
                SUM(BytesUploaded)
            FROM NetworkSessions
            WHERE NetworkName = @NetworkName AND StartTime >= @Start AND StartTime <= @End
            GROUP BY DayStr
            ORDER BY DayStr ASC;";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@NetworkName", networkName);
        command.Parameters.AddWithValue("@Start", start.ToUniversalTime().ToString("o"));
        command.Parameters.AddWithValue("@End", end.ToUniversalTime().ToString("o"));

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new DailyUsageRecord
            {
                Day = DateTime.Parse(reader.GetString(0)),
                BytesDownloaded = reader.GetInt64(1),
                BytesUploaded = reader.GetInt64(2)
            });
        }
        
        // Fill gaps like we do for standard daily series
        var dictionary = new Dictionary<DateTime, DailyUsageRecord>();
        foreach(var r in results) dictionary[r.Day.Date] = r;
        
        var filled = new List<DailyUsageRecord>();
        for (var date = start.Date; date <= end.Date; date = date.AddDays(1))
        {
            if (dictionary.TryGetValue(date, out var rec))
                filled.Add(rec);
            else
                filled.Add(new DailyUsageRecord { Day = date });
        }
        return filled;
    }

    public async Task<IEnumerable<HourlyUsageRecord>> GetNetworkHourlyUsageAsync(string networkName, DateTime day)
    {
        var results = new List<HourlyUsageRecord>();
        
        var dayStart = day.Date.ToUniversalTime();
        var dayEnd   = dayStart.AddDays(1).AddTicks(-1);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        string sql = @"
            SELECT 
                CAST(strftime('%H', StartTime) AS INTEGER) AS Hour,
                SUM(BytesDownloaded), 
                SUM(BytesUploaded)
            FROM NetworkSessions
            WHERE NetworkName = @NetworkName AND StartTime >= @Start AND StartTime <= @End
            GROUP BY Hour
            ORDER BY Hour ASC;";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@NetworkName", networkName);
        command.Parameters.AddWithValue("@Start", dayStart.ToUniversalTime().ToString("o"));
        command.Parameters.AddWithValue("@End", dayEnd.ToUniversalTime().ToString("o"));

        using var reader = await command.ExecuteReaderAsync();
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

    public async Task<NetworkPerformanceSummary?> GetNetworkPerformanceAsync(string networkName)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        string sql = @"
            SELECT 
                AVG(DownloadSpeedMbps), MAX(DownloadSpeedMbps),
                AVG(UploadSpeedMbps), MAX(UploadSpeedMbps),
                AVG(PingMs), MIN(PingMs),
                COUNT(*)
            FROM SpeedTestRecords
            WHERE NetworkName = @NetworkName;";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@NetworkName", networkName);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync() && !reader.IsDBNull(0))
        {
            return new NetworkPerformanceSummary
            {
                AvgDownloadMbps = reader.GetDouble(0),
                BestDownloadMbps = reader.GetDouble(1),
                AvgUploadMbps = reader.GetDouble(2),
                BestUploadMbps = reader.GetDouble(3),
                AvgPingMs = reader.GetDouble(4),
                BestPingMs = reader.GetDouble(5),
                TotalTests = reader.GetInt32(6)
            };
        }
        return null;
    }

    public async Task<IEnumerable<NetworkComparisonRecord>> GetNetworkComparisonAsync()
    {
        var results = new List<NetworkComparisonRecord>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // Query session aggregates
        string sql = @"
            SELECT 
                NetworkName,
                MAX(ConnectionType) AS ConnType,
                SUM(BytesDownloaded + BytesUploaded) AS TotalUsage,
                SUM((julianday(COALESCE(EndTime, datetime('now'))) - julianday(StartTime)) * 24 * 60 * 60) AS ConnTime,
                COUNT(*) AS Sessions
            FROM NetworkSessions
            WHERE NetworkName != ''
            GROUP BY NetworkName
            ORDER BY TotalUsage DESC;";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new NetworkComparisonRecord
            {
                NetworkName = reader.GetString(0),
                ConnectionType = reader.GetString(1),
                TotalUsage = reader.GetInt64(2),
                TotalConnectionTime = TimeSpan.FromSeconds(reader.GetDouble(3)),
                SessionsCount = reader.GetInt32(4)
            });
        }
        
        // Fetch average speeds for each network
        foreach(var net in results)
        {
            string speedSql = "SELECT AVG(DownloadSpeedMbps), AVG(UploadSpeedMbps) FROM SpeedTestRecords WHERE NetworkName = @Net;";
            using var speedCmd = connection.CreateCommand();
            speedCmd.CommandText = speedSql;
            speedCmd.Parameters.AddWithValue("@Net", net.NetworkName);
            using var speedReader = await speedCmd.ExecuteReaderAsync();
            if (await speedReader.ReadAsync() && !speedReader.IsDBNull(0))
            {
                net.AvgDownloadMbps = speedReader.GetDouble(0);
                net.AvgUploadMbps = speedReader.GetDouble(1);
            }
        }
        
        return results;
    }

    // ────────────────────────────────────────────────────────────────────────
    // App Settings (key-value persistence)
    // ────────────────────────────────────────────────────────────────────────

    public async Task<string?> GetSettingAsync(string key)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = "SELECT Value FROM AppSettings WHERE Key = @Key LIMIT 1;";
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@Key", key);

        var result = await cmd.ExecuteScalarAsync();
        return result is DBNull or null ? null : result.ToString();
    }

    public async Task SaveSettingAsync(string key, string value)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // UPSERT: insert or replace the value for the given key
        const string sql = @"
            INSERT INTO AppSettings (Key, Value) VALUES (@Key, @Value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;";
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@Key",   key);
        cmd.Parameters.AddWithValue("@Value", value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IEnumerable<ProcessUsageRecord>> GetProcessUsageIdentitiesAsync(DateTime start, DateTime end)
    {
        var results = new List<ProcessUsageRecord>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT
                ProcessName,
                Pid,
                StartTimeTicks,
                SUM(BytesDownloaded) AS DownloadBytes,
                SUM(BytesUploaded) AS UploadBytes,
                MAX(ExecutablePath) AS ExecutablePath,
                MAX(UserName) AS UserName,
                MAX(DataSource) AS DataSource,
                MIN(Timestamp) AS FirstSeen,
                MAX(Timestamp) AS LastSeen
            FROM ProcessUsageRecords
            WHERE Timestamp >= @Start AND Timestamp <= @End
            GROUP BY ProcessName, Pid, StartTimeTicks;";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@Start", start.ToUniversalTime().ToString("o"));
        cmd.Parameters.AddWithValue("@End", end.ToUniversalTime().ToString("o"));

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new ProcessUsageRecord
            {
                ProcessName = reader.GetString(0),
                Pid = reader.GetInt32(1),
                StartTimeTicks = reader.GetInt64(2),
                BytesDownloaded = reader.GetInt64(3),
                BytesUploaded = reader.GetInt64(4),
                ExecutablePath = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                UserName = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                DataSource = reader.IsDBNull(7) ? "Nethogs" : reader.GetString(7),
                FirstSeen = reader.IsDBNull(8) ? DateTime.UtcNow : DateTime.Parse(reader.GetString(8)),
                LastSeen = reader.IsDBNull(9) ? DateTime.UtcNow : DateTime.Parse(reader.GetString(9)),
                Timestamp = reader.IsDBNull(8) ? DateTime.UtcNow : DateTime.Parse(reader.GetString(8))
            });
        }
        return results;
    }

    public async Task<IEnumerable<HourlyUsageRecord>> GetProcessIdentityHourlyUsageAsync(string processName, int pid, long startTimeTicks, DateTime day)
    {
        var results = new List<HourlyUsageRecord>();
        var dayStart = day.Date.ToUniversalTime();
        var dayEnd   = dayStart.AddDays(1).AddTicks(-1);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        string sql = @"
            SELECT
                CAST(strftime('%H', Timestamp) AS INTEGER) AS Hour,
                SUM(BytesDownloaded) AS TotalDl,
                SUM(BytesUploaded)   AS TotalUl
            FROM ProcessUsageRecords
            WHERE Timestamp >= @Start AND Timestamp <= @End 
              AND ProcessName = @ProcessName 
              AND Pid = @Pid 
              AND StartTimeTicks = @StartTimeTicks
            GROUP BY Hour ORDER BY Hour ASC;";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@Start", dayStart.ToUniversalTime().ToString("o"));
        cmd.Parameters.AddWithValue("@End", dayEnd.ToUniversalTime().ToString("o"));
        cmd.Parameters.AddWithValue("@ProcessName", processName);
        cmd.Parameters.AddWithValue("@Pid", pid);
        cmd.Parameters.AddWithValue("@StartTimeTicks", startTimeTicks);

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

    public async Task<IEnumerable<DailyUsageRecord>> GetProcessIdentityDailyUsageAsync(string processName, int pid, long startTimeTicks, DateTime start, DateTime end)
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
            WHERE Timestamp >= @Start AND Timestamp <= @End 
              AND ProcessName = @ProcessName 
              AND Pid = @Pid 
              AND StartTimeTicks = @StartTimeTicks
            GROUP BY Day ORDER BY Day ASC;";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@Start", start.ToUniversalTime().ToString("o"));
        cmd.Parameters.AddWithValue("@End", end.ToUniversalTime().ToString("o"));
        cmd.Parameters.AddWithValue("@ProcessName", processName);
        cmd.Parameters.AddWithValue("@Pid", pid);
        cmd.Parameters.AddWithValue("@StartTimeTicks", startTimeTicks);

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

    public async Task<int> GetTotalRecordCountAsync()
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT 
                    (SELECT COUNT(*) FROM NetworkUsageRecords) +
                    (SELECT COUNT(*) FROM NetworkSessions) +
                    (SELECT COUNT(*) FROM ProcessUsageRecords) +
                    (SELECT COUNT(*) FROM SpeedTestRecords);
            ";
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
        catch
        {
            return 0;
        }
    }

    public async Task ClearAllHistoryAsync()
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM NetworkUsageRecords;
                DELETE FROM NetworkSessions;
                DELETE FROM ProcessUsageRecords;
                DELETE FROM SpeedTestRecords;
                VACUUM;
            ";
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }
    }
}
