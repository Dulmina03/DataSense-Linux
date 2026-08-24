using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

public interface INetworkIdentityService
{
    Task<NetworkIdentity> GetCurrentIdentityAsync(string interfaceName);
    NetworkIdentity GetLastKnownIdentity(string interfaceName);
    string NormalizeNetworkName(string? rawName, string? interfaceName = null);
    string GetCanonicalKey(string? rawName, string? interfaceName = null);
    bool IsValidNetworkName(string? name);
}
