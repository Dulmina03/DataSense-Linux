using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Database;
using DataSense.Models;

namespace DataSense.Services;

public interface IDiagnosticsService
{
    Task<IEnumerable<DiagnosticComponent>> GetDiagnosticsAsync();
}

public class DiagnosticsService : IDiagnosticsService
{
    private readonly ISystemHealthRegistry _healthRegistry;
    private readonly INetworkUsageRepository _repository;
    private readonly ILinuxPlatformService? _platformService;
    private readonly ILinuxCapabilityService? _capabilityService;
    private readonly ProcessNetworkMonitorWorker? _processMonitorWorker;
    private readonly ILiveMonitoringEngine? _liveMonitoringEngine;

    public DiagnosticsService(
        ISystemHealthRegistry healthRegistry,
        INetworkUsageRepository repository,
        ILinuxPlatformService? platformService = null,
        ILinuxCapabilityService? capabilityService = null,
        ProcessNetworkMonitorWorker? processMonitorWorker = null,
        ILiveMonitoringEngine? liveMonitoringEngine = null)
    {
        _healthRegistry        = healthRegistry        ?? throw new ArgumentNullException(nameof(healthRegistry));
        _repository            = repository            ?? throw new ArgumentNullException(nameof(repository));
        _platformService       = platformService;
        _capabilityService     = capabilityService;
        _processMonitorWorker  = processMonitorWorker;
        _liveMonitoringEngine  = liveMonitoringEngine;
    }

