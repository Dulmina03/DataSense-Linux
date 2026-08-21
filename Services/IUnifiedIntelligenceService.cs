using System.Collections.Generic;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

public interface IUnifiedIntelligenceService
{
    /// <summary>
    /// Aggregates, prioritizes, and normalizes intelligence events across all DataSense analytics engines into a single stream.
    /// </summary>
    Task<IEnumerable<IntelligenceEvent>> GetUnifiedEventsAsync(int limit = 10);

    /// <summary>
    /// Performs a deterministic self-health check on DataSense background workers, persistence, and telemetry status.
    /// </summary>
    Task<DataSenseHealthModel> GetDataSenseHealthAsync();
}
