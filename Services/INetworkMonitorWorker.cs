using System;
using DataSense.Models;

namespace DataSense.Services;

public interface INetworkMonitorWorker
{
    string? ActiveInterface { get; }
    double DownloadSpeed { get; }
    double UploadSpeed { get; }
    
    event Action<NetworkUsage>? NetworkUsageUpdated;

    void Start();
    void Stop();
}
