using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DataSense.Database;
using DataSense.Models;

namespace DataSense.Services;

/// <summary>
/// Local, deterministic usage forecasting using an Exponential Weighted Moving Average (EWMA)
/// over the last 30 days of historical daily data.
/// Results are cached for 5 minutes to avoid expensive recalculation on every UI tick.
/// </summary>
public class ForecastService : IForecastService
{
    private const string BudgetSettingKey  = "DataBudget";
    private const int    MinDaysRequired   = 3;
    private const int    HistoryWindowDays = 30;
    private const double EwmaDecay        = 0.9;   // weight for each additional day back
    private const double ConfidenceSigmaK  = 1.5;   // σ multiplier for forecast range
    private const int    CacheMinutes      = 5;

    private readonly INetworkUsageRepository _repository;
    private readonly IAnalyticsService       _analytics;

    // ── Cache ────────────────────────────────────────────────────────────────
    private UsageForecast?      _cachedForecast;
    private List<ForecastPoint>? _cachedPoints;
    private DataBudget?          _cachedBudget;
    private DateTime             _cacheExpiry = DateTime.MinValue;

    public ForecastService(INetworkUsageRepository repository, IAnalyticsService analytics)
    {
        _repository = repository;
        _analytics  = analytics;
    }

    // ── IForecastService ─────────────────────────────────────────────────────

    public async Task<UsageForecast> GetForecastAsync()
    {
        await EnsureCacheAsync();
        return _cachedForecast!;
    }

    public async Task<IList<ForecastPoint>> GetMonthForecastPointsAsync()
    {
        await EnsureCacheAsync();
        return _cachedPoints!;
    }

    public async Task<DataBudget> GetBudgetAsync()
    {
        if (_cachedBudget != null) return _cachedBudget;

        var json = await _repository.GetSettingAsync(BudgetSettingKey);
        if (string.IsNullOrEmpty(json))
        {
            _cachedBudget = DataBudget.Default();
            return _cachedBudget;
        }

        try
        {
            _cachedBudget = JsonSerializer.Deserialize<DataBudget>(json) ?? DataBudget.Default();
        }
        catch
        {
            _cachedBudget = DataBudget.Default();
        }

        return _cachedBudget;
    }

    public async Task SaveBudgetAsync(DataBudget budget)
    {
        budget.Validate();
        var json = JsonSerializer.Serialize(budget);
        await _repository.SaveSettingAsync(BudgetSettingKey, json);
        _cachedBudget  = budget;
        InvalidateCache();          // force forecast recalc on next fetch
    }

    public async Task<BudgetResult?> GetBudgetResultAsync(
        long currentMonthUsageBytes,
        long todayUsageBytes,
        long avgDailyBytes)
    {
        var budget = await GetBudgetAsync();
        if (!budget.Enabled || budget.MonthlyLimitBytes <= 0)
            return null;

        var utcNow          = DateTime.UtcNow;
        var monthStart      = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        int totalDaysInMonth = DateTime.DaysInMonth(utcNow.Year, utcNow.Month);
        int remainingDays    = totalDaysInMonth - utcNow.Day;  // days after today

        double usedPct   = budget.MonthlyLimitBytes > 0
            ? (double)currentMonthUsageBytes / budget.MonthlyLimitBytes * 100.0
            : 0;
        long   remaining = budget.MonthlyLimitBytes - currentMonthUsageBytes;

        var status = usedPct >= 100                      ? BudgetStatus.Exceeded
                   : usedPct >= budget.CriticalThresholdPct ? BudgetStatus.Critical
                   : usedPct >= budget.WarningThresholdPct  ? BudgetStatus.Warning
                   :                                          BudgetStatus.Healthy;

        // Estimated exhaustion date
        DateTime? exhaustionDate = null;
        if (avgDailyBytes > 0 && remaining > 0)
        {
            double daysToExhaustion = (double)remaining / avgDailyBytes;
            var    candidate        = utcNow.Date.AddDays(daysToExhaustion);
            // Only meaningful if within the month
            if (candidate.Year == utcNow.Year && candidate.Month == utcNow.Month)
                exhaustionDate = candidate;
        }
        else if (remaining <= 0)
        {
            exhaustionDate = utcNow.Date; // already exceeded
        }

        // Required daily pace to stay within budget
        long? requiredPace = null;
        if (remainingDays > 0 && remaining > 0)
            requiredPace = remaining / remainingDays;

        // Daily budget result
        bool hasDailyBudget = budget.DailyLimitBytes > 0;
        double todayPct     = hasDailyBudget && budget.DailyLimitBytes > 0
            ? (double)todayUsageBytes / budget.DailyLimitBytes * 100.0
            : 0;

        return new BudgetResult
        {
            Status                  = status,
            UsedBytes               = currentMonthUsageBytes,
            LimitBytes              = budget.MonthlyLimitBytes,
            UsedPercent             = usedPct,
            RemainingBytes          = remaining,
            EstimatedExhaustionDate = exhaustionDate,
            CurrentDailyPaceBytes   = avgDailyBytes,
            RequiredDailyPaceBytes  = requiredPace,
            HasDailyBudget          = hasDailyBudget,
            TodayUsedBytes          = todayUsageBytes,
            DailyLimitBytes         = budget.DailyLimitBytes,
            TodayUsedPercent        = todayPct,
        };
    }

