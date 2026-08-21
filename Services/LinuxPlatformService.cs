using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace DataSense.Services;

public class LinuxPlatformService : ILinuxPlatformService
{
    private readonly Lazy<Dictionary<string, string>> _osReleaseData;

    public bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public string DistributionName => GetOsReleaseValue("NAME", "Linux");

    public string DistributionVersion => GetOsReleaseValue("VERSION_ID", GetOsReleaseValue("VERSION", "Unknown"));

    public string PrettyOsName => GetOsReleaseValue("PRETTY_NAME", $"{DistributionName} {DistributionVersion}".Trim());

    public string DesktopEnvironment
    {
        get
        {
            string desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")
                ?? Environment.GetEnvironmentVariable("DESKTOP_SESSION")
                ?? "Unknown";
            return desktop;
        }
    }

    public bool IsGnome => DesktopEnvironment.Contains("GNOME", StringComparison.OrdinalIgnoreCase);

    public string DisplayServer
    {
        get
        {
            string sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(sessionType))
            {
                return "Unknown";
            }
            return sessionType.ToLowerInvariant() switch
            {
                "wayland" => "Wayland",
                "x11" => "X11",
                "tty" => "TTY",
                _ => sessionType
            };
        }
    }

    public string Architecture => RuntimeInformation.ProcessArchitecture.ToString();

    public string KernelVersion
    {
        get
        {
            try
            {
                if (File.Exists("/proc/sys/kernel/osrelease"))
                {
                    string kernel = File.ReadAllText("/proc/sys/kernel/osrelease").Trim();
                    if (!string.IsNullOrEmpty(kernel)) return kernel;
                }
            }
            catch { /* fallback below */ }

            return RuntimeInformation.OSDescription;
        }
    }

    public string DotNetRuntime => RuntimeInformation.FrameworkDescription;

    public string ApplicationVersion
    {
        get
        {
            var version = Assembly.GetEntryAssembly()?.GetName().Version;
            return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
        }
    }

    public bool HasSystemdUserSession
    {
        get
        {
            string runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? string.Empty;
            if (!string.IsNullOrEmpty(runtimeDir) && Directory.Exists(Path.Combine(runtimeDir, "systemd")))
            {
                return true;
            }

            int uid = GetCurrentUserId();
            if (uid >= 0 && Directory.Exists($"/run/user/{uid}/systemd"))
            {
                return true;
            }

            return !string.IsNullOrEmpty(GetExecutablePath("systemctl"));
        }
    }

    public bool HasNmcli => !string.IsNullOrEmpty(GetExecutablePath("nmcli"));

    public bool HasNethogs => !string.IsNullOrEmpty(GetExecutablePath("nethogs"));

    public bool HasNotifySend => !string.IsNullOrEmpty(GetExecutablePath("notify-send"));

    public bool HasNotificationCapability => HasNotifySend;

    public LinuxPlatformService()
    {
        _osReleaseData = new Lazy<Dictionary<string, string>>(ParseOsRelease);
    }

    public string GetExecutablePath(string utilityName)
    {
        if (string.IsNullOrWhiteSpace(utilityName)) return string.Empty;

        // Prevent path traversal in utilityName check
        string cleanName = Path.GetFileName(utilityName);
        string[] searchPaths = new[]
        {
            "/usr/bin",
            "/bin",
            "/usr/sbin",
            "/sbin",
            "/usr/local/bin",
            "/usr/local/sbin"
        };

        foreach (var path in searchPaths)
        {
            string fullPath = Path.Combine(path, cleanName);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        // Also check PATH environment variable
        string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string fullPath = Path.Combine(dir, cleanName);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            catch { /* Skip invalid PATH entries */ }
        }

        return string.Empty;
    }

    public IReadOnlyDictionary<string, string> GetSystemSummary()
    {
        return new Dictionary<string, string>
        {
            ["Operating System"] = "Linux",
            ["Distribution"] = PrettyOsName,
            ["Desktop Environment"] = DesktopEnvironment,
            ["Display Server"] = DisplayServer,
            ["Kernel"] = KernelVersion,
            ["Architecture"] = Architecture,
            [".NET Runtime"] = DotNetRuntime,
            ["DataSense Version"] = ApplicationVersion
        };
    }

    private string GetOsReleaseValue(string key, string fallback)
    {
        var data = _osReleaseData.Value;
        return data.TryGetValue(key, out var val) ? val : fallback;
    }

    private static Dictionary<string, string> ParseOsRelease()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string[] releaseFiles = new[] { "/etc/os-release", "/usr/lib/os-release", "/etc/lsb-release" };

        foreach (var file in releaseFiles)
        {
            if (!File.Exists(file)) continue;

            try
            {
                foreach (var line in File.ReadAllLines(file))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
                    int eqIndex = line.IndexOf('=');
                    if (eqIndex <= 0) continue;

                    string key = line.Substring(0, eqIndex).Trim();
                    string value = line.Substring(eqIndex + 1).Trim().Trim('"').Trim('\'');

                    if (!dict.ContainsKey(key))
                    {
                        dict[key] = value;
                    }
                }
                break; // Found and parsed valid release file
            }
            catch { /* Try next file */ }
        }

        return dict;
    }

    private static int GetCurrentUserId()
    {
        try
        {
            string uidStr = Environment.GetEnvironmentVariable("UID") ?? string.Empty;
            if (int.TryParse(uidStr, out int uid)) return uid;
        }
        catch { }
        return -1;
    }
}
