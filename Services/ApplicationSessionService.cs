using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataSense.Database;
using DataSense.Models;

namespace DataSense.Services;

public class ApplicationSessionService : IApplicationSessionService
{
    private readonly INetworkUsageRepository _repository;
    private readonly ISystemHealthRegistry _healthRegistry;
    
    private readonly ConcurrentDictionary<string, ApplicationLifecycleSummary> _lifecycleCache = new();
    private DateTime _lastCacheUpdate = DateTime.MinValue;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    
    public ApplicationSessionService(INetworkUsageRepository repository, ISystemHealthRegistry healthRegistry)
    {
        _repository = repository;
        _healthRegistry = healthRegistry;
    }

    public async Task<IEnumerable<ApplicationSession>> GetProcessSessionsAsync(string processName, int pid, long startTimeTicks, DateTime start, DateTime end)
    {
        try
        {
            var records = await _repository.GetProcessUsageIdentitiesAsync(start, end);
            var filtered = records.Where(r => r.ProcessName == processName && r.Pid == pid && r.StartTimeTicks == startTimeTicks).OrderBy(r => r.Timestamp).ToList();
            
            var sessions = new List<ApplicationSession>();
            if (filtered.Count == 0) return sessions;
            
            ApplicationSession? currentSession = null;
            DateTime lastTime = DateTime.MinValue;
            
            foreach (var r in filtered)
            {
                if (currentSession == null || (r.Timestamp - lastTime).TotalMinutes > 15)
                {
                    if (currentSession != null)
                    {
                        sessions.Add(currentSession);
                    }
                    
                    currentSession = new ApplicationSession
                    {
                        ProcessName = r.ProcessName,
                        Pid = r.Pid,
                        StartTimeTicks = r.StartTimeTicks,
                        ExecutablePath = r.ExecutablePath,
                        UserName = r.UserName,
                        DataSource = r.DataSource,
                        SessionStart = r.Timestamp,
                        SessionEnd = r.Timestamp,
                        NetworkName = "Unknown",
                        DownloadBytes = 0,
                        UploadBytes = 0,
                        IsActive = true
                    };
                }
                
                currentSession.SessionEnd = r.Timestamp;
                currentSession.DownloadBytes += r.BytesDownloaded;
                currentSession.UploadBytes += r.BytesUploaded;
                
                lastTime = r.Timestamp;
            }
            
            if (currentSession != null)
            {
                currentSession.IsActive = (DateTime.UtcNow - currentSession.SessionEnd).TotalMinutes <= 15;
                sessions.Add(currentSession);
            }
            
            return sessions;
        }
        catch (Exception ex)
        {
            _healthRegistry.ReportHealth("ApplicationSessionService", SubsystemState.Error, "Failed to get process sessions", ex);
            return Enumerable.Empty<ApplicationSession>();
        }
    }

    public async Task<ApplicationLifecycleSummary?> GetApplicationLifecycleAsync(string processName)
    {
        await EnsureCachePopulatedAsync();
        _lifecycleCache.TryGetValue(processName, out var summary);
        return summary;
    }

    public async Task<IEnumerable<ApplicationLifecycleSummary>> GetAllLifecyclesAsync()
    {
        await EnsureCachePopulatedAsync();
        return _lifecycleCache.Values.ToList();
    }

    public Task InvalidateCacheAsync()
    {
        _lifecycleCache.Clear();
        _lastCacheUpdate = DateTime.MinValue;
        return Task.CompletedTask;
    }
    
    private async Task EnsureCachePopulatedAsync()
    {
        if ((DateTime.UtcNow - _lastCacheUpdate).TotalMinutes < 5 && !_lifecycleCache.IsEmpty)
        {
            return;
        }

        await _cacheLock.WaitAsync();
        try
        {
            if ((DateTime.UtcNow - _lastCacheUpdate).TotalMinutes < 5 && !_lifecycleCache.IsEmpty)
            {
                return;
            }
            
            _lifecycleCache.Clear();
            var records = await _repository.GetProcessUsageIdentitiesAsync(DateTime.UtcNow.AddDays(-30), DateTime.UtcNow);
            
            var byProcess = records.GroupBy(r => r.ProcessName);
            foreach (var group in byProcess)
            {
                var pRecords = group.OrderBy(r => r.Timestamp).ToList();
                if (pRecords.Count == 0) continue;
                
                var first = pRecords.First();
                var last = pRecords.Last();
                
                var todayRecords = pRecords.Where(r => r.Timestamp >= DateTime.UtcNow.Date).ToList();
                var sevenDayRecords = pRecords.Where(r => r.Timestamp >= DateTime.UtcNow.Date.AddDays(-6)).ToList();
                
                var summary = new ApplicationLifecycleSummary
                {
                    ProcessName = group.Key,
                    FirstObserved = first.Timestamp,
                    LastObserved = last.Timestamp,
                    TotalSessions = 1,
                    TotalActiveDuration = last.Timestamp - first.Timestamp,
                    AverageSessionDuration = last.Timestamp - first.Timestamp,
                    LongestSession = last.Timestamp - first.Timestamp,
                    TodaySessionCount = todayRecords.Count > 0 ? 1 : 0,
                    TodayActiveDuration = todayRecords.Count > 0 ? todayRecords.Last().Timestamp - todayRecords.First().Timestamp : TimeSpan.Zero,
                    TodayUsage = todayRecords.Sum(r => r.BytesDownloaded + r.BytesUploaded),
                    SevenDaySessionCount = sevenDayRecords.Count > 0 ? 1 : 0,
                    SevenDayActiveDuration = sevenDayRecords.Count > 0 ? sevenDayRecords.Last().Timestamp - sevenDayRecords.First().Timestamp : TimeSpan.Zero,
                    SevenDayUsage = sevenDayRecords.Sum(r => r.BytesDownloaded + r.BytesUploaded),
                    IsCurrentlyActive = (DateTime.UtcNow - last.Timestamp).TotalMinutes <= 15
                };
                
                _lifecycleCache[group.Key] = summary;
            }
            
            _lastCacheUpdate = DateTime.UtcNow;
            _healthRegistry.ReportHealth("ApplicationSessionService", SubsystemState.Healthy, "Session Cache Populated");
        }
        catch (Exception ex)
        {
            _healthRegistry.ReportHealth("ApplicationSessionService", SubsystemState.Error, "Session Cache Error", ex);
        }
        finally
        {
            _cacheLock.Release();
        }
    }
}
