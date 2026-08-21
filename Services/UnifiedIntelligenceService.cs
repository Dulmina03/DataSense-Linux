using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Database;
using DataSense.Models;

namespace DataSense.Services;

public class UnifiedIntelligenceService : IUnifiedIntelligenceService
{
    private readonly IIntelligenceService _intelligenceService;
    private readonly IPatternAnalysisService _patternAnalysisService;
    private readonly IForecastService _forecastService;
    private readonly IApplicationIntelligenceService _appIntelligenceService;
    private readonly INetworkMonitorWorker _networkWorker;
    private readonly ProcessNetworkMonitorWorker _processWorker;
    private readonly INetworkUsageRepository _repository;
    private readonly NetworkSessionManager _sessionManager;

    public UnifiedIntelligenceService(
        IIntelligenceService intelligenceService,
        IPatternAnalysisService patternAnalysisService,
        IForecastService forecastService,
        IApplicationIntelligenceService appIntelligenceService,
        INetworkMonitorWorker networkWorker,
        ProcessNetworkMonitorWorker processWorker,
        INetworkUsageRepository repository,
        NetworkSessionManager sessionManager)
    {
        _intelligenceService    = intelligenceService    ?? throw new ArgumentNullException(nameof(intelligenceService));
        _patternAnalysisService = patternAnalysisService ?? throw new ArgumentNullException(nameof(patternAnalysisService));
        _forecastService        = forecastService        ?? throw new ArgumentNullException(nameof(forecastService));
        _appIntelligenceService = appIntelligenceService ?? throw new ArgumentNullException(nameof(appIntelligenceService));
        _networkWorker          = networkWorker          ?? throw new ArgumentNullException(nameof(networkWorker));
        _processWorker          = processWorker          ?? throw new ArgumentNullException(nameof(processWorker));
        _repository             = repository             ?? throw new ArgumentNullException(nameof(repository));
        _sessionManager         = sessionManager         ?? throw new ArgumentNullException(nameof(sessionManager));
    }

