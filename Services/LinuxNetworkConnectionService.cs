using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DataSense.Models;

namespace DataSense.Services;

public class LinuxNetworkConnectionService : INetworkConnectionService
{
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
            // Query nmcli device details
            string deviceShowOutput = await RunCommandAsync("nmcli", $"-t device show {interfaceName}");
            if (string.IsNullOrEmpty(deviceShowOutput))
            {
                // Fallback to basic sysfs and dotnet API info if nmcli is not available
                await PopulateBasicFallbackAsync(interfaceName, details);
                return details;
            }

            var dnsList = new List<string>();
            var lines = deviceShowOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

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
                    // Format is usually e.g. "100 (connected)" or "30 (disconnected)"
                    // Extract the text inside parentheses if available, or use the raw value
                    var match = Regex.Match(value, @"\(([^)]+)\)");
                    details.ConnectionState = match.Success ? match.Groups[1].Value : value;
                }
                else if (key.StartsWith("GENERAL.CONNECTION"))
                {
                    details.ConnectionName = value;
                }
                else if (key.StartsWith("IP4.ADDRESS"))
                {
                    // Clean up e.g., "192.168.1.50/24" -> "192.168.1.50"
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
                await PopulateWifiDetailsAsync(details);
            }
            else
            {
                // Ethernet/Wired speed querying from sysfs
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

    private async Task PopulateWifiDetailsAsync(NetworkConnectionDetails details)
    {
        try
        {
            string wifiOutput = await RunCommandAsync("nmcli", "-t -f ACTIVE,SSID,SIGNAL,RATE dev wifi");
            if (string.IsNullOrEmpty(wifiOutput)) return;

            var wifiLines = wifiOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in wifiLines)
            {
                // Format: ACTIVE:SSID:SIGNAL:RATE
                // Wait! SSID could contain colons. Active column is either "yes" or "no".
                // Since ACTIVE is the first column, we check if it starts with "yes:"
                if (line.StartsWith("yes:", StringComparison.OrdinalIgnoreCase))
                {
                    // Line format: yes:SSID:SIGNAL:RATE
                    var parts = line.Split(':');
                    if (parts.Length >= 4)
                    {
                        // The last parts are RATE and SIGNAL. Let's extract them from the end.
                        string rate = parts[^1].Trim();
                        string signalStr = parts[^2].Trim();
                        
                        // SSID is everything in the middle
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
        catch
        {
            // Ignore operational errors (e.g. virtual adapters return -1 or error)
        }
    }

    private async Task PopulateBasicFallbackAsync(string interfaceName, NetworkConnectionDetails details)
    {
        try
        {
            // Minimal fallback parsing using /sys/class/net & .NET Interfaces
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

                // Gateway fallback by reading /proc/net/route
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

                // Default route destination is "00000000"
                if (iface == interfaceName && destination == "00000000")
                {
                    // Convert Hex Gateway IP (Little Endian) to string representation
                    if (uint.TryParse(gatewayHex, System.Globalization.NumberStyles.HexNumber, null, out uint ipAddress))
                    {
                        var bytes = BitConverter.GetBytes(ipAddress);
                        return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.{bytes[3]}";
                    }
                }
            }
        }
        catch
        {
            // Ignore
        }
        return string.Empty;
    }

    private async Task<string> RunCommandAsync(string command, string arguments)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return string.Empty;

            string output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return output;
        }
        catch
        {
            return string.Empty;
        }
    }
}
