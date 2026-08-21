using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DataSense.Helpers;
using DataSense.Models;

namespace DataSense.Services;

public class LinuxCapabilityService : ILinuxCapabilityService
{
    private readonly ILinuxPlatformService _platformService;
    private readonly ILinuxStorageService _storageService;

    public LinuxCapabilityService(ILinuxPlatformService platformService, ILinuxStorageService storageService)
    {
        _platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
        _storageService  = storageService  ?? throw new ArgumentNullException(nameof(storageService));
    }

    public async Task<IReadOnlyList<LinuxCapabilityItem>> AssessCapabilitiesAsync()
    {
        var items = new List<LinuxCapabilityItem>();

        // 1. Core Network Telemetry (/proc/net/dev)
        bool procNetDevExists = File.Exists("/proc/net/dev");
        items.Add(new LinuxCapabilityItem
        {
            CapabilityId = "ProcNetDev",
            DisplayName = "Kernel Traffic Telemetry (/proc/net/dev)",
            Category = "Core Telemetry",
            Status = procNetDevExists ? LinuxCapabilityStatus.Available : LinuxCapabilityStatus.Degraded,
            Explanation = procNetDevExists
                ? "Kernel network interface byte counters readable without root."
                : "/proc/net/dev is missing or restricted.",
            RecommendedAction = procNetDevExists ? "No action required." : "Verify Linux kernel filesystem mounting."
        });

        // 2. NetworkManager Integration (nmcli)
        if (_platformService.HasNmcli)
        {
            string nmcliPath = _platformService.GetExecutablePath("nmcli");
            var result = await ProcessExecutionHelper.ExecuteAsync(nmcliPath, new[] { "general", "status" }, timeoutMs: 1500);

            if (result.Success)
            {
                items.Add(new LinuxCapabilityItem
                {
                    CapabilityId = "NetworkManager",
                    DisplayName = "NetworkManager CLI (nmcli)",
                    Category = "Network Intelligence",
                    Status = LinuxCapabilityStatus.Available,
                    Explanation = "Active connection details and SSID resolution operational.",
                    RecommendedAction = "No action required."
                });
            }
            else
            {
                items.Add(new LinuxCapabilityItem
                {
                    CapabilityId = "NetworkManager",
                    DisplayName = "NetworkManager CLI (nmcli)",
                    Category = "Network Intelligence",
                    Status = LinuxCapabilityStatus.Degraded,
                    Explanation = "nmcli exists but NetworkManager daemon is unreachable or disabled.",
                    RecommendedAction = "Network SSID resolution degraded. Standard byte counter tracking active."
                });
            }
        }
        else
        {
            items.Add(new LinuxCapabilityItem
            {
                CapabilityId = "NetworkManager",
                DisplayName = "NetworkManager CLI (nmcli)",
                Category = "Network Intelligence",
                Status = LinuxCapabilityStatus.Unavailable,
                Explanation = "nmcli utility not installed on system.",
                RecommendedAction = "Optional: Run 'sudo apt install network-manager' to enable SSID resolution."
            });
        }

        // 3. Per-Process Accounting (nethogs)
        var nethogsCap = await AssessNethogsCapabilityAsync();
        items.Add(nethogsCap);

        // 4. Desktop Notifications (notify-send)
        if (_platformService.HasNotifySend)
        {
            items.Add(new LinuxCapabilityItem
            {
                CapabilityId = "DesktopNotifications",
                DisplayName = "Native Desktop Notifications",
                Category = "Desktop Integration",
                Status = LinuxCapabilityStatus.Available,
                Explanation = "notify-send available for OS event alerts.",
                RecommendedAction = "No action required."
            });
        }
        else
        {
            items.Add(new LinuxCapabilityItem
            {
                CapabilityId = "DesktopNotifications",
                DisplayName = "Native Desktop Notifications",
                Category = "Desktop Integration",
                Status = LinuxCapabilityStatus.Unavailable,
                Explanation = "notify-send CLI package not found.",
                RecommendedAction = "Optional: Install 'libnotify-bin' to receive native OS desktop alerts."
            });
        }

        // 5. Systemd User Session
        if (_platformService.HasSystemdUserSession)
        {
            items.Add(new LinuxCapabilityItem
            {
                CapabilityId = "SystemdUserSession",
                DisplayName = "systemd User Session",
                Category = "System Integration",
                Status = LinuxCapabilityStatus.Available,
                Explanation = "systemd user instance detected ($XDG_RUNTIME_DIR/systemd).",
                RecommendedAction = "No action required."
            });
        }
        else
        {
            items.Add(new LinuxCapabilityItem
            {
                CapabilityId = "SystemdUserSession",
                DisplayName = "systemd User Session",
                Category = "System Integration",
                Status = LinuxCapabilityStatus.Unavailable,
                Explanation = "systemd user session unavailable on this desktop environment.",
                RecommendedAction = "XDG autostart will be used for login startup."
            });
        }

        // 6. Desktop Autostart Support
        try
        {
            string autostartDir = _storageService.AutostartDirectory;
            Directory.CreateDirectory(autostartDir);
            items.Add(new LinuxCapabilityItem
            {
                CapabilityId = "DesktopAutostart",
                DisplayName = "XDG Desktop Autostart",
                Category = "Desktop Integration",
                Status = LinuxCapabilityStatus.Available,
                Explanation = $"Writable autostart folder available at {autostartDir}.",
                RecommendedAction = "No action required."
            });
        }
        catch (Exception ex)
        {
            items.Add(new LinuxCapabilityItem
            {
                CapabilityId = "DesktopAutostart",
                DisplayName = "XDG Desktop Autostart",
                Category = "Desktop Integration",
                Status = LinuxCapabilityStatus.Unavailable,
                Explanation = $"Autostart folder inaccessible: {ex.Message}",
                RecommendedAction = "Check permissions for ~/.config/autostart."
            });
        }

        return items;
    }

