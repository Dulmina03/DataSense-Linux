using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DataSense.Helpers;
using DataSense.Models;

namespace DataSense.Services;

public class LinuxNetworkConnectionService : INetworkConnectionService
{
    private readonly ILinuxPlatformService? _platformService;

    public LinuxNetworkConnectionService(ILinuxPlatformService? platformService = null)
    {
        _platformService = platformService;
    }

    public async Task<NetworkConnectionDetails> GetConnectionDetailsAsync(string interfaceName)
    {
        var details = new NetworkConnectionDetails
        {
            InterfaceName = interfaceName
        };

        if (string.IsNullOrEmpty(interfaceName) || interfaceName == "None" || interfaceName == "Disconnected")
        {
            return details;
        }

        try
        {
            // If nmcli is not available on platform, degrade gracefully to basic sysfs/dotnet API
            if (_platformService != null && !_platformService.HasNmcli)
            {
                await PopulateBasicFallbackAsync(interfaceName, details);
                return details;
            }

            string nmcliExec = _platformService?.GetExecutablePath("nmcli") ?? "nmcli";

            // Query nmcli device details safely
            var result = await ProcessExecutionHelper.ExecuteAsync(nmcliExec, new[] { "-t", "device", "show", interfaceName }, timeoutMs: 2000);
            if (!result.Success || string.IsNullOrEmpty(result.StandardOutput))
            {
                await PopulateBasicFallbackAsync(interfaceName, details);
                return details;
            }

            var dnsList = new List<string>();
            var lines = result.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var parts = line.Split(':', 2);
                if (parts.Length < 2) continue;

                var key = parts[0].Trim();
                var value = parts[1].Trim();

                if (string.IsNullOrEmpty(value) || value == "--") continue;

                if (key.StartsWith("GENERAL.TYPE"))
                {
                    details.ConnectionType = value;
                }
                else if (key.StartsWith("GENERAL.HWADDR"))
                {
                    details.MacAddress = value;
                }
                else if (key.StartsWith("GENERAL.STATE"))
                {
                    var match = Regex.Match(value, @"\(([^)]+)\)");
                    details.ConnectionState = match.Success ? match.Groups[1].Value : value;
                }
                else if (key.StartsWith("GENERAL.CONNECTION"))
                {
                    details.ConnectionName = value;
                }
                else if (key.StartsWith("IP4.ADDRESS"))
                {
                    details.Ipv4Address = value.Split('/')[0];
                }
                else if (key.StartsWith("IP4.GATEWAY"))
                {
                    details.Gateway = value;
                }
                else if (key.StartsWith("IP4.DNS"))
                {
                    dnsList.Add(value);
                }
                else if (key.StartsWith("IP6.ADDRESS"))
                {
                    details.Ipv6Address = value.Split('/')[0];
                }
            }

            if (dnsList.Count > 0)
            {
                details.DnsServers = string.Join(", ", dnsList);
            }

            // Wi-Fi specific details querying
            if (details.ConnectionType.Equals("wifi", StringComparison.OrdinalIgnoreCase))
            {
                await PopulateWifiDetailsAsync(nmcliExec, details);
            }
            else
            {
                await PopulateEthernetSpeedAsync(interfaceName, details);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting connection details: {ex.Message}");
            await PopulateBasicFallbackAsync(interfaceName, details);
        }

        return details;
    }

    private async Task PopulateWifiDetailsAsync(string nmcliExec, NetworkConnectionDetails details)
    {
        try
        {
            var result = await ProcessExecutionHelper.ExecuteAsync(nmcliExec, new[] { "-t", "-f", "ACTIVE,SSID,SIGNAL,RATE", "dev", "wifi" }, timeoutMs: 2000);
            if (!result.Success || string.IsNullOrEmpty(result.StandardOutput)) return;

            var wifiLines = result.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in wifiLines)
            {
                if (line.StartsWith("yes:", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(':');
                    if (parts.Length >= 4)
                    {
                        string rate = parts[^1].Trim();
                        string signalStr = parts[^2].Trim();
                        string ssid = string.Join(":", parts.Skip(1).Take(parts.Length - 3)).Trim();

                        details.WifiSsid = ssid;
                        details.LinkSpeed = rate;
                        if (int.TryParse(signalStr, out int signal))
                        {
                            details.WifiSignalStrength = signal;
                        }
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error fetching wifi details: {ex.Message}");
        }
    }

    private async Task PopulateEthernetSpeedAsync(string interfaceName, NetworkConnectionDetails details)
    {
        try
        {
            string speedPath = $"/sys/class/net/{interfaceName}/speed";
            if (File.Exists(speedPath))
            {
                string speedText = (await File.ReadAllTextAsync(speedPath)).Trim();
                if (int.TryParse(speedText, out int speedValue) && speedValue > 0)
                {
                    details.LinkSpeed = $"{speedValue} Mbit/s";
                }
            }
        }
        catch { /* Ignore operational errors */ }
    }

    private async Task PopulateBasicFallbackAsync(string interfaceName, NetworkConnectionDetails details)
    {
        try
        {
            var netInterface = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(i => i.Name == interfaceName);

            if (netInterface != null)
            {
                details.MacAddress = string.Join(":", netInterface.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));
                details.ConnectionState = netInterface.OperationalStatus.ToString();
                details.ConnectionType = netInterface.NetworkInterfaceType.ToString();

                var ipProps = netInterface.GetIPProperties();
                var ipv4 = ipProps.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                if (ipv4 != null)
                {
                    details.Ipv4Address = ipv4.Address.ToString();
                }

                var ipv6 = ipProps.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6);
                if (ipv6 != null)
                {
                    details.Ipv6Address = ipv6.Address.ToString();
                }

                if (ipProps.DnsAddresses.Count > 0)
                {
                    details.DnsServers = string.Join(", ", ipProps.DnsAddresses.Select(d => d.ToString()));
                }

                string gateway = await GetDefaultGatewayFromRouteAsync(interfaceName);
                if (!string.IsNullOrEmpty(gateway))
                {
                    details.Gateway = gateway;
                }

                await PopulateEthernetSpeedAsync(interfaceName, details);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Fallback population failed: {ex.Message}");
        }
    }

    private async Task<string> GetDefaultGatewayFromRouteAsync(string interfaceName)
    {
        const string routePath = "/proc/net/route";
        if (!File.Exists(routePath)) return string.Empty;

        try
        {
            var lines = await File.ReadAllLinesAsync(routePath);
            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length < 8) continue;

                var iface = parts[0];
                var destination = parts[1];
                var gatewayHex = parts[2];

                if (iface == interfaceName && destination == "00000000")
                {
                    if (uint.TryParse(gatewayHex, System.Globalization.NumberStyles.HexNumber, null, out uint ipAddress))
                    {
                        var bytes = BitConverter.GetBytes(ipAddress);
                        return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.{bytes[3]}";
                    }
                }
            }
        }
        catch { }
        return string.Empty;
    }
}