    // ── Cache management ─────────────────────────────────────────────────────

    private void InvalidateCache()
    {
        _cacheExpiry    = DateTime.MinValue;
        _cachedForecast = null;
        _cachedPoints   = null;
    }

    private async Task EnsureCacheAsync()
    {
        if (_cachedForecast != null && DateTime.UtcNow < _cacheExpiry)
            return;

        (_cachedForecast, _cachedPoints) = await ComputeForecastAsync();
        _cacheExpiry = DateTime.UtcNow.AddMinutes(CacheMinutes);
    }

    // ── Core calculation ─────────────────────────────────────────────────────

    private async Task<(UsageForecast forecast, List<ForecastPoint> points)> ComputeForecastAsync()
    {
        var utcNow       = DateTime.UtcNow;
        var today        = utcNow.Date;
        var monthStart   = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        int totalDays    = DateTime.DaysInMonth(today.Year, today.Month);
        int remainingDays = totalDays - today.Day;   // days after today

        // 1. Fetch last HistoryWindowDays of completed days (excluding today)
        var histStart = today.AddDays(-HistoryWindowDays);
        var histEnd   = today.AddTicks(-1);           // up to end of yesterday
        var rawHistory = (await _repository.GetDailyUsageAsync(histStart, histEnd))
                             .OrderBy(d => d.Day)
                             .ToList();

        // Days that actually have data (non-zero)
        var activeDays = rawHistory.Where(d => d.TotalBytes > 0).ToList();
        int daysObserved = activeDays.Count;

        if (daysObserved < MinDaysRequired)
        {
            // Not enough data — return an empty/insufficient forecast
            var insufficient = new UsageForecast
            {
                HasSufficientData    = false,
                DaysObserved         = daysObserved,
                RemainingDaysInMonth = remainingDays,
                Confidence           = ForecastConfidence.Low,
            };
            var emptyPoints = BuildEmptyMonthPoints(monthStart, today, totalDays);
            return (insufficient, emptyPoints);
        }

        // 2. EWMA daily baseline (most recent day has highest weight)
        //    Reverse so index 0 = yesterday, 1 = two days ago, etc.
        var ordered = activeDays.OrderByDescending(d => d.Day).ToList();
        double weightSum  = 0;
        double weightedSum = 0;
        for (int i = 0; i < ordered.Count; i++)
        {
            double weight = Math.Pow(EwmaDecay, i);
            weightedSum  += ordered[i].TotalBytes * weight;
            weightSum    += weight;
        }
        long avgDaily = weightSum > 0 ? (long)(weightedSum / weightSum) : 0;

        // 3. Std deviation for forecast range
        double mean   = activeDays.Average(d => (double)d.TotalBytes);
        double variance = activeDays.Average(d => Math.Pow(d.TotalBytes - mean, 2));
        double stdDev   = Math.Sqrt(variance);
        double rangeFactor = ConfidenceSigmaK * stdDev * Math.Sqrt(remainingDays);

        // 4. Current month actual usage (all complete days + today partial)
        var (todayDl, todayUl) = await _repository.GetTodaySummaryAsync();
        long todayActual        = todayDl + todayUl;

        // Sum of all completed days this month
        var monthEnd  = monthStart.AddMonths(1).AddTicks(-1);
        var monthDaily = (await _repository.GetDailyUsageAsync(monthStart, today.AddTicks(-1)))
                             .ToList();
        long completedMonthBytes = monthDaily.Sum(d => d.TotalBytes);
        long currentMonthTotal   = completedMonthBytes + todayActual;

        // 5. Monthly projection
        long projected  = currentMonthTotal + (long)(avgDaily * remainingDays);
        long lowerBound = Math.Max(0, projected - (long)rangeFactor);
        long upperBound = projected + (long)rangeFactor;

        // 6. Confidence
        var confidence = daysObserved >= 14 ? ForecastConfidence.High
                       : daysObserved >= 7  ? ForecastConfidence.Medium
                       :                     ForecastConfidence.Low;

        // 7. Budget exhaustion date
        var budget = await GetBudgetAsync();
        DateTime? limitDate = null;
        long remainingAllowance = 0;
        if (budget.Enabled && budget.MonthlyLimitBytes > 0)
        {
            remainingAllowance = budget.MonthlyLimitBytes - currentMonthTotal;
            if (remainingAllowance > 0 && avgDaily > 0)
            {
                double daysToLimit = (double)remainingAllowance / avgDaily;
                var candidate = today.AddDays(daysToLimit);
                if (candidate.Year == today.Year && candidate.Month == today.Month)
                    limitDate = candidate;
            }
            else if (remainingAllowance <= 0)
            {
                limitDate = today;
            }
        }

        var forecast = new UsageForecast
        {
            HasSufficientData      = true,
            DaysObserved           = daysObserved,
            CurrentUsageBytes      = currentMonthTotal,
            AverageDailyUsageBytes = avgDaily,
            ProjectedMonthEndBytes = projected,
            LowerBoundBytes        = lowerBound,
            UpperBoundBytes        = upperBound,
            RemainingAllowanceBytes = remainingAllowance,
            EstimatedLimitDate     = limitDate,
            Confidence             = confidence,
            RemainingDaysInMonth   = remainingDays,
        };

        // 8. Build per-day chart points for the entire current month
        var points = await BuildMonthPointsAsync(
            monthStart, today, totalDays, monthDaily, todayActual, avgDaily);

        return (forecast, points);
    }

