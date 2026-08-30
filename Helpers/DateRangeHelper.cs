using System;

namespace DataSense.Helpers;

/// <summary>
/// Authoritative helper for local calendar day and month boundaries.
/// Guarantees that "Today" across all services, repositories, ViewModels, and UI surfaces
/// uses the exact same local calendar day boundaries converted to UTC.
/// </summary>
public static class DateRangeHelper
{
    /// <summary>
    /// Returns UTC start and end timestamps corresponding to a specific local calendar date.
    /// </summary>
    public static (DateTime startUtc, DateTime endUtc) GetLocalDayRange(DateTime localDate)
    {
        var localStart = new DateTime(localDate.Year, localDate.Month, localDate.Day, 0, 0, 0, DateTimeKind.Local);
        var startUtc = localStart.ToUniversalTime();
        var endUtc = localStart.AddDays(1).AddTicks(-1).ToUniversalTime();
        return (startUtc, endUtc);
    }

    /// <summary>
    /// Returns UTC start and end timestamps corresponding to today's local calendar day.
    /// </summary>
    public static (DateTime startUtc, DateTime endUtc) GetLocalTodayRange()
    {
        return GetLocalDayRange(DateTime.Today);
    }

    /// <summary>
    /// Returns UTC start and end timestamps corresponding to a local calendar month.
    /// </summary>
    public static (DateTime startUtc, DateTime endUtc) GetLocalMonthRange(int year, int month)
    {
        var localStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Local);
        var startUtc = localStart.ToUniversalTime();
        var endUtc = localStart.AddMonths(1).AddTicks(-1).ToUniversalTime();
        return (startUtc, endUtc);
    }
}
