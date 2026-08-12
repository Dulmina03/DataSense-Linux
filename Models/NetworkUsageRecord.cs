using System;

namespace DataSense.Models;

public class NetworkUsageRecord
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string InterfaceName { get; set; } = string.Empty;
    public double DownloadSpeed { get; set; }
    public double UploadSpeed { get; set; }
    public long BytesReceived { get; set; }
    public long BytesSent { get; set; }
}