    private async Task<List<ForecastPoint>> BuildMonthPointsAsync(
        DateTime monthStart,
        DateTime today,
        int totalDays,
        List<DailyUsageRecord> completedMonthDays,
        long todayActual,
        long avgDaily)
    {
        var points = new List<ForecastPoint>(totalDays);
        var dayMap  = completedMonthDays.ToDictionary(d => d.Day.Date, d => d.TotalBytes);

        for (int i = 0; i < totalDays; i++)
        {
            var date = monthStart.AddDays(i);
            if (date.Date < today)
            {
                // Past day — use actual data
                dayMap.TryGetValue(date.Date, out long actual);
                points.Add(new ForecastPoint
                {
                    Date        = date,
                    ActualBytes = actual,
                    IsForecast  = false,
                    IsToday     = false,
                });
            }
            else if (date.Date == today)
            {
                // Today — show actual so far (partial day, no forecast overlay)
                points.Add(new ForecastPoint
                {
                    Date        = date,
                    ActualBytes = todayActual,
                    IsForecast  = false,
                    IsToday     = true,
                });
            }
            else
            {
                // Future day — forecast
                points.Add(new ForecastPoint
                {
                    Date          = date,
                    ForecastBytes = avgDaily,
                    IsForecast    = true,
                    IsToday       = false,
                });
            }
        }

        return points;
    }

    private static List<ForecastPoint> BuildEmptyMonthPoints(DateTime monthStart, DateTime today, int totalDays)
    {
        var points = new List<ForecastPoint>(totalDays);
        for (int i = 0; i < totalDays; i++)
        {
            var date = monthStart.AddDays(i);
            points.Add(new ForecastPoint
            {
                Date       = date,
                IsForecast = date.Date > today,
                IsToday    = date.Date == today,
            });
        }
        return points;
    }
}
