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
}
