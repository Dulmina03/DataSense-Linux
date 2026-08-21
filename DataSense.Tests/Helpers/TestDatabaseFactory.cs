using System;
using System.IO;
using System.Threading.Tasks;
using DataSense.Database;

namespace DataSense.Tests.Helpers;

public class TestDatabaseContext : IDisposable
{
    public string DbPath { get; }
    public SqliteNetworkUsageRepository Repository { get; }

    public TestDatabaseContext(string dbPath, SqliteNetworkUsageRepository repository)
    {
        DbPath = dbPath;
        Repository = repository;
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(DbPath))
            {
                File.Delete(DbPath);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }
}

public static class TestDatabaseFactory
{
    public static async Task<TestDatabaseContext> CreateAsync()
    {
        string tempFolder = Path.Combine(Path.GetTempPath(), "DataSense_Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        string dbPath = Path.Combine(tempFolder, "test_datasense.db");

        // Use Pooling=False to avoid SQLite pool disposal interference between concurrent tests
        string connectionStringPath = $"{dbPath};Pooling=False";
        var repository = new SqliteNetworkUsageRepository(connectionStringPath);
        await repository.InitializeAsync();

        return new TestDatabaseContext(dbPath, repository);
    }
}
