using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;

namespace DataSense.Services;

public class LinuxProcessResolver : ILinuxProcessResolver
{
    private static readonly ConcurrentDictionary<int, string> UidCache = new();

    public ProcessIdentityInfo? ResolveProcessIdentity(int pid)
    {
        if (pid <= 0) return null;

        string procDir = $"/proc/{pid}";
        if (!Directory.Exists(procDir)) return null;

        try
        {
            string processName = ReadProcessName(pid);
            string exePath = ReadExecutablePath(pid);
            string cmdLine = ReadCommandLine(pid);
            string userName = ReadUserName(pid);
            long startTimeTicks = ReadStartTimeTicks(pid);

            if (string.IsNullOrWhiteSpace(processName))
            {
                processName = !string.IsNullOrWhiteSpace(exePath)
                    ? Path.GetFileName(exePath)
                    : $"pid_{pid}";
            }

            return new ProcessIdentityInfo
            {
                Pid = pid,
                ProcessName = processName,
                ExecutablePath = exePath,
                CommandLine = cmdLine,
                UserName = userName,
                StartTimeTicks = startTimeTicks
            };
        }
        catch
        {
            // Process disappeared or permission denied; return minimal fallback identity
            return new ProcessIdentityInfo
            {
                Pid = pid,
                ProcessName = $"pid_{pid}",
                ExecutablePath = string.Empty,
                CommandLine = string.Empty,
                UserName = "unknown",
                StartTimeTicks = 0
            };
        }
    }

    private static string ReadProcessName(int pid)
    {
        try
        {
            string commPath = $"/proc/{pid}/comm";
            if (File.Exists(commPath))
            {
                string comm = File.ReadAllText(commPath).Trim();
                if (!string.IsNullOrWhiteSpace(comm)) return comm;
            }
        }
        catch { }
        return string.Empty;
    }

    private static string ReadExecutablePath(int pid)
    {
        try
        {
            string exeLink = $"/proc/{pid}/exe";
            if (File.Exists(exeLink))
            {
                var linkInfo = File.ResolveLinkTarget(exeLink, returnFinalTarget: true);
                if (linkInfo != null) return linkInfo.FullName;
            }
        }
        catch { }
        return string.Empty;
    }

    private static string ReadCommandLine(int pid)
    {
        try
        {
            string cmdPath = $"/proc/{pid}/cmdline";
            if (File.Exists(cmdPath))
            {
                byte[] bytes = File.ReadAllBytes(cmdPath);
                if (bytes.Length > 0)
                {
                    // Replace null bytes with space
                    for (int i = 0; i < bytes.Length; i++)
                    {
                        if (bytes[i] == 0) bytes[i] = (byte)' ';
                    }
                    string cmd = System.Text.Encoding.UTF8.GetString(bytes).Trim();
                    // Limit string length to max 256 chars for security & performance
                    return cmd.Length > 256 ? cmd.Substring(0, 256) : cmd;
                }
            }
        }
        catch { }
        return string.Empty;
    }

    private static string ReadUserName(int pid)
    {
        try
        {
            string statusPath = $"/proc/{pid}/status";
            if (File.Exists(statusPath))
            {
                var lines = File.ReadAllLines(statusPath);
                foreach (var line in lines)
                {
                    if (line.StartsWith("Uid:", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (parts.Length > 1 && int.TryParse(parts[1], out int realUid))
                        {
                            return GetUserNameFromUidStatic(realUid);
                        }
                    }
                }
            }
        }
        catch { }
        return "unknown";
    }

    private static long ReadStartTimeTicks(int pid)
    {
        try
        {
            string statPath = $"/proc/{pid}/stat";
            if (File.Exists(statPath))
            {
                string content = File.ReadAllText(statPath);
                // /proc/[pid]/stat format: 22nd field is starttime
                // Note: command name in field 2 can contain spaces and parentheses e.g. (code --type=...)
                int lastParen = content.LastIndexOf(')');
                if (lastParen != -1 && lastParen + 2 < content.Length)
                {
                    string rest = content.Substring(lastParen + 2);
                    var fields = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    // In fields after ')', field 0 is state (3rd in raw stat), field 19 is starttime (22nd in raw stat)
                    if (fields.Length > 19 && long.TryParse(fields[19], out long starttime))
                    {
                        return starttime;
                    }
                }
            }
        }
        catch { }
        return 0;
    }

    public string GetUserNameFromUid(int uid) => GetUserNameFromUidStatic(uid);

    private static string GetUserNameFromUidStatic(int uid)
    {
        if (UidCache.TryGetValue(uid, out string? cachedName))
        {
            return cachedName;
        }

        string resolved = "unknown";
        try
        {
            const string passwdPath = "/etc/passwd";
            if (File.Exists(passwdPath))
            {
                var lines = File.ReadAllLines(passwdPath);
                foreach (var line in lines)
                {
                    if (line.StartsWith("#")) continue;
                    var parts = line.Split(':');
                    if (parts.Length >= 3 && int.TryParse(parts[2], out int parsedUid) && parsedUid == uid)
                    {
                        resolved = parts[0];
                        break;
                    }
                }
            }
        }
        catch { }

        UidCache[uid] = resolved;
        return resolved;
    }
}
