using System;

namespace DataSense.Services;

public class ProcessIdentityInfo
{
    public int Pid { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;
    public string CommandLine { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public long StartTimeTicks { get; init; }
    public string CompositeKey => $"{ProcessName}_{Pid}_{StartTimeTicks}";
}

public interface ILinuxProcessResolver
{
    ProcessIdentityInfo? ResolveProcessIdentity(int pid);
    string GetUserNameFromUid(int uid);
}
