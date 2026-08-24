using System;
using DataSense.Models;
using DataSense.Services;
using Xunit;

namespace DataSense.Tests.Services;

public class LinuxApplicationIconServiceTests
{
    [Fact]
    public void GenericApplicationIcon_IsNotNull()
    {
        var service = new LinuxApplicationIconService();
        Assert.NotNull(service.GenericApplicationIcon);
    }

    [Theory]
    [InlineData("brave", "Brave Web Browser")]
    [InlineData("code", "Visual Studio Code")]
    [InlineData("steam", "Steam")]
    [InlineData("discord", "Discord")]
    [InlineData("node", "Node.js")]
    [InlineData("dotnet", ".NET Runtime")]
    [InlineData("python3", "Python 3")]
    [InlineData("curl", "cURL")]
    public void GetApplicationDisplayName_ResolvesKnownApplicationAliases(string identifier, string expectedDisplayName)
    {
        var service = new LinuxApplicationIconService();
        string displayName = service.GetApplicationDisplayName(identifier);
        Assert.Equal(expectedDisplayName, displayName);
    }

    [Fact]
    public void GetApplicationIcon_NeverReturnsNull_EvenForUnknownProcess()
    {
        var service = new LinuxApplicationIconService();
        var icon = service.GetApplicationIcon("some_unknown_background_daemon_xyz");
        Assert.NotNull(icon);
    }

    [Fact]
    public void ProcessNetworkUsage_HasDistinctProcessIdentifier_EvaluatesCorrectly()
    {
        var item1 = new ProcessNetworkUsage
        {
            ProcessIdentifier = "brave",
            ApplicationDisplayName = "Brave Web Browser"
        };
        Assert.True(item1.HasDistinctProcessIdentifier);
        Assert.Equal("Brave Web Browser", item1.EffectiveDisplayName);

        var item2 = new ProcessNetworkUsage
        {
            ProcessIdentifier = "nethogs",
            ApplicationDisplayName = "nethogs"
        };
        Assert.False(item2.HasDistinctProcessIdentifier);
        Assert.Equal("nethogs", item2.EffectiveDisplayName);

        var item3 = new ProcessNetworkUsage
        {
            ProcessIdentifier = "curl",
            ApplicationDisplayName = ""
        };
        Assert.False(item3.HasDistinctProcessIdentifier);
        Assert.Equal("curl", item3.EffectiveDisplayName);
    }
}
