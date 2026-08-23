using System.Collections.Generic;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

public interface IUnifiedAnalyticsIntelligenceService
{
    Task<UnifiedSystemSummary> GetSystemSummaryAsync();
    Task<IEnumerable<UnifiedInsight>> GetUnifiedInsightsAsync();
    void InvalidateCache();
}