    public async Task<LinuxCapabilityItem> AssessNethogsCapabilityAsync()
    {
        if (!_platformService.HasNethogs)
        {
            return new LinuxCapabilityItem
            {
                CapabilityId = "NethogsProcessMonitor",
                DisplayName = "Per-Process Traffic Monitor (nethogs)",
                Category = "Process Accounting",
                Status = LinuxCapabilityStatus.Unavailable,
                Explanation = "Install nethogs to enable process-level network accounting.",
                RecommendedAction = "Run: sudo apt install nethogs",
                SetupCommand = "sudo apt install nethogs"
            };
        }

        string nethogsPath = _platformService.GetExecutablePath("nethogs");

        // Test running nethogs briefly with -V (version check) or test raw socket permission
        var versionResult = await ProcessExecutionHelper.ExecuteAsync(nethogsPath, new[] { "-V" }, timeoutMs: 1500);

        // Check if setcap permissions are configured
        var capResult = await ProcessExecutionHelper.ExecuteAsync("/usr/sbin/getcap", new[] { nethogsPath }, timeoutMs: 1500);

        bool hasRawCap = capResult.Success && capResult.StandardOutput.Contains("cap_net_raw");

        if (hasRawCap)
        {
            return new LinuxCapabilityItem
            {
                CapabilityId = "NethogsProcessMonitor",
                DisplayName = "Per-Process Traffic Monitor (nethogs)",
                Category = "Process Accounting",
                Status = LinuxCapabilityStatus.Available,
                Explanation = "nethogs installed with cap_net_raw capabilities.",
                RecommendedAction = "No action required."
            };
        }

        return new LinuxCapabilityItem
        {
            CapabilityId = "NethogsProcessMonitor",
            DisplayName = "Per-Process Traffic Monitor (nethogs)",
            Category = "Process Accounting",
            Status = LinuxCapabilityStatus.RequiresSetup,
            Explanation = "nethogs installed but lacks raw socket capabilities to capture process packets without root.",
            RecommendedAction = $"Grant non-root raw socket capabilities: sudo setcap cap_net_raw,cap_net_admin=eip {nethogsPath}",
            SetupCommand = $"sudo setcap cap_net_raw,cap_net_admin=eip {nethogsPath}"
        };
    }
}
