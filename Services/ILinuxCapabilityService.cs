using System.Collections.Generic;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

public interface ILinuxCapabilityService
{
    Task<IReadOnlyList<LinuxCapabilityItem>> AssessCapabilitiesAsync();
    Task<LinuxCapabilityItem> AssessNethogsCapabilityAsync();
}
