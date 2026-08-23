using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

public class ChartDataService : IChartDataService
{
    private readonly IAnalyticsService _analyticsService;

    public ChartDataService(IAnalyticsService analyticsService)
    {
        _analyticsService = analyticsService ?? throw new ArgumentNullException(nameof(analyticsService));
    }

    public async Task<IEnumerable<ProcessChartItem>> GetTopProcessesAsync(AnalyticsPeriod period, int limit = 5)
    {
        var topConsumers = await _analyticsService.GetTopDataConsumersAsync(period, int.MaxValue);
        var consumerList = topConsumers.ToList();

        if (consumerList.Count == 0)
        {
            return Array.Empty<ProcessChartItem>();
        }

        var result = new List<ProcessChartItem>();
        var totalBytes = consumerList.Sum(c => c.BytesDownloaded + c.BytesUploaded);

        if (totalBytes == 0)
        {
            return Array.Empty<ProcessChartItem>();
        }

        var top = consumerList.Take(limit).ToList();
        var others = consumerList.Skip(limit).ToList();

        foreach (var p in top)
        {
            var pTotal = p.BytesDownloaded + p.BytesUploaded;
            result.Add(new ProcessChartItem
            {
                ProcessName = p.ProcessName,
                DownloadBytes = p.BytesDownloaded,
                UploadBytes = p.BytesUploaded,
                TotalBytes = pTotal,
                Percentage = (double)pTotal / totalBytes * 100.0
            });
        }

        if (others.Count > 0)
        {
            var othersDl = others.Sum(o => o.BytesDownloaded);
            var othersUl = others.Sum(o => o.BytesUploaded);
            var othersTotal = othersDl + othersUl;

            if (othersTotal > 0)
            {
                result.Add(new ProcessChartItem
                {
                    ProcessName = "Others",
                    DownloadBytes = othersDl,
                    UploadBytes = othersUl,
                    TotalBytes = othersTotal,
                    Percentage = (double)othersTotal / totalBytes * 100.0
                });
            }
        }

        return result;
    }

    public async Task<IEnumerable<UsageTrendPoint>> GetUsageTrendAsync(AnalyticsPeriod period)
    {
        var dailySeries = await _analyticsService.GetDailySeriesAsync(period);
        if (dailySeries == null || dailySeries.Count == 0)
        {
            return Array.Empty<UsageTrendPoint>();
        }

        return dailySeries.Select(d => new UsageTrendPoint
        {
            Timestamp = d.Day,
            DownloadBytes = d.BytesDownloaded,
            UploadBytes = d.BytesUploaded,
            TotalBytes = d.TotalBytes
        }).OrderBy(x => x.Timestamp).ToList();
    }

    public async Task<UsageChartItem> GetDownloadUploadDonutAsync(AnalyticsPeriod period)
    {
        var summary = await _analyticsService.GetSummaryAsync(period);
        if (summary == null || summary.TotalUsage == 0)
        {
            return new UsageChartItem { Label = period.ToString() };
        }

        return new UsageChartItem
        {
            Label = period.ToString(),
            DownloadBytes = summary.TotalDownloaded,
            UploadBytes = summary.TotalUploaded,
            TotalBytes = summary.TotalUsage,
            Percentage = 100.0
        };
    }
}
