using System;
using System.Threading;
using System.Threading.Tasks;

namespace DataSense.Services;

public class SpeedTestResult
{
    public double DownloadSpeedMbps { get; set; }
    public double UploadSpeedMbps { get; set; }
    public double PingMs { get; set; }
    public double JitterMs { get; set; }
    public string ServerName { get; set; } = string.Empty;
}

public interface ISpeedTestService
{
    Task<double> TestPingAsync(CancellationToken cancellationToken);
    Task<double> TestDownloadAsync(Action<double> progressCallback, CancellationToken cancellationToken);
    Task<double> TestUploadAsync(Action<double> progressCallback, CancellationToken cancellationToken);
}
