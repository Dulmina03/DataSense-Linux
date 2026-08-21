using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DataSense.Services;

public class LinuxStorageService : ILinuxStorageService
{
    private static readonly SemaphoreSlim LogLock = new(1, 1);
    private const long MaxLogSizeBytes = 5 * 1024 * 1024; // 5 MB
    private const int MaxLogArchives = 3;

    public string ApplicationDataDirectory { get; }
    public string ConfigDirectory { get; }
    public string CacheDirectory { get; }
    public string DatabasePath { get; }
    public string BackupDirectory { get; }
    public string ExportDirectory { get; }
    public string LogDirectory { get; }
    public string AutostartDirectory { get; }

    public LinuxStorageService()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string? xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        ApplicationDataDirectory = !string.IsNullOrWhiteSpace(xdgData)
            ? Path.Combine(xdgData, "DataSense")
            : Path.Combine(home, ".local", "share", "DataSense");

        string? xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        ConfigDirectory = !string.IsNullOrWhiteSpace(xdgConfig)
            ? Path.Combine(xdgConfig, "DataSense")
            : Path.Combine(home, ".config", "DataSense");

        string? xdgCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        CacheDirectory = !string.IsNullOrWhiteSpace(xdgCache)
            ? Path.Combine(xdgCache, "DataSense")
            : Path.Combine(home, ".cache", "DataSense");

        AutostartDirectory = Path.Combine(home, ".config", "autostart");
        BackupDirectory = Path.Combine(ApplicationDataDirectory, "backups");
        ExportDirectory = Path.Combine(ApplicationDataDirectory, "exports");
        LogDirectory = Path.Combine(ApplicationDataDirectory, "logs");

        // Preserve legacy location if existing database file exists in LocalApplicationData
        string legacyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DataSense", "datasense.db");
        string defaultPath = Path.Combine(ApplicationDataDirectory, "datasense.db");

        if (File.Exists(legacyPath))
        {
            DatabasePath = legacyPath;
        }
        else
        {
            DatabasePath = defaultPath;
        }
    }

    public async Task EnsureDirectoriesCreatedAsync()
    {
        await Task.Run(() =>
        {
            Directory.CreateDirectory(ApplicationDataDirectory);
            Directory.CreateDirectory(ConfigDirectory);
            Directory.CreateDirectory(CacheDirectory);
            Directory.CreateDirectory(AutostartDirectory);
            Directory.CreateDirectory(BackupDirectory);
            Directory.CreateDirectory(ExportDirectory);
            Directory.CreateDirectory(LogDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        });
    }

    public async Task LogAsync(string message, string level = "INFO")
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        string sanitized = FilterSensitiveData(message);
        string entry = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [{level.ToUpperInvariant()}] {sanitized}";

        await LogLock.WaitAsync();
        try
        {
            Directory.CreateDirectory(LogDirectory);
            string logFilePath = Path.Combine(LogDirectory, "datasense.log");

            // Perform safe log rotation if file exceeds 5 MB
            if (File.Exists(logFilePath))
            {
                var fi = new FileInfo(logFilePath);
                if (fi.Length >= MaxLogSizeBytes)
                {
                    RotateLogFiles(logFilePath);
                }
            }

            await File.AppendAllTextAsync(logFilePath, entry + Environment.NewLine);
        }
        catch
        {
            // Logging is best-effort; avoid throwing to caller
        }
        finally
        {
            LogLock.Release();
        }
    }

    public string SanitizePath(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath)) return string.Empty;

        // Strip dangerous path traversal segments
        string clean = inputPath.Replace('\\', '/');
        clean = Regex.Replace(clean, @"\.\.+", ".");
        return Path.GetFullPath(clean);
    }

    private void RotateLogFiles(string currentLogPath)
    {
        try
        {
            for (int i = MaxLogArchives - 1; i >= 1; i--)
            {
                string oldArchive = Path.Combine(LogDirectory, $"datasense.log.{i}");
                string newArchive = Path.Combine(LogDirectory, $"datasense.log.{i + 1}");

                if (File.Exists(oldArchive))
                {
                    File.Copy(oldArchive, newArchive, overwrite: true);
                }
            }

            string firstArchive = Path.Combine(LogDirectory, "datasense.log.1");
            File.Copy(currentLogPath, firstArchive, overwrite: true);
            File.Delete(currentLogPath);
        }
        catch { /* Best effort rotation */ }
    }

    private static string FilterSensitiveData(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        // Filter out passwords, bearer tokens, or secret patterns
        string filtered = Regex.Replace(text, @"(password|token|secret|key|authorization)=([^&\s]+)", "$1=[REDACTED]", RegexOptions.IgnoreCase);
        filtered = Regex.Replace(filtered, @"Bearer\s+[A-Za-z0-9\-\._~\+\/]+=*", "Bearer [REDACTED]", RegexOptions.IgnoreCase);
        return filtered;
    }
}
