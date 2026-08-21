using System;
using System.IO;
using System.Threading.Tasks;
using DataSense.Services;
using Xunit;

namespace DataSense.Tests.Services;

public class LinuxStorageServiceTests
{
    [Fact]
    public void StorageService_ExposesValidXdgPaths()
    {
        var service = new LinuxStorageService();

        Assert.False(string.IsNullOrWhiteSpace(service.ApplicationDataDirectory));
        Assert.False(string.IsNullOrWhiteSpace(service.ConfigDirectory));
        Assert.False(string.IsNullOrWhiteSpace(service.CacheDirectory));
        Assert.False(string.IsNullOrWhiteSpace(service.DatabasePath));
        Assert.False(string.IsNullOrWhiteSpace(service.BackupDirectory));
        Assert.False(string.IsNullOrWhiteSpace(service.ExportDirectory));
        Assert.False(string.IsNullOrWhiteSpace(service.LogDirectory));
        Assert.False(string.IsNullOrWhiteSpace(service.AutostartDirectory));
    }

    [Fact]
    public async Task StorageService_EnsureDirectoriesCreatedAsync_CreatesDirectories()
    {
        var service = new LinuxStorageService();
        await service.EnsureDirectoriesCreatedAsync();

        Assert.True(Directory.Exists(service.ApplicationDataDirectory));
        Assert.True(Directory.Exists(service.ConfigDirectory));
        Assert.True(Directory.Exists(service.CacheDirectory));
        Assert.True(Directory.Exists(service.LogDirectory));
    }

    [Fact]
    public async Task StorageService_LogAsync_WritesAndFiltersSecrets()
    {
        var service = new LinuxStorageService();
        await service.EnsureDirectoriesCreatedAsync();

        string testMessage = "Test log entry with password=SecretKey123 and Bearer abc123token";
        await service.LogAsync(testMessage, "INFO");

        string logFile = Path.Combine(service.LogDirectory, "datasense.log");
        Assert.True(File.Exists(logFile));

        string content = await File.ReadAllTextAsync(logFile);
        Assert.Contains("[INFO]", content);
        Assert.DoesNotContain("SecretKey123", content);
        Assert.DoesNotContain("abc123token", content);
        Assert.Contains("[REDACTED]", content);
    }
}
