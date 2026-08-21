using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Models;
using DataSense.Services;

namespace DataSense.Services;

public class IntelligenceService : IIntelligenceService
{
    private readonly IAnalyticsService _analytics;

    public IntelligenceService(IAnalyticsService analytics)
    {
        _analytics = analytics;
    }

    public async Task<IEnumerable<NetworkInsight>> GenerateInsightsAsync(AnalyticsPeriod period, string? currentNetworkName)
    {
        var insights = new List<NetworkInsight>();

        try
        {
            // 1. Fetch historical daily series for base calculations
            var dailySeries = await _analytics.GetDailySeriesAsync(AnalyticsPeriod.AllTime);
            var today = DateTime.UtcNow.Date;
            
            var historicalDays = dailySeries.Where(d => d.Day < today).OrderByDescending(d => d.Day).ToList();
            var todayData = dailySeries.FirstOrDefault(d => d.Day == today);
            
            // Check Data Sufficiency
            if (historicalDays.Count < 3)
            {
                insights.Add(new NetworkInsight
                {
                    Type = InsightType.NoData,
                    Severity = InsightSeverity.Info,
                    Title = "Network Intelligence",
                    Description = "DataSense is still learning your normal network usage patterns. Continue using DataSense to build enough history for personalized insights.",
                    Timestamp = DateTime.UtcNow
                });
                return insights; // Return early if not enough data
            }

            // Time normalization factor (how much of the day has passed)
            // If it's early morning, comparing absolute today's data to a full day's average is skewed.
            // We'll require at least 5% of the day to have passed to make meaningful daily comparisons.
            double fractionOfDay = DateTime.UtcNow.TimeOfDay.TotalHours / 24.0;
            if (fractionOfDay < 0.05) fractionOfDay = 0.05; 
            
            var recentDays = historicalDays.Take(7).ToList();
            var avgDailyUsage = recentDays.Average(d => d.TotalBytes);
            var normalizedAvgDailyUsage = avgDailyUsage * fractionOfDay;
            
            // 2. High Usage Detection
            if (todayData != null && todayData.TotalBytes > 0)
            {
                // High Usage: > 50% above normalized average, minimum 100MB
                if (todayData.TotalBytes > normalizedAvgDailyUsage * 1.5 && todayData.TotalBytes > 100_000_000)
                {
                    double pct = (todayData.TotalBytes - avgDailyUsage) / avgDailyUsage * 100;
                    if (pct < 0) pct = (todayData.TotalBytes - normalizedAvgDailyUsage) / normalizedAvgDailyUsage * 100;
                    
                    insights.Add(new NetworkInsight
                    {
                        Type = InsightType.HighUsage,
                        Severity = InsightSeverity.Warning,
                        Title = "High Data Usage",
                        Description = $"You've used {FormatBytes(todayData.TotalBytes)} today, which is {pct:F0}% above your recent average.",
                        PercentageChange = pct,
                        CurrentValue = todayData.TotalBytes,
                        BaselineValue = avgDailyUsage,
                        Timestamp = DateTime.UtcNow
                    });
                }
                // Low Usage: < 50% of normalized average, if late enough in the day
                else if (fractionOfDay > 0.5 && todayData.TotalBytes < normalizedAvgDailyUsage * 0.5 && avgDailyUsage > 100_000_000)
                {
                    double pct = (normalizedAvgDailyUsage - todayData.TotalBytes) / normalizedAvgDailyUsage * 100;
                    insights.Add(new NetworkInsight
                    {
                        Type = InsightType.LowUsage,
                        Severity = InsightSeverity.Info,
                        Title = "Low Usage",
                        Description = $"You've used {pct:F0}% less data today than your recent average.",
                        PercentageChange = -pct,
                        CurrentValue = todayData.TotalBytes,
                        BaselineValue = normalizedAvgDailyUsage,
                        Timestamp = DateTime.UtcNow
                    });
                }

                // Upload Spike
                var avgUpload = recentDays.Average(d => d.BytesUploaded);
                var normalizedAvgUpload = avgUpload * fractionOfDay;
                if (todayData.BytesUploaded > normalizedAvgUpload * 2.0 && todayData.BytesUploaded > 50_000_000)
                {
                    insights.Add(new NetworkInsight
                    {
                        Type = InsightType.UploadSpike,
                        Severity = InsightSeverity.Warning,
                        Title = "Upload Spike",
                        Description = "Today's upload usage is significantly higher than your recent average.",
                        Timestamp = DateTime.UtcNow
                    });
                }
            }

            // 3. Usage Trend Detection
            if (historicalDays.Count >= 3)
            {
                var trendDays = historicalDays.Take(4).Reverse().ToList(); // chronological order
                if (trendDays.Count >= 3)
                {
                    bool increasing = true;
                    bool decreasing = true;
                    
                    for (int i = 0; i < trendDays.Count - 1; i++)
                    {
                        if (trendDays[i].TotalBytes >= trendDays[i + 1].TotalBytes) increasing = false;
                        if (trendDays[i].TotalBytes <= trendDays[i + 1].TotalBytes) decreasing = false;
                    }
                    
                    if (increasing)
                    {
                        insights.Add(new NetworkInsight
                        {
                            Type = InsightType.UsageIncrease,
                            Severity = InsightSeverity.Info,
                            Title = "Usage Increasing",
                            Description = $"Your daily data usage has increased for {trendDays.Count} consecutive days.",
                            Timestamp = DateTime.UtcNow
                        });
                    }
                    else if (decreasing)
                    {
                        insights.Add(new NetworkInsight
                        {
                            Type = InsightType.UsageDecrease,
                            Severity = InsightSeverity.Success,
                            Title = "Usage Decreasing",
                            Description = $"Your daily data usage has decreased for {trendDays.Count} consecutive days.",
                            Timestamp = DateTime.UtcNow
                        });
                    }
                }
            }

            // 4. Top Data Consumer & Application Spike
            var topConsumers = await _analytics.GetTopDataConsumersAsync(AnalyticsPeriod.Today, 1);
            var topConsumer = topConsumers.FirstOrDefault();
            
            if (topConsumer != null && topConsumer.TotalBytes > 50_000_000) // Minimum 50MB
            {
                insights.Add(new NetworkInsight
                {
                    Type = InsightType.TopDataConsumer,
                    Severity = InsightSeverity.Info,
                    Title = "Top Data Consumer",
                    Description = $"{topConsumer.ProcessName} is your largest data consumer today, using {FormatBytes(topConsumer.TotalBytes)}.",
                    ApplicationName = topConsumer.ProcessName,
                    Timestamp = DateTime.UtcNow
                });

                // Application Spike Check
                var procDaily = await _analytics.GetProcessDailySeriesAsync(topConsumer.ProcessName, AnalyticsPeriod.Last7Days);
                var procRecent = procDaily.Where(d => d.Day < today).ToList();
                if (procRecent.Count >= 3)
                {
                    var procAvg = procRecent.Average(d => d.TotalBytes);
                    var procNormalizedAvg = procAvg * fractionOfDay;
                    
                    // Allow 100% margin (2.0x) and minimum 100MB
                    if (topConsumer.TotalBytes > procNormalizedAvg * 2.0 && topConsumer.TotalBytes > 100_000_000)
                    {
                        double spikePct = (topConsumer.TotalBytes - procAvg) / procAvg * 100;
                        if (spikePct < 0) spikePct = (topConsumer.TotalBytes - procNormalizedAvg) / procNormalizedAvg * 100;

                        insights.Add(new NetworkInsight
                        {
                            Type = InsightType.ApplicationSpike,
                            Severity = InsightSeverity.Warning,
                            Title = "Application Usage Spike",
                            Description = $"{topConsumer.ProcessName} has used {spikePct:F0}% more data than its recent daily average.",
                            ApplicationName = topConsumer.ProcessName,
                            Timestamp = DateTime.UtcNow
                        });
                    }
                }
            }

            // 5. Network Insights (Best Network, Most Used)
            var comparisons = (await _analytics.GetNetworkComparisonAsync()).ToList();
            if (comparisons.Count >= 2)
            {
                var bestNetwork = comparisons.Where(c => c.AvgDownloadMbps > 0).OrderByDescending(c => c.AvgDownloadMbps).FirstOrDefault();
                if (bestNetwork != null)
                {
                    insights.Add(new NetworkInsight
                    {
                        Type = InsightType.BestNetwork,
                        Severity = InsightSeverity.Success,
                        Title = "Best Performing Network",
                        Description = $"{bestNetwork.NetworkName} has the highest average download speed at {bestNetwork.AvgDownloadMbps:F1} Mbps.",
                        NetworkName = bestNetwork.NetworkName,
                        Timestamp = DateTime.UtcNow
                    });
                }

                var mostUsed = comparisons.OrderByDescending(c => c.TotalUsage).FirstOrDefault();
                if (mostUsed != null && mostUsed.TotalUsage > 100_000_000)
                {
                    insights.Add(new NetworkInsight
                    {
                        Type = InsightType.FrequentNetwork,
                        Severity = InsightSeverity.Info,
                        Title = "Most Used Network",
                        Description = $"{mostUsed.NetworkName} is currently your most-used network by total data.",
                        NetworkName = mostUsed.NetworkName,
                        Timestamp = DateTime.UtcNow
                    });
                }
            }

            // 6. Current Network Specific Insights
            if (!string.IsNullOrEmpty(currentNetworkName))
            {
                var sessions = await _analytics.GetNetworkSessionsAsync(currentNetworkName, AnalyticsPeriod.Today);
                var longest = sessions.OrderByDescending(s => s.Duration).FirstOrDefault();
                if (longest != null && longest.Duration.TotalHours > 4) // > 4 hours
                {
                    insights.Add(new NetworkInsight
                    {
                        Type = InsightType.LongestSession,
                        Severity = InsightSeverity.Info,
                        Title = "Long Network Session",
                        Description = $"Your current {currentNetworkName} session has lasted {longest.Duration:h\\h\\ m\\m}.",
                        NetworkName = currentNetworkName,
                        Timestamp = DateTime.UtcNow
                    });
                }
            }

            // Deduplicate Insights by Type and Target
            var uniqueInsights = insights
                .GroupBy(i => new { i.Type, i.ApplicationName, i.NetworkName })
                .Select(g => g.First())
                .ToList();

            // Sort by severity (Critical -> Warning -> Info -> Success)
            return uniqueInsights.OrderByDescending(i => i.Severity);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error generating insights: {ex}");
            return Array.Empty<NetworkInsight>();
        }
    }

