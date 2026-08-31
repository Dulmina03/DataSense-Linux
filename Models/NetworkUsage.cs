using System;

namespace DataSense.Models;

public class NetworkUsage
{
    public string InterfaceName { get; set; } = string.Empty;
    public long BytesReceived { get; set; }
    public long BytesSent { get; set; }
    public double DownloadSpeed { get; set; } // Bytes/sec
    public double UploadSpeed { get; set; }   // Bytes/sec
    public long DownloadDelta { get; set; }   // Bytes transferred in interval
    public long UploadDelta { get; set; }     // Bytes transferred in interval
    public DateTime Timestamp { get; set; }
}
