using System;

namespace DataSense.Models;

public class SpeedTestRecord
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public double DownloadSpeedMbps { get; set; }
    public double UploadSpeedMbps { get; set; }
    public double PingMs { get; set; }
    public double JitterMs { get; set; }
    public string ServerName { get; set; } = string.Empty;
}