    // ── Budget-aware insights ────────────────────────────────────────────────

    public async Task<IEnumerable<NetworkInsight>> GenerateInsightsWithBudgetAsync(
        AnalyticsPeriod period,
        string?        currentNetworkName,
        BudgetResult?  budgetResult,
        UsageForecast? forecast)
    {
        // Start with all standard insights
        var insights = (await GenerateInsightsAsync(period, currentNetworkName)).ToList();

        try
        {
            // ── Budget insights ──────────────────────────────────────────────
            if (budgetResult != null)
            {
                var pct = budgetResult.UsedPercent;

                if (budgetResult.Status == DataSense.Models.BudgetStatus.Exceeded)
                {
                    long over = -budgetResult.RemainingBytes;
                    insights.Add(new NetworkInsight
                    {
                        Type        = InsightType.HighUsage,
                        Severity    = InsightSeverity.Critical,
                        Title       = "Monthly Budget Exceeded",
                        Description = $"You have exceeded your {FormatBytes(budgetResult.LimitBytes)} monthly allowance by {FormatBytes(over)}.",
                        Timestamp   = DateTime.UtcNow
                    });
                }
                else if (budgetResult.Status == DataSense.Models.BudgetStatus.Critical)
                {
                    insights.Add(new NetworkInsight
                    {
                        Type        = InsightType.HighUsage,
                        Severity    = InsightSeverity.Critical,
                        Title       = "Near Monthly Budget Limit",
                        Description = $"You've used {pct:F0}% of your {FormatBytes(budgetResult.LimitBytes)} monthly allowance. Only {FormatBytes(budgetResult.RemainingBytes)} remains.",
                        Timestamp   = DateTime.UtcNow
                    });
                }
                else if (budgetResult.Status == DataSense.Models.BudgetStatus.Warning)
                {
                    insights.Add(new NetworkInsight
                    {
                        Type        = InsightType.HighUsage,
                        Severity    = InsightSeverity.Warning,
                        Title       = "Monthly Budget Warning",
                        Description = $"You're using {pct:F0}% of your {FormatBytes(budgetResult.LimitBytes)} monthly data allowance.",
                        Timestamp   = DateTime.UtcNow
                    });
                }

                // Budget pace insight
                if (budgetResult.RequiredDailyPaceBytes.HasValue && budgetResult.CurrentDailyPaceBytes > 0)
                {
                    long required = budgetResult.RequiredDailyPaceBytes.Value;
                    long current  = budgetResult.CurrentDailyPaceBytes;
                    if (current > required * 1.15)  // >15% over required pace
                    {
                        insights.Add(new NetworkInsight
                        {
                            Type        = InsightType.UsageIncrease,
                            Severity    = InsightSeverity.Warning,
                            Title       = "Exceeding Budget Pace",
                            Description = $"Your current daily usage ({FormatBytes(current)}/day) exceeds the pace required to stay within your allowance ({FormatBytes(required)}/day).",
                            Timestamp   = DateTime.UtcNow
                        });
                    }
                    else if (budgetResult.Status == DataSense.Models.BudgetStatus.Healthy && current <= required)
                    {
                        insights.Add(new NetworkInsight
                        {
                            Type        = InsightType.LowUsage,
                            Severity    = InsightSeverity.Success,
                            Title       = "On Track with Budget",
                            Description = $"You're on pace to stay within your monthly allowance. Keep daily usage below {FormatBytes(required)} to finish safely.",
                            Timestamp   = DateTime.UtcNow
                        });
                    }
                }

                // Exhaustion date insight
                if (budgetResult.EstimatedExhaustionDate.HasValue &&
                    budgetResult.Status != DataSense.Models.BudgetStatus.Exceeded)
                {
                    var date = budgetResult.EstimatedExhaustionDate.Value;
                    insights.Add(new NetworkInsight
                    {
                        Type        = InsightType.HighUsage,
                        Severity    = InsightSeverity.Warning,
                        Title       = "Estimated Budget Limit Date",
                        Description = $"At your current usage rate, you are projected to reach your {FormatBytes(budgetResult.LimitBytes)} limit around {date:MMMM d}.",
                        Timestamp   = DateTime.UtcNow
                    });
                }

                // Daily budget exceeded
                if (budgetResult.HasDailyBudget && budgetResult.TodayUsedBytes > budgetResult.DailyLimitBytes)
                {
                    long over = budgetResult.TodayUsedBytes - budgetResult.DailyLimitBytes;
                    insights.Add(new NetworkInsight
                    {
                        Type        = InsightType.HighUsage,
                        Severity    = InsightSeverity.Warning,
                        Title       = "Daily Allowance Exceeded",
                        Description = $"Today's usage has exceeded your daily allowance by {FormatBytes(over)}.",
                        Timestamp   = DateTime.UtcNow
                    });
                }
            }

            // ── Forecast insights ────────────────────────────────────────────
            if (forecast?.HasSufficientData == true && budgetResult != null &&
                budgetResult.Status != DataSense.Models.BudgetStatus.Exceeded)
            {
                long limitBytes = budgetResult.LimitBytes;
                long projected  = forecast.ProjectedMonthEndBytes;

                if (projected > limitBytes)
                {
                    long overBy = projected - limitBytes;
                    insights.Add(new NetworkInsight
                    {
                        Type        = InsightType.UsageIncrease,
                        Severity    = InsightSeverity.Warning,
                        Title       = "Projected to Exceed Allowance",
                        Description = $"At your current usage rate, you are projected to exceed your {FormatBytes(limitBytes)} allowance by approximately {FormatBytes(overBy)} this month.",
                        Timestamp   = DateTime.UtcNow
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error generating budget insights: {ex}");
        }

        // Deduplicate and sort
        return insights
            .GroupBy(i => new { i.Type, i.Title })
            .Select(g => g.First())
            .OrderByDescending(i => i.Severity);
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffix = { "B", "KB", "MB", "GB", "TB" };
        int i;
        double dblSByte = bytes;
        for (i = 0; i < suffix.Length && bytes >= 1024; i++, bytes /= 1024)
        {
            dblSByte = bytes / 1024.0;
        }
        return $"{dblSByte:0.##} {suffix[i]}";
    }
}
