using System;
using DataSense.Models;

namespace DataSense.Services;

public interface INetworkMonitorWorker
{
    string? ActiveInterface { get; }
    double DownloadSpeed { get; }
    double UploadSpeed { get; }
    long TotalBytesDownloaded { get; }
    long TotalBytesUploaded { get; }
    long TotalDataUsage { get; } // Total downloaded + uploaded bytes
    NetworkUsage? LatestUsage { get; }
    
    event Action<NetworkUsage>? NetworkUsageUpdated;

    void Start();
    void Stop();
}
