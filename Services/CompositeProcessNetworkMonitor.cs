using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

/// <summary>
/// Composite process network monitor that uses Nethogs when available with elevated capabilities,
/// and automatically falls back to native Linux socket monitoring (`LinuxSocketProcessNetworkMonitor`) when nethogs is uninstalled or lacks root privileges.
/// </summary>
public class CompositeProcessNetworkMonitor : IProcessNetworkMonitor
{
    private readonly NethogsProcessNetworkMonitor _nethogsMonitor;
    private readonly LinuxSocketProcessNetworkMonitor _socketMonitor;

    public CompositeProcessNetworkMonitor(
        ILinuxPlatformService? platformService = null,
        ILinuxProcessResolver? processResolver = null)
    {
        _nethogsMonitor = new NethogsProcessNetworkMonitor(platformService, processResolver);
        _socketMonitor = new LinuxSocketProcessNetworkMonitor(platformService, processResolver);
    }

    public string NethogsPath => _nethogsMonitor.NethogsPath;

    public async Task<bool> IsAvailableAsync()
    {
        if (await _nethogsMonitor.IsAvailableAsync()) return true;
        return await _socketMonitor.IsAvailableAsync();
    }

    public async Task<bool> HasPermissionsAsync()
    {
        if (await _nethogsMonitor.IsAvailableAsync() && await _nethogsMonitor.HasPermissionsAsync())
            return true;

        return await _socketMonitor.HasPermissionsAsync();
    }

    public async IAsyncEnumerable<IEnumerable<ProcessNetworkUsage>> StartMonitoringAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        bool nethogsAvailable = false;
        bool nethogsPermitted = false;

        try
        {
            nethogsAvailable = await _nethogsMonitor.IsAvailableAsync();
            if (nethogsAvailable)
            {
                nethogsPermitted = await _nethogsMonitor.HasPermissionsAsync();
            }
        }
        catch { }

        if (nethogsAvailable && nethogsPermitted)
        {
            System.Diagnostics.Debug.WriteLine("[CompositeProcessNetworkMonitor] Using Nethogs backend.");
            await foreach (var batch in _nethogsMonitor.StartMonitoringAsync(cancellationToken))
            {
                yield return batch;
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[CompositeProcessNetworkMonitor] Using native Linux socket fallback backend.");
            await foreach (var batch in _socketMonitor.StartMonitoringAsync(cancellationToken))
            {
                yield return batch;
            }
        }
    }
}
