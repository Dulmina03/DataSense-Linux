using System;
using DataSense.Services;
using Xunit;

namespace DataSense.Tests.Services;

public class LinuxNetworkUsageCollectorTests
{
    [Theory]
    [InlineData("lo", true)]
    [InlineData("veth1234", true)]
    [InlineData("docker0", true)]
    [InlineData("br-abcdef", true)]
    [InlineData("virbr0", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("wlan0", false)]
    [InlineData("wlp3s0", false)]
    [InlineData("eth0", false)]
    [InlineData("enp0s31f6", false)]
    [InlineData("tun0", false)]
    [InlineData("wg0", false)]
    [InlineData("usb0", false)]
    [InlineData("wwan0", false)]
    public void ShouldExcludeInterface_AppliesCorrectExclusionPolicy(string? iface, bool expectedExcluded)
    {
        bool result = LinuxNetworkUsageCollector.ShouldExcludeInterface(iface!);
        Assert.Equal(expectedExcluded, result);
    }

    [Theory]
    [InlineData("wlan0", "WiFi")]
    [InlineData("wlp2s0", "WiFi")]
    [InlineData("wifi0", "WiFi")]
    [InlineData("eth0", "Ethernet")]
    [InlineData("enp3s0", "Ethernet")]
    [InlineData("eno1", "Ethernet")]
    [InlineData("tun0", "VPN")]
    [InlineData("tap0", "VPN")]
    [InlineData("wg0", "VPN")]
    [InlineData("usb0", "Cellular")]
    [InlineData("wwan0", "Cellular")]
    [InlineData("dummy0", "Other")]
    public void ClassifyConnectionType_CorrectlyIdentifiesInterfaceTypes(string iface, string expectedType)
    {
        string type = LinuxNetworkUsageCollector.ClassifyConnectionType(iface);
        Assert.Equal(expectedType, type);
    }
}
