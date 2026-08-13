using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

public interface INetworkConnectionService
{
    Task<NetworkConnectionDetails> GetConnectionDetailsAsync(string interfaceName);
}