    public async Task<IEnumerable<DiagnosticComponent>> GetDiagnosticsAsync()
    {
        var reports = _healthRegistry.GetAllReports().ToDictionary(r => r.Name, r => r);
        var components = new List<DiagnosticComponent>();

        // 1. Linux Platform System Info Component
        if (_platformService != null)
        {
            var summary = _platformService.GetSystemSummary();
            string sysInfoStr = $"{summary.GetValueOrDefault("Distribution", "Linux")} | {summary.GetValueOrDefault("Desktop Environment", "Desktop")} ({summary.GetValueOrDefault("Display Server", "Display")}) | Kernel {summary.GetValueOrDefault("Kernel", "Unknown")}";

            components.Add(new DiagnosticComponent
            {
                Name = "LinuxPlatform",
                DisplayName = "Linux OS & Environment",
                Category = "Platform & Runtime",
                Status = SubsystemState.Healthy,
                Message = sysInfoStr,
                DetailedMessage = $"Architecture: {summary.GetValueOrDefault("Architecture", "x64")}, Runtime: {summary.GetValueOrDefault(".NET Runtime", ".NET")}, App: v{summary.GetValueOrDefault("DataSense Version", "1.0.0")}",
                IsRequired = true,
                CanRecoverAutomatically = true,
                LastHealthyAt = DateTime.UtcNow,
                RecommendedAction = "No action required."
            });
        }

        // 2. Network Monitor Component
        var netReport = reports.GetValueOrDefault("NetworkMonitor");
        components.Add(new DiagnosticComponent
        {
            Name = "NetworkMonitor",
            DisplayName = "Linux Network Traffic Telemetry",
            Category = "Core Monitoring",
            Status = netReport?.State ?? SubsystemState.Healthy,
            Message = netReport?.Message ?? "Active (/proc/net/dev reader running)",
            DetailedMessage = "Reads cumulative network interface byte counters from /proc/net/dev.",
            IsRequired = true,
            CanRecoverAutomatically = true,
            LastHealthyAt = DateTime.UtcNow,
            RecommendedAction = netReport?.State == SubsystemState.Error ? "Check Linux kernel network interface permissions." : "No action required."
        });

        // 3. Process Monitor Component (nethogs) — Phase 11.27 enhanced
        var procReport = reports.GetValueOrDefault("ProcessMonitor");
        string procMessage = procReport?.Message ?? "Operational";
        string procDetailedMessage = "Per-process network traffic monitoring via nethogs.";
        string procRecommendedAction = "No action required.";

        if (_processMonitorWorker != null)
        {
            var monitor = _processMonitorWorker.Monitor;
            string nethogsPath = monitor.NethogsPath;
            bool isAvailable = await monitor.IsAvailableAsync();
            bool hasPermissions = await monitor.HasPermissionsAsync();
            int activeCount = _processMonitorWorker.TrackedProcessCount;
            int restarts = _processMonitorWorker.RestartAttempts;

            procMessage = $"{_processMonitorWorker.MonitoringStatus}";
            if (activeCount > 0)
            {
                procMessage += $" | Tracking {activeCount} processes";
            }

            procDetailedMessage = $"Executable: {nethogsPath} | Available: {isAvailable} | Permissions: {hasPermissions} | Restarts: {restarts}";
            if (_processMonitorWorker.LastSuccessfulSample.HasValue)
            {
                procDetailedMessage += $" | Last sample: {_processMonitorWorker.LastSuccessfulSample.Value:HH:mm:ss} UTC";
            }
            if (!string.IsNullOrEmpty(_processMonitorWorker.LastError))
            {
                procDetailedMessage += $" | Last error: {_processMonitorWorker.LastError}";
            }

            SubsystemState workerState = SubsystemState.Healthy;
            if (!isAvailable)
            {
                workerState = SubsystemState.Unavailable;
                procRecommendedAction = "Install nethogs: sudo apt install nethogs";
            }
            else if (!hasPermissions)
            {
                workerState = SubsystemState.Degraded;
                procRecommendedAction = "Grant capabilities: sudo setcap cap_net_raw,cap_net_admin=eip $(which nethogs)";
            }
            else if (_processMonitorWorker.MonitoringStatus.Contains("Error"))
            {
                workerState = SubsystemState.Error;
                procRecommendedAction = "Restart the application or check nethogs installation.";
            }
            else if (_processMonitorWorker.MonitoringStatus.Contains("Restarting"))
            {
                workerState = SubsystemState.Degraded;
                procRecommendedAction = "The nethogs backend is restarting. Check syslog for details.";
            }
            else if (_processMonitorWorker.MonitoringStatus == "Paused")
            {
                workerState = SubsystemState.Healthy;
                procRecommendedAction = "Resume monitoring to start capturing process usage.";
            }

            components.Add(new DiagnosticComponent
            {
                Name = "ProcessMonitor",
                DisplayName = "Per-Process Traffic Monitor (nethogs)",
                Category = "Process Analytics",
                Status = workerState,
                Message = procMessage,
                DetailedMessage = procDetailedMessage,
                IsRequired = false,
                CanRecoverAutomatically = true,
                RecommendedAction = procRecommendedAction,
                LastHealthyAt = workerState == SubsystemState.Healthy ? DateTime.UtcNow : (procReport != null && procReport.State == SubsystemState.Healthy ? procReport.LastStatusUpdate : null)
            });
        }
        else
        {
            components.Add(new DiagnosticComponent
            {
                Name = "ProcessMonitor",
                DisplayName = "Per-Process Traffic Monitor (nethogs)",
                Category = "Process Analytics",
                Status = procReport?.State ?? SubsystemState.Healthy,
                Message = procMessage,
                DetailedMessage = procDetailedMessage,
                IsRequired = false,
                CanRecoverAutomatically = true,
                RecommendedAction = procReport?.State != SubsystemState.Healthy
                    ? "Grant CAP_NET_RAW capabilities or run nethogs with sudo permissions."
                    : "No action required.",
                LastHealthyAt = procReport?.State == SubsystemState.Healthy ? procReport.LastStatusUpdate : null
            });
        }

        // 4. SQLite Database Component
        bool dbAccessible = false;
        try
        {
            var summary = await _repository.GetTodaySummaryAsync();
            dbAccessible = true;
        }
        catch { dbAccessible = false; }

        components.Add(new DiagnosticComponent
        {
            Name = "SQLiteDatabase",
            DisplayName = "SQLite Telemetry Persistence",
            Category = "Data & Storage",
            Status = dbAccessible ? SubsystemState.Healthy : SubsystemState.Error,
            Message = dbAccessible ? "Database accessible (WAL mode active)" : "Database inaccessible",
            DetailedMessage = "Local SQLite store located in application app data folder.",
            IsRequired = true,
            CanRecoverAutomatically = true,
            RecommendedAction = dbAccessible ? "No action required." : "Check directory write permissions for local app data."
        });

        // 5. Desktop Integration Capabilities (if service available)
        if (_capabilityService != null)
        {
            var capabilities = await _capabilityService.AssessCapabilitiesAsync();
            foreach (var cap in capabilities)
            {
                SubsystemState status = cap.Status switch
                {
                    LinuxCapabilityStatus.Available => SubsystemState.Healthy,
                    LinuxCapabilityStatus.Degraded => SubsystemState.Degraded,
                    LinuxCapabilityStatus.RequiresSetup => SubsystemState.Degraded,
                    _ => SubsystemState.Unavailable
                };

                components.Add(new DiagnosticComponent
                {
                    Name = cap.CapabilityId,
                    DisplayName = cap.DisplayName,
                    Category = cap.Category,
                    Status = status,
                    Message = cap.Explanation,
                    DetailedMessage = !string.IsNullOrEmpty(cap.SetupCommand) ? $"Setup Command: {cap.SetupCommand}" : cap.Explanation,
                    IsRequired = false,
                    CanRecoverAutomatically = cap.Status == LinuxCapabilityStatus.Available,
                    RecommendedAction = cap.RecommendedAction
                });
            }
        }

        // 6. Forecast & Budget Component
        var forecastReport = reports.GetValueOrDefault("ForecastService");
        components.Add(new DiagnosticComponent
        {
            Name = "ForecastService",
            DisplayName = "Deterministic Usage Forecasting",
            Category = "Analytics & Intelligence",
            Status = forecastReport?.State ?? SubsystemState.Healthy,
            Message = forecastReport?.Message ?? "Operational (EWMA 30-day window)",
            DetailedMessage = "Computes usage pace and projects end-of-month totals based on historical SQLite records.",
            IsRequired = false,
            CanRecoverAutomatically = true,
            RecommendedAction = "Requires at least 3 days of historical records for accurate predictions."
        });

        // 7. Cloudflare Speed Test Component
        var speedReport = reports.GetValueOrDefault("SpeedTest");
        components.Add(new DiagnosticComponent
        {
            Name = "SpeedTest",
            DisplayName = "Cloudflare Speed Test Engine",
            Category = "Network Intelligence",
            Status = speedReport?.State ?? SubsystemState.Healthy,
            Message = speedReport?.Message ?? "Ready (User-initiated only)",
            DetailedMessage = "Performs latency, download, and upload benchmarking against Cloudflare edge servers.",
            IsRequired = false,
            CanRecoverAutomatically = true,
            RecommendedAction = "Verify internet connection if test timeouts occur."
        });

        // 8. Live Traffic Monitoring Engine Component
        int liveSamples = _liveMonitoringEngine?.SampleCount ?? 0;
        bool livePaused = _liveMonitoringEngine?.IsPaused ?? false;
        
        SubsystemState liveState = SubsystemState.Healthy;
        string liveMsg = livePaused ? "Monitoring Paused by user" : $"Active ({liveSamples} live samples in-memory)";
        string liveDetailedMsg = "Maintains bounded in-memory rolling traffic window without writing high-frequency samples to SQLite.";

        if (_liveMonitoringEngine != null)
        {
            var diagInfo = _liveMonitoringEngine.GetDiagnosticsInfo();
            liveState = livePaused ? SubsystemState.Degraded : (diagInfo.MonitorState.Contains("Error") ? SubsystemState.Error : SubsystemState.Healthy);
            liveMsg = livePaused ? "Monitoring Paused by user" : $"Active ({diagInfo.ActiveProcessCount} active apps | Stream: {diagInfo.CurrentStreamStatus})";
            liveDetailedMsg = $"Last live sample: {(diagInfo.LastLiveSampleTimestamp.HasValue ? diagInfo.LastLiveSampleTimestamp.Value.ToLocalTime().ToString("HH:mm:ss") : "Never")} | Monitor: {diagInfo.MonitorState} | Nethogs: {diagInfo.NethogsState} | Restarts: {diagInfo.RestartCount}";
            if (!string.IsNullOrEmpty(diagInfo.LastProcessingError))
            {
                liveDetailedMsg += $" | Error: {diagInfo.LastProcessingError}";
            }
        }

        components.Add(new DiagnosticComponent
        {
            Name = "LiveTrafficMonitoring",
            DisplayName = "Live Network Traffic Intelligence",
            Category = "Real-Time Engine",
            Status = liveState,
            Message = liveMsg,
            DetailedMessage = liveDetailedMsg,
            IsRequired = false,
            CanRecoverAutomatically = true,
            RecommendedAction = livePaused ? "Resume live monitoring from header or tray control." : "No action required."
        });

        // 9. Session Intelligence Component
        var sessionReport = reports.GetValueOrDefault("SessionIntelligence");
        components.Add(new DiagnosticComponent
        {
            Name = "SessionIntelligence",
            DisplayName = "Network Session Intelligence",
            Category = "Analytics & Intelligence",
            Status = sessionReport?.State ?? SubsystemState.Healthy,
            Message = sessionReport?.Message ?? "Operational",
            DetailedMessage = "Analyzes historical network session patterns, calculates process attribution, and detects network switches.",
            IsRequired = false,
            CanRecoverAutomatically = true,
            RecommendedAction = "No action required."
        });

        return components;
    }
}
