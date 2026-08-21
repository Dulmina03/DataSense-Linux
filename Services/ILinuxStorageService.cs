using System.Threading.Tasks;

namespace DataSense.Services;

public interface ILinuxStorageService
{
    string ApplicationDataDirectory { get; }
    string ConfigDirectory { get; }
    string CacheDirectory { get; }
    string DatabasePath { get; }
    string BackupDirectory { get; }
    string ExportDirectory { get; }
    string LogDirectory { get; }
    string AutostartDirectory { get; }

    Task EnsureDirectoriesCreatedAsync();
    Task LogAsync(string message, string level = "INFO");
    string SanitizePath(string inputPath);
}
