using System.Threading.Tasks;

namespace DataSense.Services;

public interface ILinuxStartupService
{
    Task<bool> IsAutostartEnabledAsync();
    Task<bool> SetAutostartEnabledAsync(bool enable);
    string GetAutostartFilePath();
    Task<bool> VerifyAutostartFileAsync();
    Task<bool> IsSystemdUserSessionAvailableAsync();
    Task<bool> SetSystemdUserServiceEnabledAsync(bool enable);
}
