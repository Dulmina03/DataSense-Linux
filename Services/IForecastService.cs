using System.Collections.Generic;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

/// <summary>
/// Provides local, deterministic usage forecasting and budget management.
/// All calculations use historical SQLite data — no external calls, no AI.
/// </summary>
public interface IForecastService
{
    /// <summary>
    /// Generates a usage forecast for the current month.
    /// Returns a forecast with <c>HasSufficientData = false</c> if fewer than 3 historical days exist.
    /// </summary>
    Task<UsageForecast> GetForecastAsync();

    /// <summary>
    /// Returns one <see cref="ForecastPoint"/> per calendar day in the current month.
    /// Past days carry actual data; future days carry the forecasted daily average.
    /// </summary>
    Task<IList<ForecastPoint>> GetMonthForecastPointsAsync();

    /// <summary>Loads the persisted <see cref="DataBudget"/> (or a disabled default if none saved).</summary>
    Task<DataBudget> GetBudgetAsync();

    /// <summary>Persists the user's <see cref="DataBudget"/> settings.</summary>
    Task SaveBudgetAsync(DataBudget budget);

    /// <summary>
    /// Computes the current budget consumption result given the actual month-to-date usage.
    /// Returns null when no budget is configured or budget is disabled.
    /// </summary>
    Task<BudgetResult?> GetBudgetResultAsync(long currentMonthUsageBytes, long todayUsageBytes, long avgDailyBytes);
}
