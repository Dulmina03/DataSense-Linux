using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataSense.Database;
using DataSense.Models;

namespace DataSense.Services;

public class UnifiedAnalyticsIntelligenceService : IUnifiedAnalyticsIntelligenceService
{
    private readonly INetworkUsageRepository _repository;
    private readonly IAnalyticsService _analyticsService;
    private readonly IIntelligenceService _intelligenceService;
    private readonly IApplicationAnalyticsService _appAnalyticsService;
    private readonly IApplicationIntelligenceService _appIntelligenceService;
    private readonly IPatternAnalysisService _patternAnalysisService;
    private readonly IForecastService _forecastService;
    private readonly IEventService _eventService;
    private readonly INetworkMonitorWorker _networkWorker;
    private readonly ISystemHealthRegistry _healthRegistry;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private DateTime _lastUpdate = DateTime.MinValue;
    private UnifiedSystemSummary? _cachedSummary;
    private List<UnifiedInsight>? _cachedInsights;

    public UnifiedAnalyticsIntelligenceService(
        INetworkUsageRepository repository,
        IAnalyticsService analyticsService,
        IIntelligenceService intelligenceService,
        IApplicationAnalyticsService appAnalyticsService,
        IApplicationIntelligenceService appIntelligenceService,
        IPatternAnalysisService patternAnalysisService,
        IForecastService forecastService,
        IEventService eventService,
        INetworkMonitorWorker networkWorker,
        ISystemHealthRegistry healthRegistry)
    {
        _repository = repository;
        _analyticsService = analyticsService;
        _intelligenceService = intelligenceService;
        _appAnalyticsService = appAnalyticsService;
        _appIntelligenceService = appIntelligenceService;
        _patternAnalysisService = patternAnalysisService;
        _forecastService = forecastService;
        _eventService = eventService;
        _networkWorker = networkWorker;
        _healthRegistry = healthRegistry;
    }

    public void InvalidateCache()
    {
        _lastUpdate = DateTime.MinValue;
    }

    private async Task EnsureDataLoadedAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (DateTime.UtcNow - _lastUpdate < TimeSpan.FromMinutes(5) && _cachedSummary != null && _cachedInsights != null)
            {
                return;
            }

            var summary = new UnifiedSystemSummary();
            var insights = new List<UnifiedInsight>();

