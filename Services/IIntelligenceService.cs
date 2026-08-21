using System.Collections.Generic;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

public interface IIntelligenceService
{
    Task<IEnumerable<NetworkInsight>> GenerateInsightsAsync(AnalyticsPeriod period, string? currentNetworkName);

    /// <summary>
    /// Generates insights that include budget and forecast awareness.
    /// Pass the active budget result and forecast (both may be null when no budget is configured
    /// or insufficient data exists).
    /// </summary>
    Task<IEnumerable<NetworkInsight>> GenerateInsightsWithBudgetAsync(
        AnalyticsPeriod period,
        string?        currentNetworkName,
        BudgetResult?  budgetResult,
        UsageForecast? forecast);
}
