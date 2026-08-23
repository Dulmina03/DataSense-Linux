using System.Collections.Generic;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

public interface IChartDataService
{
    Task<IEnumerable<ProcessChartItem>> GetTopProcessesAsync(AnalyticsPeriod period, int limit = 5);
    Task<IEnumerable<UsageTrendPoint>> GetUsageTrendAsync(AnalyticsPeriod period);
    Task<UsageChartItem> GetDownloadUploadDonutAsync(AnalyticsPeriod period);
}