            bool isHealthy = true;
            try
            {
                // 1. Current System Status
                var netSummary = await _repository.GetTodaySummaryAsync();
                summary.TodayTotalBytes = netSummary.BytesDownloaded + netSummary.BytesUploaded;
                summary.CurrentNetwork = _networkWorker.ActiveInterface ?? "Disconnected";

                var activeNetworkSummary = await _repository.GetNetworkSummaryAsync(summary.CurrentNetwork, DateTime.UtcNow.Date, DateTime.UtcNow.AddDays(1));
                if (activeNetworkSummary != null)
                {
                    summary.TodayTopNetwork = summary.CurrentNetwork;
                }

                var topApps = (await _repository.GetTopProcessesAsync(DateTime.UtcNow.Date, DateTime.UtcNow.AddDays(1), 1)).ToList();
                if (topApps.Any())
                {
                    summary.TodayTopApplication = topApps.First().ProcessName;
                }

                // 2. Fetch dependencies
                var appProfiles = (await _appAnalyticsService.GetTopApplicationsAsync(20)).ToList();
                var anomalies = await _patternAnalysisService.DetectAnomaliesAsync();
                var forecast = await _forecastService.GetForecastAsync();

                // Build Budget state
                var monthSummary = await _repository.GetMonthSummaryAsync();
                long monthTotal = monthSummary.BytesDownloaded + monthSummary.BytesUploaded;
                var budgetResult = await _forecastService.GetBudgetResultAsync(monthTotal, summary.TodayTotalBytes, forecast.AverageDailyUsageBytes);

                if (budgetResult != null)
                {
                    summary.BudgetStatus = budgetResult.Status == BudgetStatus.Healthy ? SubsystemState.Healthy :
                        (budgetResult.Status == BudgetStatus.Warning ? SubsystemState.Degraded : SubsystemState.Error);
                }

                summary.ForecastStatus = forecast.HasSufficientData ? SubsystemState.Healthy : SubsystemState.Unavailable;
                summary.AnomalyStatus = anomalies.Any(a => a.Severity == AnomalySeverity.Critical) ? SubsystemState.Error : SubsystemState.Healthy;

                // --- CROSS-DOMAIN CORRELATION ---
                var hasSufficientAppHistory = appProfiles.Any(p => p.HasSufficientData);

                // A. APPLICATION + NETWORK
                if (summary.TodayTopApplication != "None" && summary.TodayTopNetwork != "None" && summary.TodayTotalBytes > 0)
                {
                    var topApp = topApps.First();
                    double appShare = (double)topApp.TotalBytes / summary.TodayTotalBytes * 100.0;
                    
                    if (appShare > 20)
                    {
                        insights.Add(new UnifiedInsight
                        {
                            Title = "Heavy Application on Current Network",
                            Description = $"{topApp.ProcessName} generated {appShare:F0}% of today's traffic while connected to {summary.CurrentNetwork}.",
                            Category = UnifiedInsightCategory.CrossDomain,
                            Severity = UnifiedInsightSeverity.Info,
                            Confidence = UnifiedInsightConfidence.High,
                            RelatedApplication = topApp.ProcessName,
                            RelatedNetwork = summary.CurrentNetwork,
                            CurrentValue = appShare,
                            RecommendedAction = "No action required.",
                            Evidence = { $"Application generated {topApp.TotalBytes} bytes out of {summary.TodayTotalBytes} total today." },
                            Priority = 7
                        });
                    }
                }

                // B. APPLICATION + ANOMALY
                foreach (var app in appProfiles.Where(p => p.IsUsageSurging && p.SurgePercentage.HasValue && p.HasSufficientData))
                {
                    insights.Add(new UnifiedInsight
                    {
                        Title = "Application Usage Spike",
                        Description = $"{app.ProcessName} usage is {app.SurgePercentage:F0}% above its normal historical pattern.",
                        Category = UnifiedInsightCategory.Anomaly,
                        Severity = UnifiedInsightSeverity.Warning,
                        Confidence = UnifiedInsightConfidence.High,
                        RelatedApplication = app.ProcessName,
                        PercentageChange = app.SurgePercentage,
                        RecommendedAction = "Check if this background usage is expected.",
                        Evidence = { $"Current 7-day average: {app.SevenDayAverageBytes} bytes", $"Change: +{app.SurgePercentage:F0}%" },
                        Priority = 3
                    });
                }

                // C. APPLICATION + FORECAST
                if (summary.TodayTotalBytes > 0 && appProfiles.Count > 0)
                {
                    var topApp = appProfiles.First();
                    if (topApp.IsIncreasing && topApp.HasSufficientData)
                    {
                        double share = (double)topApp.TotalBytes / summary.TodayTotalBytes * 100.0;
                        if (share > 15)
                        {
                            insights.Add(new UnifiedInsight
                            {
                                Title = "App Driving Future Usage",
                                Description = $"{topApp.ProcessName} is currently responsible for {share:F0}% of usage and is trending upward.",
                                Category = UnifiedInsightCategory.Forecast,
                                Severity = UnifiedInsightSeverity.Info,
                                Confidence = UnifiedInsightConfidence.Medium,
                                RelatedApplication = topApp.ProcessName,
                                CurrentValue = share,
                                RecommendedAction = "Monitor this app if you have strict data limits.",
                                Evidence = { $"App share: {share:F0}%", $"Trend: {topApp.TrendState}" },
                                Priority = 6
                            });
                        }
                    }
                }

                // F. BUDGET + APPLICATION
                if (budgetResult != null && (budgetResult.Status == BudgetStatus.Warning || budgetResult.Status == BudgetStatus.Critical) && appProfiles.Any())
                {
                    var heavyApp = appProfiles.First();
                    if (heavyApp.TotalBytes > 0 && summary.TodayTotalBytes > 0)
                    {
                        double share = (double)heavyApp.TotalBytes / summary.TodayTotalBytes * 100.0;
                        if (share > 10)
                        {
                            insights.Add(new UnifiedInsight
                            {
                                Title = "Budget Risk from Application",
                                Description = $"{heavyApp.ProcessName} accounts for {share:F0}% of current usage while your monthly budget is approaching its warning threshold.",
                                Category = UnifiedInsightCategory.Budget,
                                Severity = budgetResult.Status == BudgetStatus.Critical ? UnifiedInsightSeverity.Critical : UnifiedInsightSeverity.Warning,
                                Confidence = UnifiedInsightConfidence.High,
                                RelatedApplication = heavyApp.ProcessName,
                                RecommendedAction = "Consider closing this app or disabling background data.",
                                Evidence = { $"Budget used: {budgetResult.UsedPercent:F0}%", $"App share: {share:F0}%" },
                                Priority = 1
                            });
                        }
                    }
                }

                // G. FORECAST + BUDGET
                if (budgetResult != null && forecast.HasSufficientData && forecast.ProjectedMonthEndBytes > budgetResult.LimitBytes && budgetResult.LimitBytes > 0)
                {
                    insights.Add(new UnifiedInsight
                    {
                        Title = "Forecast Exceeds Budget",
                        Description = "Projected usage is above the configured monthly allowance.",
                        Category = UnifiedInsightCategory.Forecast,
                        Severity = UnifiedInsightSeverity.Warning,
                        Confidence = UnifiedInsightConfidence.High,
                        RecommendedAction = "Reduce daily usage to stay within budget.",
                        Evidence = { $"Projected: {forecast.ProjectedMonthEndBytes} bytes", $"Limit: {budgetResult.LimitBytes} bytes" },
                        Priority = 2
                    });
                }

                // Base Network Insight
                var netInsights = await _intelligenceService.GenerateInsightsAsync(AnalyticsPeriod.Today, summary.CurrentNetwork);
                foreach (var ni in netInsights)
                {
                    insights.Add(new UnifiedInsight
                    {
                        Title = ni.Title,
                        Description = ni.Description,
                        Category = UnifiedInsightCategory.Network,
                        Severity = ni.Severity == InsightSeverity.Info ? UnifiedInsightSeverity.Info : UnifiedInsightSeverity.Warning,
                        Confidence = UnifiedInsightConfidence.Medium,
                        RelatedNetwork = summary.CurrentNetwork,
                        RecommendedAction = "No action required.",
                        Priority = 8
                    });
                }

                summary.OverallIntelligenceStatus = SubsystemState.Healthy;

                // Push Warning/Critical events to EventService
                foreach (var insight in insights.Where(i => i.Severity == UnifiedInsightSeverity.Warning || i.Severity == UnifiedInsightSeverity.Critical))
                {
                    var dataSenseEvent = new DataSenseEvent
                    {
                        Title = insight.Title,
                        Description = insight.Description,
                        Severity = insight.Severity == UnifiedInsightSeverity.Critical ? EventSeverity.Critical : EventSeverity.Warning,
                        Timestamp = insight.Timestamp,
                        Source = "Unified Intelligence",
                        ActionText = insight.RecommendedAction,
                        Fingerprint = $"unified_{insight.Title.Replace(" ", "_").ToLower()}_{insight.Severity}"
                    };
                    _eventService.PublishEvent(dataSenseEvent);
                }

                _healthRegistry.ReportHealth("UnifiedIntelligence", SubsystemState.Healthy, "Unified Intelligence active");
            }
            catch (Exception ex)
            {
                isHealthy = false;
                summary.OverallIntelligenceStatus = SubsystemState.Error;
                _healthRegistry.ReportHealth("UnifiedIntelligence", SubsystemState.Error, "Intelligence Error", ex);
            }

            if (insights.Count == 0 && isHealthy)
            {
                insights.Add(new UnifiedInsight
                {
                    Title = "System Normal",
                    Description = "No significant anomalies or correlations detected across your applications or networks.",
                    Category = UnifiedInsightCategory.CrossDomain,
                    Severity = UnifiedInsightSeverity.Success,
                    Confidence = UnifiedInsightConfidence.Medium,
                    Priority = 100
                });
            }

            _cachedSummary = summary;
            _cachedInsights = insights.OrderBy(i => i.Priority).ThenByDescending(i => i.Timestamp).ToList();
            _lastUpdate = DateTime.UtcNow;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<UnifiedSystemSummary> GetSystemSummaryAsync()
    {
        await EnsureDataLoadedAsync();
        return _cachedSummary ?? new UnifiedSystemSummary { OverallIntelligenceStatus = SubsystemState.Error };
    }

    public async Task<IEnumerable<UnifiedInsight>> GetUnifiedInsightsAsync()
    {
        await EnsureDataLoadedAsync();
        return _cachedInsights ?? Enumerable.Empty<UnifiedInsight>();
    }
}