    public async Task<IEnumerable<IntelligenceEvent>> GetUnifiedEventsAsync(int limit = 10)
    {
        var events = new List<IntelligenceEvent>();

        try
        {
            // 1. Anomalies
            var anomalies = await _patternAnalysisService.DetectAnomaliesAsync();
            foreach (var anomaly in anomalies)
            {
                events.Add(new IntelligenceEvent
                {
                    Title          = anomaly.Title,
                    Description    = anomaly.Description,
                    Priority       = anomaly.Severity switch
                    {
                        AnomalySeverity.Critical => IntelligencePriority.Critical,
                        AnomalySeverity.Warning  => IntelligencePriority.High,
                        _                        => IntelligencePriority.Medium
                    },
                    Type           = IntelligenceEventType.Anomaly,
                    Timestamp      = anomaly.Timestamp,
                    ActionableStep = "Inspect system activity during peak window or review application usage."
                });
            }

            // 2. Budget & Forecast Events
            var forecast = await _forecastService.GetForecastAsync();
            if (forecast.HasSufficientData)
            {
                var todaySummary  = await _repository.GetTodaySummaryAsync();
                var monthSummary  = await _repository.GetMonthSummaryAsync();
                long todayTotal   = todaySummary.BytesDownloaded + todaySummary.BytesUploaded;
                long monthTotal   = monthSummary.BytesDownloaded + monthSummary.BytesUploaded;
                var budget = await _forecastService.GetBudgetResultAsync(monthTotal, todayTotal, forecast.AverageDailyUsageBytes);

                if (budget != null && budget.Status != BudgetStatus.Healthy)
                {
                    var exhaustionDesc = budget.EstimatedExhaustionDate.HasValue
                        ? $"Budget projected to be exhausted by {budget.EstimatedExhaustionDate.Value:MMM d}. Used {budget.UsedPercent:F1}% of monthly limit."
                        : $"Monthly data usage is at {budget.UsedPercent:F1}% of limit.";

                    events.Add(new IntelligenceEvent
                    {
                        Title          = budget.StatusLabel,
                        Description    = exhaustionDesc,
                        Priority       = budget.Status == BudgetStatus.Critical ? IntelligencePriority.Critical : IntelligencePriority.High,
                        Type           = IntelligenceEventType.Budget,
                        Percentage     = budget.UsedPercent,
                        ActionableStep = "Consider adjusting daily data budget limits in Settings."
                    });
                }
            }

            // 3. Application Intelligence Recommendations
            var appRecs = await _appIntelligenceService.GenerateApplicationRecommendationsAsync();
            foreach (var rec in appRecs)
            {
                events.Add(new IntelligenceEvent
                {
                    Title           = rec.Title,
                    Description     = rec.Description,
                    Priority        = rec.Impact switch
                    {
                        RecommendationImpact.Critical => IntelligencePriority.Critical,
                        RecommendationImpact.High     => IntelligencePriority.High,
                        RecommendationImpact.Medium   => IntelligencePriority.Medium,
                        _                             => IntelligencePriority.Low
                    },
                    Type            = IntelligenceEventType.Application,
                    ApplicationName = rec.ProcessName,
                    Timestamp       = rec.Timestamp,
                    ActionableStep  = rec.ActionableStep
                });
            }

            // 4. Network Insights
            var netInsights = await _intelligenceService.GenerateInsightsAsync(AnalyticsPeriod.Today, _networkWorker.ActiveInterface ?? "Ethernet");
            foreach (var insight in netInsights)
            {
                events.Add(new IntelligenceEvent
                {
                    Title       = insight.Title,
                    Description = insight.Description,
                    Priority    = insight.Severity switch
                    {
                        InsightSeverity.Warning => IntelligencePriority.High,
                        InsightSeverity.Info    => IntelligencePriority.Medium,
                        _                       => IntelligencePriority.Low
                    },
                    Type        = IntelligenceEventType.Network,
                    Timestamp   = DateTime.UtcNow
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to aggregate unified intelligence: {ex.Message}");
        }

        return events
            .GroupBy(e => e.Title)
            .Select(g => g.First())
            .OrderByDescending(e => e.Priority)
            .ThenByDescending(e => e.Timestamp)
            .Take(limit);
    }

    public async Task<DataSenseHealthModel> GetDataSenseHealthAsync()
    {
        bool isNetActive  = _networkWorker.IsRunning;
        bool isProcActive = _processWorker.IsRunning;
        bool isDbAccessible = true;
        long recordCount = 0;

        try
        {
            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var dailyRecords = await _repository.GetDailyUsageAsync(monthStart, DateTime.UtcNow);
            recordCount = dailyRecords.Count();
        }
        catch
        {
            isDbAccessible = false;
        }

        var forecast = await _forecastService.GetForecastAsync();
        bool hasSuff = forecast.HasSufficientData;
        string activeSession = _networkWorker.ActiveInterface ?? "Active Network";

        DataSenseHealthStatus status;
        string summaryText;

        if (!isNetActive || !isDbAccessible)
        {
            status = DataSenseHealthStatus.Degraded;
            summaryText = "Background monitoring worker or SQLite repository encountered operational warnings.";
        }
        else if (!hasSuff)
        {
            status = DataSenseHealthStatus.CollectingTelemetry;
            summaryText = "DataSense background services are running normally. Currently establishing baseline telemetry (requires 3+ days).";
        }
        else if (isProcActive)
        {
            status = DataSenseHealthStatus.Optimal;
            summaryText = "All background network workers, per-process monitors, SQLite persistence, and intelligence services are operating at peak efficiency.";
        }
        else
        {
            status = DataSenseHealthStatus.Operational;
            summaryText = "Core network monitoring and database persistence are operational.";
        }

        return new DataSenseHealthModel
        {
            Status                  = status,
            IsNetworkWorkerActive   = isNetActive,
            IsProcessWorkerActive   = isProcActive,
            IsDatabaseAccessible    = isDbAccessible,
            DatabaseRecordCount     = recordCount,
            ActiveSessionName       = activeSession,
            HasSufficientTelemetry  = hasSuff,
            OperationalSummary      = summaryText,
            LastChecked             = DateTime.UtcNow
        };
    }
}
