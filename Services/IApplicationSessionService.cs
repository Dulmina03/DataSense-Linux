using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

public interface IApplicationSessionService
{
    Task<IEnumerable<ApplicationSession>> GetProcessSessionsAsync(string processName, int pid, long startTimeTicks, DateTime start, DateTime end);
    Task<ApplicationLifecycleSummary?> GetApplicationLifecycleAsync(string processName);
    Task<IEnumerable<ApplicationLifecycleSummary>> GetAllLifecyclesAsync();
    Task InvalidateCacheAsync();
}
