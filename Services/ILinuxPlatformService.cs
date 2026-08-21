using System;
using System.Collections.Generic;

namespace DataSense.Services;

public interface ILinuxPlatformService
{
    bool IsLinux { get; }
    string DistributionName { get; }
    string DistributionVersion { get; }
    string PrettyOsName { get; }
    string DesktopEnvironment { get; }
    bool IsGnome { get; }
    string DisplayServer { get; }
    string Architecture { get; }
    string KernelVersion { get; }
    string DotNetRuntime { get; }
    string ApplicationVersion { get; }

    bool HasSystemdUserSession { get; }
    bool HasNmcli { get; }
    bool HasNethogs { get; }
    bool HasNotifySend { get; }
    bool HasNotificationCapability { get; }

    string GetExecutablePath(string utilityName);
    IReadOnlyDictionary<string, string> GetSystemSummary();
}
