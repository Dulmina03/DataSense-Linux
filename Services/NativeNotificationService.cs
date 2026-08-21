using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using DataSense.Database;
using DataSense.Helpers;
using DataSense.Models;

namespace DataSense.Services;

public class NativeNotificationService : INativeNotificationService
{
    private readonly ILinuxPlatformService _platformService;
    private readonly INetworkUsageRepository _repository;
    private readonly ConcurrentDictionary<string, DateTime> _cooldowns = new();
    private static readonly TimeSpan DefaultCooldown = TimeSpan.FromMinutes(15);

    public NativeNotificationService(ILinuxPlatformService platformService, INetworkUsageRepository repository)
    {
        _platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
        _repository       = repository       ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<bool> ShowNotificationAsync(string title, string message, NotificationUrgency urgency = NotificationUrgency.Normal, string? category = null)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message)) return false;

        // Check global notification toggle
        string? globalSetting = await _repository.GetSettingAsync("EnableDesktopNotifications");
        if (globalSetting != null && bool.TryParse(globalSetting, out bool enabled) && !enabled)
        {
            return false;
        }

        // Check category toggle if provided
        if (!string.IsNullOrEmpty(category))
        {
            string? catSetting = await _repository.GetSettingAsync($"Notify{category}Alerts");
            if (catSetting != null && bool.TryParse(catSetting, out bool catEnabled) && !catEnabled)
            {
                return false;
            }
        }

        if (!_platformService.HasNotifySend)
        {
            return false;
        }

        string notifySendPath = _platformService.GetExecutablePath("notify-send");
        if (string.IsNullOrEmpty(notifySendPath)) return false;

        string urgencyFlag = urgency switch
        {
            NotificationUrgency.Low => "low",
            NotificationUrgency.Critical => "critical",
            _ => "normal"
        };

        string[] args = new[]
        {
            "-u", urgencyFlag,
            "-a", "DataSense",
            title,
            message
        };

        var result = await ProcessExecutionHelper.ExecuteAsync(notifySendPath, args, timeoutMs: 2000);
        return result.Success;
    }

    public async Task HandleEventPublishedAsync(DataSenseEvent evt)
    {
        if (evt == null) return;

        // Only notify on Warning or Critical events by default unless explicit category toggle
        if (evt.Severity != EventSeverity.Warning && evt.Severity != EventSeverity.Critical && evt.Severity != EventSeverity.Success)
        {
            return;
        }

        // Fingerprint cooldown deduplication
        string fp = !string.IsNullOrEmpty(evt.Fingerprint) ? evt.Fingerprint : evt.Title;
        DateTime now = DateTime.UtcNow;

        if (_cooldowns.TryGetValue(fp, out DateTime lastTime))
        {
            if (now - lastTime < DefaultCooldown)
            {
                return; // Cooldown active, suppress desktop toast
            }
        }

        _cooldowns[fp] = now;

        string category = MapEventTypeToCategory(evt.EventType, evt.Source);
        NotificationUrgency urgency = evt.Severity == EventSeverity.Critical
            ? NotificationUrgency.Critical
            : NotificationUrgency.Normal;

        await ShowNotificationAsync(evt.Title, evt.Description, urgency, category);
    }

    private static string MapEventTypeToCategory(DataSenseEventType type, string source)
    {
        if (type == DataSenseEventType.BudgetWarning || type == DataSenseEventType.BudgetCritical) return "Budget";
        if (type == DataSenseEventType.NetworkChanged || type == DataSenseEventType.NetworkAnomaly) return "Network";
        if (type == DataSenseEventType.DiagnosticWarning || type == DataSenseEventType.MonitoringUnavailable || source.Contains("Database", StringComparison.OrdinalIgnoreCase)) return "Diagnostics";
        if (type == DataSenseEventType.BackupCompleted || type == DataSenseEventType.BackupFailed || source.Contains("Backup", StringComparison.OrdinalIgnoreCase)) return "Backup";
        if (type == DataSenseEventType.UsageAnomaly || type == DataSenseEventType.ApplicationAnomaly) return "Anomaly";
        if (type == DataSenseEventType.ProcessMonitorUnavailable || type == DataSenseEventType.ProcessMonitorRecovered
            || type == DataSenseEventType.ProcessMonitorPermissionDenied || type == DataSenseEventType.ProcessMonitorBackendRestarted) return "Diagnostics";

        return "Diagnostics";
    }
}
