namespace DataSense.Models;

public class NetworkConnectionDetails
{
    public string InterfaceName { get; set; } = "Unknown";
    public string ConnectionType { get; set; } = "Unknown";
    public string ConnectionState { get; set; } = "Disconnected";
    public string ConnectionName { get; set; } = "None";
    public string Ipv4Address { get; set; } = "—";
    public string Ipv6Address { get; set; } = "—";
    public string Gateway { get; set; } = "—";
    public string DnsServers { get; set; } = "—";
    public string MacAddress { get; set; } = "—";
    public string WifiSsid { get; set; } = "—";
    public int WifiSignalStrength { get; set; } = -1; // Percentage, -1 if N/A
    public string LinkSpeed { get; set; } = "—";
}
